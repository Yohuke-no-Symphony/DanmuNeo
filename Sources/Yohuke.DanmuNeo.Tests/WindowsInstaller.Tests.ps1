#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    . (Join-Path $repository 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.ps1')
}

Describe 'Windows NSIS compiler discovery' {
    BeforeEach {
        $originalProgramFiles = $env:ProgramFiles
        $originalProgramFilesX86 = ${env:ProgramFiles(x86)}
        $compilerFixtureRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        $env:ProgramFiles = Join-Path $compilerFixtureRoot 'Program Files'
        ${env:ProgramFiles(x86)} = Join-Path $compilerFixtureRoot 'Program Files (x86)'
        $compilerPath = Join-Path $TestDrive "compiler with spaces-$([Guid]::NewGuid().ToString('N'))/makensis.exe"
        [IO.Directory]::CreateDirectory((Split-Path $compilerPath -Parent)) | Out-Null
        [IO.File]::WriteAllText($compilerPath, 'compiler fixture')
        Mock Invoke-WindowsInstallerCommand { 'v3.13' }
        Mock Get-Command { $null } -ParameterFilter { $Name -in @('makensis', 'makensis.exe') }
    }

    AfterEach {
        $env:ProgramFiles = $originalProgramFiles
        ${env:ProgramFiles(x86)} = $originalProgramFilesX86
    }

    It 'validates an explicit compiler path without searching PATH' {
        $result = Resolve-WindowsNsisCompiler -NsisPath $compilerPath

        $result.Path | Should -Be $compilerPath
        $result.Version | Should -Be '3.13'
        Should -Invoke Get-Command -Times 0 -Exactly -ParameterFilter { $Name -in @('makensis', 'makensis.exe') }
        Should -Invoke Invoke-WindowsInstallerCommand -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq $compilerPath -and $Arguments.Count -eq 1 -and $Arguments[0] -eq '/VERSION'
        }
    }

    It 'rejects a missing explicit compiler without falling back to PATH' {
        $missing = Join-Path $TestDrive 'missing compiler.exe'

        { Resolve-WindowsNsisCompiler -NsisPath $missing } | Should -Throw '*Missing NSIS compiler*'

        Should -Invoke Get-Command -Times 0 -Exactly -ParameterFilter { $Name -in @('makensis', 'makensis.exe') }
        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'resolves a compiler from PATH before standard installation directories' {
        Mock Get-Command { [pscustomobject]@{ Source = $compilerPath; Path = $compilerPath } } -ParameterFilter {
            $Name -in @('makensis', 'makensis.exe')
        }
        $standardCompiler = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe'
        [IO.Directory]::CreateDirectory((Split-Path $standardCompiler -Parent)) | Out-Null
        [IO.File]::WriteAllText($standardCompiler, 'another compiler fixture')

        $result = Resolve-WindowsNsisCompiler

        $result.Path | Should -Be $compilerPath
        Should -Invoke Invoke-WindowsInstallerCommand -Times 1 -Exactly -ParameterFilter { $FilePath -eq $compilerPath }
    }

    It 'finds NSIS under the standard <Location> installation directory' -ForEach @(
        @{ Location = 'ProgramFilesX86' }
        @{ Location = 'ProgramFiles' }
    ) {
        $prefix = if ($Location -eq 'ProgramFilesX86') { ${env:ProgramFiles(x86)} } else { $env:ProgramFiles }
        $expectedCompiler = Join-Path $prefix 'NSIS/makensis.exe'
        [IO.Directory]::CreateDirectory((Split-Path $expectedCompiler -Parent)) | Out-Null
        [IO.File]::WriteAllText($expectedCompiler, 'standard compiler fixture')

        $result = Resolve-WindowsNsisCompiler

        $result.Path | Should -Be $expectedCompiler
        Should -Invoke Invoke-WindowsInstallerCommand -Times 1 -Exactly -ParameterFilter { $FilePath -eq $expectedCompiler }
    }

    It 'reports a missing compiler when PATH and standard directories are empty' {
        { Resolve-WindowsNsisCompiler } | Should -Throw '*Missing NSIS compiler*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'accepts the minimum supported NSIS version' {
        Mock Invoke-WindowsInstallerCommand { "v3.11`n" }

        $result = Resolve-WindowsNsisCompiler -NsisPath $compilerPath

        $result.Version | Should -Be '3.11'
    }

    It 'rejects unsupported or unrecognizable version <CompilerVersion>' -ForEach @(
        @{ CompilerVersion = 'v2.51' }
        @{ CompilerVersion = 'v3.10' }
        @{ CompilerVersion = 'unrecognized output' }
    ) {
        Mock Invoke-WindowsInstallerCommand { $CompilerVersion }

        { Resolve-WindowsNsisCompiler -NsisPath $compilerPath } | Should -Throw '*NSIS 3.11 or newer*'
    }

    It 'propagates a failed compiler version query' {
        Mock Invoke-WindowsInstallerCommand { throw 'Fixture version query failed.' }

        { Resolve-WindowsNsisCompiler -NsisPath $compilerPath } | Should -Throw '*Fixture version query failed*'
    }
}

