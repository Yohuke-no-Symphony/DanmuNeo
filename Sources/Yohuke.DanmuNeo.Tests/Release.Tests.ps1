#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $releasePath = Join-Path $repository 'release.ps1'
    . (Join-Path $repository 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.ps1')
    $parseErrors = $null
    $tokens = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($releasePath, [ref]$tokens, [ref]$parseErrors)

    if ($parseErrors.Count -gt 0)
    {
        throw ($parseErrors | Out-String)
    }

    foreach ($statement in $ast.EndBlock.Statements)
    {
        if ($statement -is [Management.Automation.Language.FunctionDefinitionAst])
        {
            . ([scriptblock]::Create($statement.Extent.Text))
        }
    }
}

Describe 'Release platform boundaries' {
    BeforeEach {
        $repoRoot = Join-Path $TestDrive "repo with spaces-$([Guid]::NewGuid().ToString('N'))"
        $solutionPath = Join-Path $repoRoot 'Sources/Yohuke.DanmuNeo.sln'
        $projectPath = Join-Path $repoRoot 'Sources/Yohuke.DanmuNeo/Yohuke.DanmuNeo.csproj'
        $iconPath = Join-Path $repoRoot 'Sources/Yohuke.DanmuNeo/Assets/favicon.ico'
        $macReadmePath = Join-Path $repoRoot 'Packaging/macOS/README.txt'
        $windowsInstallerScriptPath = Join-Path $repoRoot 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.ps1'
        $windowsInstallerTemplatePath = Join-Path $repoRoot 'Packaging/Windows/Yohuke.DanmuNeo.WindowsInstaller.nsi'
        $publishRoot = Join-Path $repoRoot 'Artifacts/Publish'
        $appName = 'Yohuke Danmu Neo'
        $executableName = 'Yohuke.DanmuNeo'
        $Configuration = 'Release'
        $MacCodeSignIdentity = '-'
        $NsisPath = $null
        $Version = $null
        $SkipRestore = $false
        $Clean = $false

        foreach ($path in @($solutionPath, $projectPath, $iconPath, $windowsInstallerScriptPath, $windowsInstallerTemplatePath))
        {
            New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
            Set-Content -LiteralPath $path -Value ''
        }

        Set-Content -LiteralPath $projectPath -Value '<Project><PropertyGroup><AssemblyVersion>1.2.3</AssemblyVersion></PropertyGroup></Project>'
        New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
        $existingArtifact = Join-Path $publishRoot 'existing.txt'
        Set-Content -LiteralPath $existingArtifact -Value 'existing artifact'

        Mock Assert-CommandExists {}
        Mock Invoke-DotNet {}
        Mock Publish-Runtime {}
        Mock Assert-LanguagePacks {}
        Mock Resolve-WindowsNsisCompiler { [pscustomobject]@{ Path = 'fixture-makensis'; Version = [version]'3.13' } }
        Mock New-WindowsInstaller { Join-Path $publishRoot 'fixture-setup.exe' }
        Mock New-MacAppBundle {}
        Mock Sign-MacAppBundle {}
        Mock New-MacDmg {}
        Mock Write-Host {}
        Mock Assert-PathExists {} -ParameterFilter { $Description -eq 'Windows executable' }
    }

    It 'publishes Windows without requiring macOS resources or tools' {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }

        Invoke-Release

        Should -Invoke Publish-Runtime -Times 1 -Exactly -ParameterFilter {
            $RuntimeIdentifier -eq 'win-x64' -and
            $OutputPath -eq (Join-Path $publishRoot 'win-x64') -and
            $PublishVersion -eq '1.2.3'
        }
        Should -Invoke Resolve-WindowsNsisCompiler -Times 1 -Exactly
        Should -Invoke Assert-LanguagePacks -Times 1 -Exactly -ParameterFilter {
            $Directory -eq (Join-Path $publishRoot 'win-x64/i18n')
        }
        Should -Invoke New-WindowsInstaller -Times 1 -Exactly -ParameterFilter {
            $PayloadDirectory -eq (Join-Path $publishRoot 'win-x64') -and
            $PublishDirectory -eq $publishRoot -and
            $ProductVersion -eq '1.2.3' -and $NsisCompiler -eq 'fixture-makensis' -and $IconPath -eq $iconPath
        }
        Should -Invoke Assert-CommandExists -Times 0 -Exactly -ParameterFilter { $Name -ne 'dotnet' }
        Should -Invoke New-MacAppBundle -Times 0 -Exactly
        Should -Invoke Sign-MacAppBundle -Times 0 -Exactly
        Should -Invoke New-MacDmg -Times 0 -Exactly
        Test-Path -LiteralPath $macReadmePath | Should -BeFalse
    }

    It 'publishes only macOS and forwards the version and signing identity' {
        Mock Get-PublishRuntimeIdentifier { 'osx-arm64' }
        New-Item -ItemType Directory -Path (Split-Path $macReadmePath -Parent) -Force | Out-Null
        Set-Content -LiteralPath $macReadmePath -Value 'first launch guide'
        $Version = '2.3.4.5'
        $MacCodeSignIdentity = 'Developer ID Application: Fixture'
        Remove-Item -LiteralPath (Split-Path $windowsInstallerScriptPath -Parent) -Recurse

        Invoke-Release

        Should -Invoke Publish-Runtime -Times 1 -Exactly -ParameterFilter {
            $RuntimeIdentifier -eq 'osx-arm64' -and
            $OutputPath -eq (Join-Path $publishRoot 'osx-arm64/publish') -and
            $PublishVersion -eq '2.3.4.5'
        }
        Should -Invoke New-MacAppBundle -Times 1 -Exactly -ParameterFilter {
            $Version -eq '2.3.4.5' -and
            $BundlePath -eq (Join-Path $publishRoot 'osx-arm64/Yohuke Danmu Neo.app')
        }
        Should -Invoke Sign-MacAppBundle -Times 1 -Exactly -ParameterFilter {
            $Identity -eq 'Developer ID Application: Fixture'
        }
        Should -Invoke New-MacDmg -Times 1 -Exactly -ParameterFilter {
            $ReadmePath -eq $macReadmePath -and
            $OutputPath -eq (Join-Path $publishRoot 'Yohuke-Danmu-Neo-2.3.4.5-osx-arm64.dmg')
        }
        Should -Invoke New-WindowsInstaller -Times 0 -Exactly
        Should -Invoke Resolve-WindowsNsisCompiler -Times 0 -Exactly
    }

    It 'checks missing <Missing> before cleaning or restoring on Windows' -ForEach @(
        @{ Missing = 'builder' }
        @{ Missing = 'template' }
        @{ Missing = 'compiler' }
    ) {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $Clean = $true

        if ($Missing -eq 'builder')
        {
            Remove-Item -LiteralPath $windowsInstallerScriptPath
        }
        elseif ($Missing -eq 'template')
        {
            Remove-Item -LiteralPath $windowsInstallerTemplatePath
        }
        else
        {
            Mock Resolve-WindowsNsisCompiler { throw 'NSIS compiler unavailable.' }
        }

        { Invoke-Release } | Should -Throw

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
        Should -Invoke Publish-Runtime -Times 0 -Exactly
    }

    It 'forwards the explicit NSIS compiler path on Windows' {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $NsisPath = 'C:\NSIS with spaces\makensis.exe'

        Invoke-Release

        Should -Invoke Resolve-WindowsNsisCompiler -Times 1 -Exactly -ParameterFilter {
            $NsisPath -eq 'C:\NSIS with spaces\makensis.exe'
        }
    }

    It 'rejects a Windows-only compiler option on macOS before cleaning output' {
        Mock Get-PublishRuntimeIdentifier { 'osx-arm64' }
        $NsisPath = 'makensis'
        $Clean = $true

        { Invoke-Release } | Should -Throw '*NsisPath is only supported*'

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
    }

    It 'checks missing <Missing> before cleaning or restoring' -ForEach @(
        @{ Missing = 'guide' }
        @{ Missing = 'sips' }
        @{ Missing = 'iconutil' }
        @{ Missing = 'codesign' }
        @{ Missing = 'xattr' }
        @{ Missing = 'chmod' }
        @{ Missing = 'ditto' }
        @{ Missing = 'hdiutil' }
        @{ Missing = 'ln' }
    ) {
        Mock Get-PublishRuntimeIdentifier { 'osx-arm64' }
        $Clean = $true

        if ($Missing -ne 'guide')
        {
            New-Item -ItemType Directory -Path (Split-Path $macReadmePath -Parent) -Force | Out-Null
            Set-Content -LiteralPath $macReadmePath -Value 'first launch guide'
            Mock Assert-CommandExists { throw "Missing tool: $Name" } -ParameterFilter { $Name -eq $Missing }
        }

        { Invoke-Release } | Should -Throw

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
        Should -Invoke Publish-Runtime -Times 0 -Exactly
    }

    It 'rejects unsupported hosts before changing output' {
        Mock Get-PublishRuntimeIdentifier { throw 'Unsupported release host.' }
        $Clean = $true

        { Invoke-Release } | Should -Throw '*Unsupported release host*'

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
        Should -Invoke Publish-Runtime -Times 0 -Exactly
    }

    It 'rejects an invalid version before cleaning output' {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $Version = 'invalid'
        $Clean = $true

        { Invoke-Release } | Should -Throw '*Version must use*'

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
        Should -Invoke Publish-Runtime -Times 0 -Exactly
    }

    It 'honors SkipRestore while still publishing the selected platform' {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $SkipRestore = $true

        Invoke-Release

        Should -Invoke Invoke-DotNet -Times 0 -Exactly
        Should -Invoke Publish-Runtime -Times 1 -Exactly -ParameterFilter { $RuntimeIdentifier -eq 'win-x64' }
    }

    It 'rejects version component <Value> before cleaning output' -ForEach @(
        @{ Value = '1.2.65535' }
        @{ Value = '9999999999999999.0' }
    ) {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $Version = $Value
        $Clean = $true

        { Invoke-Release } | Should -Throw '*between 0 and 65534*'

        Get-Content -LiteralPath $existingArtifact | Should -Be 'existing artifact'
        Should -Invoke Invoke-DotNet -Times 0 -Exactly
    }

    It 'cleans existing output only after prerequisites have passed' {
        Mock Get-PublishRuntimeIdentifier { 'win-x64' }
        $Clean = $true

        Invoke-Release

        Test-Path -LiteralPath $existingArtifact | Should -BeFalse
        Test-Path -LiteralPath $publishRoot -PathType Container | Should -BeTrue
        Should -Invoke Invoke-DotNet -Times 1 -Exactly -ParameterFilter {
            $Arguments.Count -eq 2 -and $Arguments[0] -eq 'restore' -and $Arguments[1] -eq $solutionPath
        }
        Should -Invoke Publish-Runtime -Times 1 -Exactly -ParameterFilter { $RuntimeIdentifier -eq 'win-x64' }
    }
}

Describe 'Release host detection' {
    It 'resolves the runtime identifier for the actual host' {
        if ($IsWindows)
        {
            Get-PublishRuntimeIdentifier | Should -Be 'win-x64'
        }
        elseif ($IsMacOS)
        {
            Get-PublishRuntimeIdentifier | Should -Be 'osx-arm64'
        }
        else
        {
            { Get-PublishRuntimeIdentifier } | Should -Throw '*Unsupported release host*'
        }
    }
}
