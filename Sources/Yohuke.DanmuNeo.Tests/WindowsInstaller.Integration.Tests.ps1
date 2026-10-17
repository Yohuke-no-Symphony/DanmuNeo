#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.7.1' }

BeforeDiscovery {
    $canRunInstallerIntegration = $false

    if ($IsWindows)
    {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $canRunInstallerIntegration = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -and
            [bool](Get-Command makensis -CommandType Application -ErrorAction SilentlyContinue)
    }
}

Describe 'Native Windows installer lifecycle' -Tag InstallerIntegration -Skip:(!$canRunInstallerIntegration) {
    BeforeAll {
        $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $fixtureRoot = Join-Path $TestDrive 'installer fixture'
        $tools = Join-Path $fixtureRoot 'tools'
        $payload = Join-Path $fixtureRoot 'payload'
        $output = Join-Path $fixtureRoot 'output'
        $desktop = Join-Path $fixtureRoot 'desktop'
        $fixtureId = [guid]::NewGuid().ToString('N')
        $registryKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\Yohuke.DanmuNeo.Test-$fixtureId"
        $startMenuFolder = "Danmu Neo Test $fixtureId"
        $shortcut = Join-Path $desktop 'Danmu Neo.lnk'

        foreach ($directory in @($tools, $payload, $output, $desktop, (Join-Path $payload 'sub folder'), (Join-Path $payload 'i18n')))
        {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }

        Copy-Item -LiteralPath (Join-Path $repository 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.ps1') -Destination $tools
        $template = Get-Content -LiteralPath (Join-Path $repository 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.nsi') -Raw
        $template = $template.Replace('Software\Microsoft\Windows\CurrentVersion\Uninstall\Yohuke.DanmuNeo', $registryKey)
        $template = $template.Replace('$SMPROGRAMS\Danmu Neo', ('$SMPROGRAMS\' + $startMenuFolder))
        $template = $template.Replace('$DESKTOP\Danmu Neo.lnk', $shortcut)
        [IO.File]::WriteAllText((Join-Path $tools 'Yohuke.DanmuNeo.WindowsInstaller.nsi'), $template, [Text.UTF8Encoding]::new($false))
        . (Join-Path $tools 'Yohuke.DanmuNeo.WindowsInstaller.ps1')
        $compiler = Resolve-WindowsNsisCompiler
        $icon = Join-Path $repository 'Sources/Yohuke.DanmuNeo/Assets/favicon.ico'
        Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32/cmd.exe') -Destination (Join-Path $payload 'Yohuke.DanmuNeo.exe')
        Set-Content -LiteralPath (Join-Path $payload 'obsolete.txt') -Value 'old owned file'
        Set-Content -LiteralPath (Join-Path $payload 'sub folder/$literal-dollar.txt') -Value 'literal dollar payload'
        Set-Content -LiteralPath (Join-Path $payload 'i18n/en-US.json') -Value '{"fixture":true}'
        $installerV1 = New-WindowsInstaller -PayloadDirectory $payload -PublishDirectory $output -ProductVersion '1.2.3' `
            -NsisCompiler $compiler.Path -IconPath $icon
        $v1Hashes = @{}

        foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File)
        {
            $v1Hashes[[IO.Path]::GetRelativePath($payload, $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName).Hash
        }

        Remove-Item -LiteralPath (Join-Path $payload 'obsolete.txt')
        Set-Content -LiteralPath (Join-Path $payload 'new-owned.txt') -Value 'new version owned file'
        $installerV2 = New-WindowsInstaller -PayloadDirectory $payload -PublishDirectory $output -ProductVersion '1.2.4.5' `
            -NsisCompiler $compiler.Path -IconPath $icon

        function Invoke-InstallerProcess
        {
            param([string]$FilePath, [string]$Arguments)

            $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru

            try
            {
                if (-not $process.WaitForExit(60000))
                {
                    $process.Kill($true)
                    $process.WaitForExit()
                    throw "Installer process exceeded 60 seconds: $FilePath"
                }

                return $process.ExitCode
            }
            finally
            {
                $process.Dispose()
            }
        }

        function Get-FixtureRegistration
        {
            param([string]$Mode)

            $hive = if ($Mode -eq 'AllUsers') { [Microsoft.Win32.RegistryHive]::LocalMachine } else { [Microsoft.Win32.RegistryHive]::CurrentUser }
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)

            try
            {
                $key = $root.OpenSubKey($registryKey)

                if ($null -eq $key)
                {
                    return $null
                }

                try
                {
                    return [pscustomobject]@{
                        InstallLocation = $key.GetValue('InstallLocation')
                        DisplayVersion = $key.GetValue('DisplayVersion')
                        UninstallString = $key.GetValue('UninstallString')
                        QuietUninstallString = $key.GetValue('QuietUninstallString')
                    }
                }
                finally
                {
                    $key.Dispose()
                }
            }
            finally
            {
                $root.Dispose()
            }
        }

        function Get-FixtureShortcut
        {
            param([string]$Path)

            $shell = New-Object -ComObject WScript.Shell

            try
            {
                $link = $shell.CreateShortcut($Path)

                try
                {
                    return [pscustomobject]@{
                        TargetPath = $link.TargetPath
                        WorkingDirectory = $link.WorkingDirectory
                        Arguments = $link.Arguments
                    }
                }
                finally
                {
                    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) | Out-Null
                }
            }
            finally
            {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
            }
        }
    }

    BeforeEach {
        $installation = Join-Path $fixtureRoot ('安装 with spaces $literal ' + [guid]::NewGuid().ToString('N'))
        $additionalInstallation = Join-Path $fixtureRoot ('second installation ' + [guid]::NewGuid().ToString('N'))
    }

    AfterEach {
        $cleanupErrors = [Collections.Generic.List[string]]::new()

        foreach ($directory in @($installation, $additionalInstallation))
        {
            $uninstaller = Join-Path $directory 'Uninstall.exe'

            try
            {
                if ((Test-Path -LiteralPath $uninstaller) -and (Test-Path -LiteralPath (Join-Path $directory 'install-state.ini')))
                {
                    $code = Invoke-InstallerProcess -FilePath $uninstaller -Arguments "/S _?=$directory"

                    if ($code -ne 0)
                    {
                        $cleanupErrors.Add("Fixture cleanup failed with exit code $code at $directory.")
                    }
                }

            }
            catch
            {
                $cleanupErrors.Add($_.Exception.Message)
            }
        }

        try
        {
            if (Test-Path -LiteralPath $shortcut)
            {
                Remove-Item -LiteralPath $shortcut -Force
            }
        }
        catch
        {
            $cleanupErrors.Add($_.Exception.Message)
        }

        foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine))
        {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)

            try
            {
                $key = $root.OpenSubKey($registryKey)

                if ($null -ne $key)
                {
                    $key.Dispose()
                    $cleanupErrors.Add("Uninstaller left its fixture registration in $hive.")
                    $root.DeleteSubKeyTree($registryKey, $false)
                }
            }
            catch
            {
                $cleanupErrors.Add($_.Exception.Message)
            }
            finally
            {
                $root.Dispose()
            }
        }

        foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('CommonPrograms')))
        {
            $startMenu = Join-Path $folder $startMenuFolder

            try
            {
                if (Test-Path -LiteralPath $startMenu)
                {
                    $cleanupErrors.Add("Uninstaller left its fixture start menu folder at $startMenu.")
                    Remove-Item -LiteralPath $startMenu -Recurse -Force
                }
            }
            catch
            {
                $cleanupErrors.Add($_.Exception.Message)
            }
        }

        if ($cleanupErrors.Count -gt 0)
        {
            throw ($cleanupErrors -join [Environment]::NewLine)
        }
    }

    It 'installs and removes the complete payload for <Scope>, desktop=<Enabled>' -ForEach @(
        @{ Scope = 'CurrentUser'; Enabled = $false }
        @{ Scope = 'CurrentUser'; Enabled = $true }
        @{ Scope = 'AllUsers'; Enabled = $false }
        @{ Scope = 'AllUsers'; Enabled = $true }
    ) {
        $scopeOption = if ($Scope -eq 'AllUsers') { ' /AllUsers' } else { '' }
        $desktopOption = if ($Enabled) { ' /DesktopShortcut' } else { '' }
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S$scopeOption$desktopOption /D=$installation" | Should -Be 0
        $state = Get-Content -LiteralPath (Join-Path $installation 'install-state.ini') -Raw
        $state | Should -Match "(?m)^Mode=$Scope\s*$"
        $state | Should -Match "(?m)^DesktopShortcut=$([int]$Enabled)\s*$"
        Test-Path -LiteralPath $shortcut | Should -Be $Enabled

        foreach ($relative in $v1Hashes.Keys)
        {
            (Get-FileHash -LiteralPath (Join-Path $installation $relative)).Hash | Should -Be $v1Hashes[$relative]
        }

        $registration = Get-FixtureRegistration -Mode $Scope
        $registration.InstallLocation | Should -Be $installation
        $registration.DisplayVersion | Should -Be '1.2.3'
        $registration.UninstallString | Should -Be ('"' + (Join-Path $installation 'Uninstall.exe') + '" /' + $Scope)
        $registration.QuietUninstallString | Should -Be ('"' + (Join-Path $installation 'Uninstall.exe') + '" /S /' + $Scope)
        $programs = if ($Scope -eq 'AllUsers') { [Environment]::GetFolderPath('CommonPrograms') } else { [Environment]::GetFolderPath('Programs') }
        $menuShortcut = Join-Path $programs "$startMenuFolder/Danmu Neo.lnk"
        $uninstallShortcut = Join-Path $programs "$startMenuFolder/Uninstall.lnk"
        Test-Path -LiteralPath $menuShortcut | Should -BeTrue
        Test-Path -LiteralPath $uninstallShortcut | Should -BeTrue
        $applicationLink = Get-FixtureShortcut -Path $menuShortcut
        $applicationLink.TargetPath | Should -Be (Join-Path $installation 'Yohuke.DanmuNeo.exe')
        $applicationLink.WorkingDirectory | Should -Be $installation
        $uninstallLink = Get-FixtureShortcut -Path $uninstallShortcut
        $uninstallLink.TargetPath | Should -Be (Join-Path $installation 'Uninstall.exe')
        $uninstallLink.Arguments | Should -Be "/$Scope"

        if ($Enabled)
        {
            $link = Get-FixtureShortcut -Path $shortcut
            $link.TargetPath | Should -Be (Join-Path $installation 'Yohuke.DanmuNeo.exe')
            $link.WorkingDirectory | Should -Be $installation
        }

        Invoke-InstallerProcess -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S /$Scope _?=$installation" | Should -Be 0

        foreach ($relative in $v1Hashes.Keys)
        {
            Test-Path -LiteralPath (Join-Path $installation $relative) | Should -BeFalse
        }

        Test-Path -LiteralPath (Join-Path $installation 'install-state.ini') | Should -BeFalse
        Test-Path -LiteralPath $shortcut | Should -BeFalse
    }

    It 'upgrades owned files, removes obsolete files and preserves extra files' {
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /DesktopShortcut /D=$installation" | Should -Be 0
        $extra = Join-Path $installation 'personal/notes.txt'
        New-Item -ItemType Directory -Path (Split-Path $extra -Parent) | Out-Null
        Set-Content -LiteralPath $extra -Value 'user-owned content'

        Invoke-InstallerProcess -FilePath $installerV2 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0

        Test-Path -LiteralPath (Join-Path $installation 'obsolete.txt') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installation 'new-owned.txt') | Should -BeTrue
        (Get-FixtureRegistration -Mode CurrentUser).DisplayVersion | Should -Be '1.2.4.5'
        Get-Content -LiteralPath $extra | Should -Be 'user-owned content'
        Invoke-InstallerProcess -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S _?=$installation" | Should -Be 0
        Get-Content -LiteralPath $extra | Should -Be 'user-owned content'
        Test-Path -LiteralPath (Join-Path $installation 'new-owned.txt') | Should -BeFalse
    }

    It 'relocates the same scope while preserving extra files in its previous directory' {
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        $extra = Join-Path $installation 'my-file.txt'
        Set-Content -LiteralPath $extra -Value 'keep after relocation'

        Invoke-InstallerProcess -FilePath $installerV2 -Arguments "/S /CurrentUser /D=$additionalInstallation" | Should -Be 0

        Test-Path -LiteralPath (Join-Path $installation 'Yohuke.DanmuNeo.exe') | Should -BeFalse
        Get-Content -LiteralPath $extra | Should -Be 'keep after relocation'
        (Get-FixtureRegistration -Mode CurrentUser).InstallLocation | Should -Be $additionalInstallation
        Test-Path -LiteralPath (Join-Path $additionalInstallation 'new-owned.txt') | Should -BeTrue
    }

    It 'rejects another scope using the same directory without removing the current installation' {
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0

        Invoke-InstallerProcess -FilePath $installerV2 -Arguments "/S /AllUsers /D=$installation" | Should -Be 1

        (Get-FixtureRegistration -Mode CurrentUser).DisplayVersion | Should -Be '1.2.3'
        Get-FixtureRegistration -Mode AllUsers | Should -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path $installation 'obsolete.txt') | Should -BeTrue
    }

    It 'keeps installations in different scopes independent' {
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        Invoke-InstallerProcess -FilePath $installerV2 -Arguments "/S /AllUsers /D=$additionalInstallation" | Should -Be 0
        (Get-FixtureRegistration -Mode CurrentUser).InstallLocation | Should -Be $installation
        (Get-FixtureRegistration -Mode AllUsers).InstallLocation | Should -Be $additionalInstallation

        Invoke-InstallerProcess -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S _?=$installation" | Should -Be 0

        (Get-FixtureRegistration -Mode AllUsers).InstallLocation | Should -Be $additionalInstallation
        Test-Path -LiteralPath (Join-Path $additionalInstallation 'Yohuke.DanmuNeo.exe') | Should -BeTrue
    }

    It 'rejects upgrade and uninstall while an owned file is locked' {
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        $executable = Join-Path $installation 'Yohuke.DanmuNeo.exe'
        $stream = [IO.File]::Open($executable, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)

        try
        {
            Invoke-InstallerProcess -FilePath $installerV2 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 1
            Invoke-InstallerProcess -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S _?=$installation" | Should -Be 1
            (Get-FixtureRegistration -Mode CurrentUser).DisplayVersion | Should -Be '1.2.3'
            Test-Path -LiteralPath (Join-Path $installation 'obsolete.txt') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $installation 'new-owned.txt') | Should -BeFalse
        }
        finally
        {
            $stream.Dispose()
        }
    }

    It 'preserves a desktop file when this installation did not create a shortcut' {
        Set-Content -LiteralPath $shortcut -Value 'unowned desktop file'
        Invoke-InstallerProcess -FilePath $installerV1 -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        Invoke-InstallerProcess -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S _?=$installation" | Should -Be 0
        Get-Content -LiteralPath $shortcut | Should -Be 'unowned desktop file'
    }
}