Describe 'Windows installer native command execution' {
    BeforeEach {
        $pwshFileName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
        $pwshExecutable = Join-Path $PSHOME $pwshFileName
        Mock Write-Host {}
    }

    It 'captures native stdout and stderr as one result' {
        $command = {
            [Console]::WriteLine('fixture stdout')
            [Console]::Error.WriteLine('fixture stderr')
        }.ToString()

        $result = @(Invoke-WindowsInstallerCommand -FilePath $pwshExecutable -Arguments @('-NoProfile', '-NonInteractive', '-Command', $command))

        $result.Count | Should -Be 1
        $result[0] | Should -Match 'fixture stdout'
        $result[0] | Should -Match 'fixture stderr'
    }

    It 'throws with the actual native failure exit code and diagnostic output' {
        $command = {
            [Console]::Error.WriteLine('fixture compiler error')
            exit 9
        }.ToString()

        { Invoke-WindowsInstallerCommand -FilePath $pwshExecutable -Arguments @('-NoProfile', '-NonInteractive', '-Command', $command) } |
            Should -Throw '*exit code 9*fixture compiler error*'
    }
}

Describe 'Windows installer compilation contract' {
    BeforeEach {
        $publishDirectory = Join-Path $TestDrive ('publish with spaces and dollar$-' + [Guid]::NewGuid().ToString('N'))
        $payloadDirectory = Join-Path $publishDirectory 'win-x64'
        $nestedDirectory = Join-Path $payloadDirectory 'tools $cache/nested'
        [IO.Directory]::CreateDirectory($nestedDirectory) | Out-Null
        foreach ($name in @('Yohuke.DanmuNeo.exe', 'tools $cache/nested/dollar$sign.txt', 'tools $cache/nested/quote''s.txt'))
        {
            [IO.File]::WriteAllText((Join-Path $payloadDirectory $name), 'published fixture')
        }
        $iconPath = Join-Path $TestDrive 'icon with spaces $app.ico'
        [IO.File]::WriteAllText($iconPath, 'icon fixture')
        $nsisCompilerPath = Join-Path $TestDrive 'NSIS fixtures/makensis.exe'
        [IO.Directory]::CreateDirectory((Split-Path $nsisCompilerPath -Parent)) | Out-Null
        [IO.File]::WriteAllText($nsisCompilerPath, 'compiler fixture')
        [IO.File]::WriteAllText((Join-Path $publishDirectory 'package-manifest.json'), 'must not be installed')
        $installerArguments = @{
            PayloadDirectory = $payloadDirectory
            PublishDirectory = $publishDirectory
            ProductVersion = '1.2.3'
            NsisCompiler = $nsisCompilerPath
            IconPath = $iconPath
        }
        Mock Invoke-WindowsInstallerCommand { throw 'Compiler must not be invoked.' }
    }

    It 'rejects invalid product version <ProductVersion> before invoking the compiler' -ForEach @(
        @{ ProductVersion = '1' }
        @{ ProductVersion = '1.2.3.4.5' }
        @{ ProductVersion = '1.2-beta' }
        @{ ProductVersion = '65535.1' }
        @{ ProductVersion = '1.65535.0' }
        @{ ProductVersion = '1.2.65535.0' }
        @{ ProductVersion = '1.2.3.65535' }
    ) {
        $installerArguments.ProductVersion = $ProductVersion

        { New-WindowsInstaller @installerArguments } | Should -Throw

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
        @(Get-ChildItem -LiteralPath $publishDirectory -Directory -Force).Count | Should -Be 1
    }

    It 'requires the published application executable before invoking the compiler' {
        Remove-Item -LiteralPath (Join-Path $payloadDirectory 'Yohuke.DanmuNeo.exe')

        { New-WindowsInstaller @installerArguments } | Should -Throw '*Yohuke.DanmuNeo.exe*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'requires the supplied Windows icon before invoking the compiler' {
        Remove-Item -LiteralPath $iconPath

        { New-WindowsInstaller @installerArguments } | Should -Throw '*icon*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'rejects installer output inside the application payload' {
        $installerArguments.PublishDirectory = $payloadDirectory

        { New-WindowsInstaller @installerArguments } | Should -Throw '*outside the application payload*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'escapes runtime NSIS strings and rejects control characters' {
        ConvertTo-WindowsNsisLiteral -Value 'dollar$sign"quote' | Should -Be 'dollar$$sign$\"quote'
        ConvertTo-WindowsNsisLiteral -Value 'dollar$sign.txt' -CompileTime | Should -Be 'dollar$sign.txt'
        { ConvertTo-WindowsNsisLiteral -Value "file`nSection injected" } | Should -Throw '*control characters*'
    }

    It 'rejects payload paths reserved for installer metadata: <ReservedName>' -ForEach @(
        @{ ReservedName = 'Uninstall.exe' }
        @{ ReservedName = 'install-state.ini' }
        @{ ReservedName = 'INSTALL-STATE.INI' }
    ) {
        [IO.File]::WriteAllText((Join-Path $payloadDirectory $ReservedName), 'reserved fixture')

        { New-WindowsInstaller @installerArguments } | Should -Throw '*Reserved installer path*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
    }

    It 'rejects NSIS preprocessor references in payload paths before invoking the compiler' {
        [IO.File]::WriteAllText((Join-Path $payloadDirectory '${DANMUNEO_INJECTED}.txt'), 'invalid fixture')

        { New-WindowsInstaller @installerArguments } | Should -Throw '*preprocessor references*'

        Should -Invoke Invoke-WindowsInstallerCommand -Times 0 -Exactly
        @(Get-ChildItem -LiteralPath $publishDirectory -Directory -Force).Count | Should -Be 1
    }

    It 'compiles explicit ownership lists and cleans staging after <Outcome>' -ForEach @(
        @{ Outcome = 'Success'; ProductVersion = '1.2.3'; FileVersion = '1.2.3.0' }
        @{ Outcome = 'Success'; ProductVersion = '1.2'; FileVersion = '1.2.0.0' }
        @{ Outcome = 'Success'; ProductVersion = '1.2.3.4'; FileVersion = '1.2.3.4' }
        @{ Outcome = 'CompilerFailure'; ProductVersion = '1.2.3'; FileVersion = '1.2.3.0' }
        @{ Outcome = 'MissingOutput'; ProductVersion = '1.2.3'; FileVersion = '1.2.3.0' }
        @{ Outcome = 'EmptyOutput'; ProductVersion = '1.2.3'; FileVersion = '1.2.3.0' }
    ) {
        $installerArguments.ProductVersion = $ProductVersion
        $installer = Join-Path $publishDirectory "Yohuke-Danmu-Neo-$ProductVersion-win-x64-setup.exe"
        [IO.File]::WriteAllText($installer, 'previous installer fixture')
        $script:installerStagingFixture = $null
        $script:installerIncludesFixture = $null
        $script:installerConfigurationFixture = $null
        Mock Invoke-WindowsInstallerCommand {
            param($FilePath, $Arguments)
            $FilePath | Should -Be $nsisCompilerPath
            $Arguments | Should -Contain '/NOCD'
            $Arguments | Should -Contain '/WX'
            $Arguments | Should -Contain '/INPUTCHARSET'
            $Arguments | Should -Contain 'UTF8'
            $Arguments[-1] | Should -Be (Join-Path $repository 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.nsi')
            $configurationArgument = @($Arguments | Where-Object { $_.StartsWith('/DDANMUNEO_CONFIG_FILE=') })
            $configurationArgument.Count | Should -Be 1
            $configurationPath = $configurationArgument[0].Substring('/DDANMUNEO_CONFIG_FILE='.Length)
            $script:installerStagingFixture = Split-Path $configurationPath -Parent
            $script:installerConfigurationFixture = Get-Content -LiteralPath $configurationPath -Raw
            $script:installerIncludesFixture = Get-Content -LiteralPath (Join-Path $script:installerStagingFixture 'payload.nsh') -Raw
            $script:installerConfigurationFixture | Should -Match ('(?m)^!define DANMUNEO_PRODUCT_VERSION "' + [regex]::Escape($ProductVersion) + '"\r?$')
            $script:installerConfigurationFixture | Should -Match ('(?m)^!define DANMUNEO_FILE_VERSION "' + [regex]::Escape($FileVersion) + '"\r?$')
            $outputDefinition = [regex]::Match($script:installerConfigurationFixture, '(?m)^!define DANMUNEO_OUTPUT_FILE "([^"]+)"\r?$')
            $outputDefinition.Success | Should -BeTrue
            $compiledPath = $outputDefinition.Groups[1].Value
            $compiledPath | Should -Not -Be $installer
            (Split-Path $compiledPath -Parent) | Should -Be $script:installerStagingFixture

            if ($Outcome -eq 'CompilerFailure')
            {
                [IO.File]::WriteAllText($compiledPath, 'partial installer fixture')
                throw 'Fixture compiler failure.'
            }

            if ($Outcome -eq 'EmptyOutput')
            {
                [IO.File]::WriteAllBytes($compiledPath, [byte[]]@())
            }
            elseif ($Outcome -ne 'MissingOutput')
            {
                [IO.File]::WriteAllText($compiledPath, 'new installer fixture')
            }

            return 'compiled fixture'
        }

        if ($Outcome -eq 'Success')
        {
            $result = @(New-WindowsInstaller @installerArguments)
            $result.Count | Should -Be 1
            $result[0] | Should -Be $installer
            [IO.File]::ReadAllText($installer) | Should -Be 'new installer fixture'
        }
        else
        {
            { New-WindowsInstaller @installerArguments } | Should -Throw
            [IO.File]::ReadAllText($installer) | Should -Be 'previous installer fixture'
        }

        Should -Invoke Invoke-WindowsInstallerCommand -Times 1 -Exactly
        $script:installerStagingFixture | Should -Not -BeNullOrEmpty
        Test-Path -LiteralPath $script:installerStagingFixture | Should -BeFalse
        [IO.Path]::GetRelativePath($publishDirectory, $script:installerStagingFixture) | Should -Not -Match '^\.\.'
        @(Get-ChildItem -LiteralPath $publishDirectory -Directory -Force).Count | Should -Be 1
        $script:installerIncludesFixture | Should -Match ([regex]::Escape('Delete "$INSTDIR\tools $$cache\nested\dollar$$sign.txt"'))
        $script:installerIncludesFixture | Should -Match ([regex]::Escape('DanmuNeoCheckFile "$INSTDIR\Yohuke.DanmuNeo.exe"'))
        $script:installerIncludesFixture | Should -Match ([regex]::Escape('DanmuNeoCheckFile "$INSTDIR\tools $$cache\nested\dollar$$sign.txt"'))
        $script:installerIncludesFixture | Should -Match ([regex]::Escape('File "/oname=dollar$$sign.txt" "' + (Join-Path $nestedDirectory 'dollar$sign.txt') + '"'))
        $script:installerIncludesFixture | Should -Not -Match 'package-manifest\.json|RMDir\s+/r|Delete\s+/REBOOTOK|File\s+/r'
        $nestedRemoval = $script:installerIncludesFixture.IndexOf('RMDir "$INSTDIR\tools $$cache\nested"')
        $parentRemoval = $script:installerIncludesFixture.IndexOf('RMDir "$INSTDIR\tools $$cache"')
        $nestedRemoval | Should -BeGreaterOrEqual 0
        $parentRemoval | Should -BeGreaterThan $nestedRemoval
    }
}
