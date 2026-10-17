#Requires -Version 7.2

function Invoke-WindowsInstallerCommand
{
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $ErrorActionPreference = "Stop"
    $PSNativeCommandUseErrorActionPreference = $false
    $output = [System.Collections.Generic.List[string]]::new()

    & $FilePath @Arguments 2>&1 | ForEach-Object {
        $line = [string]$_
        Write-Host $line
        $output.Add($line)
    }

    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0)
    {
        throw "$FilePath failed with exit code ${exitCode}: $($output -join [Environment]::NewLine)"
    }

    return $output -join [Environment]::NewLine
}

function Resolve-WindowsNsisCompiler
{
    param([string]$NsisPath)

    $ErrorActionPreference = "Stop"
    $compiler = $NsisPath

    if ([string]::IsNullOrWhiteSpace($compiler))
    {
        $command = Get-Command "makensis" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($command)
        {
            $compiler = $command.Path
        }
        else
        {
            foreach ($prefix in @(${env:ProgramFiles(x86)}, $env:ProgramFiles))
            {
                if ([string]::IsNullOrWhiteSpace($prefix))
                {
                    continue
                }

                $candidate = Join-Path $prefix "NSIS/makensis.exe"

                if (Test-Path -LiteralPath $candidate -PathType Leaf)
                {
                    $compiler = $candidate
                    break
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($compiler) -or -not (Test-Path -LiteralPath $compiler -PathType Leaf))
    {
        throw "Missing NSIS compiler. Install NSIS 3.11 or newer and add makensis to PATH, or supply -NsisPath with the makensis.exe path."
    }

    $compiler = (Resolve-Path -LiteralPath $compiler).ProviderPath
    $versionOutput = (Invoke-WindowsInstallerCommand -FilePath $compiler -Arguments @("/VERSION")).Trim()

    if ($versionOutput -notmatch '^v?([0-9]+\.[0-9]+(?:\.[0-9]+)?)$' -or [version]$Matches[1] -lt [version]"3.11")
    {
        throw "Requires NSIS 3.11 or newer; found '$versionOutput' at $compiler."
    }

    return [pscustomobject]@{
        Path = $compiler
        Version = $Matches[1]
    }
}

function ConvertTo-WindowsNsisLiteral
{
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [switch]$CompileTime
    )

    if ($Value -match '[\x00-\x1f]')
    {
        throw "NSIS strings cannot contain control characters."
    }

    if ($Value.Contains('${'))
    {
        throw "NSIS paths cannot contain preprocessor references."
    }

    if ($CompileTime)
    {
        return $Value.Replace('"', '$\"')
    }

    return $Value.Replace('$', '$$').Replace('"', '$\"')
}

function ConvertTo-WindowsInstallerFileVersion
{
    param([Parameter(Mandatory)][string]$ProductVersion)

    $versionText = $ProductVersion.Trim()

    if ($versionText -notmatch '^[0-9]+(?:\.[0-9]+){1,3}$')
    {
        throw "Windows installer version must use two to four numeric components: $ProductVersion"
    }

    $components = [System.Collections.Generic.List[int]]::new()

    foreach ($component in $versionText.Split('.'))
    {
        $number = 0

        if (-not [int]::TryParse($component, [ref]$number) -or $number -gt 65534)
        {
            throw "Windows installer version components must be between 0 and 65534: $ProductVersion"
        }

        $components.Add($number)
    }

    while ($components.Count -lt 4)
    {
        $components.Add(0)
    }

    return $components -join '.'
}

function New-WindowsInstaller
{
    param(
        [Parameter(Mandatory)][string]$PayloadDirectory,
        [Parameter(Mandatory)][string]$PublishDirectory,
        [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][string]$NsisCompiler,
        [Parameter(Mandatory)][string]$IconPath
    )

    $ErrorActionPreference = "Stop"
    $versionText = $ProductVersion.Trim()
    $fileVersion = ConvertTo-WindowsInstallerFileVersion -ProductVersion $versionText
    $scriptPath = Join-Path $PSScriptRoot "Yohuke.DanmuNeo.WindowsInstaller.nsi"

    foreach ($directory in @($PayloadDirectory, $PublishDirectory))
    {
        if (-not (Test-Path -LiteralPath $directory -PathType Container))
        {
            throw "Missing Windows installer directory: $directory"
        }
    }

    foreach ($path in @($scriptPath, $IconPath, $NsisCompiler))
    {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Missing Windows installer asset or compiler: $path"
        }
    }

    $payload = (Resolve-Path -LiteralPath $PayloadDirectory).ProviderPath
    $publish = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
    $icon = (Resolve-Path -LiteralPath $IconPath).ProviderPath
    $compiler = (Resolve-Path -LiteralPath $NsisCompiler).ProviderPath
    $payloadPrefix = $payload.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if ([string]::Equals($publish, $payload, [StringComparison]::OrdinalIgnoreCase) -or $publish.StartsWith($payloadPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Windows installer output must be outside the application payload directory."
    }

    $executable = Join-Path $payload "Yohuke.DanmuNeo.exe"

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf))
    {
        throw "Missing published Windows executable: Yohuke.DanmuNeo.exe"
    }

    $entries = @(Get-ChildItem -LiteralPath $payload -Recurse -Force)

    if (((Get-Item -LiteralPath $payload).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
        @($entries | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }).Count)
    {
        throw "Windows installer payload cannot contain symbolic links or reparse points."
    }

    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $entries)
    {
        $relative = [System.IO.Path]::GetRelativePath($payload, $entry.FullName).Replace('\', '/')

        if (-not $paths.Add($relative))
        {
            throw "Duplicate Windows installer path: $relative"
        }

        if ($relative -in @("Uninstall.exe", "install-state.ini"))
        {
            throw "Reserved installer path in payload: $relative"
        }
    }

    $files = @($entries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    $installer = Join-Path $publish "Yohuke-Danmu-Neo-$versionText-win-x64-setup.exe"
    $staging = Join-Path $publish ".danmu-neo-nsis-$([guid]::NewGuid().ToString('N'))"

    try
    {
        [System.IO.Directory]::CreateDirectory($staging) | Out-Null
        $include = Join-Path $staging "payload.nsh"
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add('!macro DanmuNeoInstallFiles')

        foreach ($directory in $entries | Where-Object PSIsContainer | Sort-Object FullName)
        {
            $relative = [System.IO.Path]::GetRelativePath($payload, $directory.FullName).Replace('/', '\')
            $lines.Add('CreateDirectory "$INSTDIR\' + (ConvertTo-WindowsNsisLiteral -Value $relative) + '"')
        }

        foreach ($file in $files)
        {
            $relativeDirectory = [System.IO.Path]::GetRelativePath($payload, $file.DirectoryName).Replace('/', '\')
            $target = if ($relativeDirectory -eq '.') { '$INSTDIR' } else { '$INSTDIR\' + (ConvertTo-WindowsNsisLiteral -Value $relativeDirectory) }
            $lines.Add("SetOutPath `"$target`"")
            $lines.Add('File "/oname=' + (ConvertTo-WindowsNsisLiteral -Value $file.Name) + '" "' + (ConvertTo-WindowsNsisLiteral -Value $file.FullName -CompileTime) + '"')
        }

        $lines.Add('!macroend')
        $lines.Add('!macro DanmuNeoCheckInstalledFiles')

        foreach ($file in $files)
        {
            $relative = [System.IO.Path]::GetRelativePath($payload, $file.FullName).Replace('/', '\')
            $lines.Add('!insertmacro DanmuNeoCheckFile "$INSTDIR\' + (ConvertTo-WindowsNsisLiteral -Value $relative) + '"')
        }

        $lines.Add('!macroend')
        $lines.Add('!macro DanmuNeoUninstallFiles')

        foreach ($file in $files)
        {
            $relative = [System.IO.Path]::GetRelativePath($payload, $file.FullName).Replace('/', '\')
            $target = '$INSTDIR\' + (ConvertTo-WindowsNsisLiteral -Value $relative)
            $lines.Add("Delete `"$target`"")
            $lines.Add('!insertmacro DanmuNeoCheckDelete')
        }

        foreach ($directory in $entries | Where-Object PSIsContainer | Sort-Object { $_.FullName.Length } -Descending)
        {
            $relative = [System.IO.Path]::GetRelativePath($payload, $directory.FullName).Replace('/', '\')
            $lines.Add('RMDir "$INSTDIR\' + (ConvertTo-WindowsNsisLiteral -Value $relative) + '"')
        }

        $lines.Add('!macroend')
        [System.IO.File]::WriteAllLines($include, $lines, [System.Text.UTF8Encoding]::new($false))
        $compiled = Join-Path $staging ([System.IO.Path]::GetFileName($installer))
        $definitions = [ordered]@{
            DANMUNEO_PRODUCT_VERSION = $versionText
            DANMUNEO_FILE_VERSION = $fileVersion
            DANMUNEO_OUTPUT_FILE = $compiled
            DANMUNEO_ICON_FILE = $icon
            DANMUNEO_PAYLOAD_INCLUDE = $include
            DANMUNEO_ESTIMATED_SIZE = [math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1024)
        }

        $configuration = Join-Path $staging "configuration.nsh"
        $content = foreach ($entry in $definitions.GetEnumerator())
        {
            '!define ' + $entry.Key + ' "' + (ConvertTo-WindowsNsisLiteral -Value ([string]$entry.Value) -CompileTime) + '"'
        }

        [System.IO.File]::WriteAllLines($configuration, [string[]]$content, [System.Text.UTF8Encoding]::new($false))
        $arguments = @("/NOCD", "/WX", "/V3", "/INPUTCHARSET", "UTF8", "/DDANMUNEO_CONFIG_FILE=$configuration", $scriptPath)
        $null = Invoke-WindowsInstallerCommand -FilePath $compiler -Arguments $arguments

        if (-not (Test-Path -LiteralPath $compiled -PathType Leaf) -or (Get-Item -LiteralPath $compiled).Length -eq 0)
        {
            throw "NSIS did not create a nonempty Windows installer."
        }

        [System.IO.File]::Move($compiled, $installer, $true)
        return $installer
    }
    finally
    {
        if (Test-Path -LiteralPath $staging)
        {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}
