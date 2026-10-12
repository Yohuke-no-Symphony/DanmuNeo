param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "Artifacts/Publish",
    [string]$Version,
    [string]$MacCodeSignIdentity = "-",
    [switch]$SkipRestore,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -ge 7)
{
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = $PSScriptRoot
$solutionPath = Join-Path $repoRoot "Sources/Yohuke.DanmuNeo.sln"
$projectPath = Join-Path $repoRoot "Sources/Yohuke.DanmuNeo/Yohuke.DanmuNeo.csproj"
$iconPath = Join-Path $repoRoot "Sources/Yohuke.DanmuNeo/Assets/favicon.ico"
$macReadmePath = Join-Path $repoRoot "Packaging/macOS/README.txt"
$appName = "Yohuke Danmu Neo"
$appDisplayName = "Danmu Neo"
$executableName = "Yohuke.DanmuNeo"
$bundleIdentifier = "com.yohuke.danmuneo"

if ([System.IO.Path]::IsPathRooted($OutputDirectory))
{
    $publishRoot = $OutputDirectory
}
else
{
    $publishRoot = Join-Path $repoRoot $OutputDirectory
}

function Assert-PathExists
{
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path $Path))
    {
        throw "$Description not found: $Path"
    }
}

function Assert-CommandExists
{
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue))
    {
        throw "Required command '$Name' was not found."
    }
}

function Get-ProjectVersion
{
    [xml]$project = Get-Content $projectPath
    $version = $null

    foreach ($propertyGroup in $project.Project.PropertyGroup)
    {
        if (-not [string]::IsNullOrWhiteSpace($propertyGroup.AssemblyVersion))
        {
            $version = $propertyGroup.AssemblyVersion
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($version))
    {
        return "0.0.1"
    }

    return $version
}

function Resolve-PublishVersion
{
    if (-not [string]::IsNullOrWhiteSpace($Version))
    {
        $publishVersion = $Version.Trim()
    }
    else
    {
        $publishVersion = Get-ProjectVersion
    }

    if ($publishVersion -notmatch '^\d+(\.\d+){1,3}$')
    {
        throw "Version must use two to four numeric components, for example 0.0.3 or 1.2.3.4."
    }

    return $publishVersion
}

function Invoke-DotNet
{
    param([string[]]$Arguments)

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function Publish-Runtime
{
    param(
        [string]$RuntimeIdentifier,
        [string]$OutputPath,
        [string]$PublishVersion
    )

    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

    $arguments = @(
        "publish",
        $projectPath,
        "--configuration", $Configuration,
        "--runtime", $RuntimeIdentifier,
        "--self-contained", "true",
        "--output", $OutputPath,
        "/p:PublishSingleFile=true",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:EnableCompressionInSingleFile=true",
        "/p:Version=$PublishVersion",
        "/p:AssemblyVersion=$PublishVersion",
        "/p:FileVersion=$PublishVersion",
        "/p:DebugType=None",
        "/p:DebugSymbols=false"
    )

    if ($SkipRestore)
    {
        $arguments += "--no-restore"
    }

    Write-Host "Publishing $RuntimeIdentifier..."
    Invoke-DotNet $arguments
}

function Convert-IconToIcns
{
    param(
        [string]$SourceIconPath,
        [string]$OutputIconPath
    )

    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX))
    {
        throw "Creating .icns requires macOS because the script uses sips and iconutil."
    }

    Assert-CommandExists "sips"
    Assert-CommandExists "iconutil"

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "danmu-neo-icon-$([System.Guid]::NewGuid().ToString('N'))"
    $iconsetPath = Join-Path $tempRoot "AppIcon.iconset"
    $sourcePng = Join-Path $tempRoot "favicon.png"

    New-Item -ItemType Directory -Force -Path $iconsetPath | Out-Null

    try
    {
        & sips -s format png $SourceIconPath --out $sourcePng | Out-Null

        $iconSizes = @(
            @{ Name = "icon_16x16.png"; Size = 16 },
            @{ Name = "icon_16x16@2x.png"; Size = 32 },
            @{ Name = "icon_32x32.png"; Size = 32 },
            @{ Name = "icon_32x32@2x.png"; Size = 64 },
            @{ Name = "icon_128x128.png"; Size = 128 },
            @{ Name = "icon_128x128@2x.png"; Size = 256 },
            @{ Name = "icon_256x256.png"; Size = 256 },
            @{ Name = "icon_256x256@2x.png"; Size = 512 },
            @{ Name = "icon_512x512.png"; Size = 512 },
            @{ Name = "icon_512x512@2x.png"; Size = 1024 }
        )

        foreach ($iconSize in $iconSizes)
        {
            $targetPng = Join-Path $iconsetPath $iconSize.Name
            & sips -z $iconSize.Size $iconSize.Size $sourcePng --out $targetPng | Out-Null
        }

        New-Item -ItemType Directory -Force -Path (Split-Path $OutputIconPath -Parent) | Out-Null
        & iconutil -c icns $iconsetPath -o $OutputIconPath

        if ($LASTEXITCODE -ne 0)
        {
            throw "iconutil failed with exit code $LASTEXITCODE."
        }
    }
    finally
    {
        Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
    }
}

function New-MacAppBundle
{
    param(
        [string]$PublishPath,
        [string]$BundlePath,
        [string]$Version
    )

    $contentsPath = Join-Path $BundlePath "Contents"
    $macosPath = Join-Path $contentsPath "MacOS"
    $resourcesPath = Join-Path $contentsPath "Resources"
    $plistPath = Join-Path $contentsPath "Info.plist"
    $icnsPath = Join-Path $resourcesPath "AppIcon.icns"

    if (Test-Path $BundlePath)
    {
        Remove-Item -Recurse -Force $BundlePath
    }

    New-Item -ItemType Directory -Force -Path $macosPath | Out-Null
    New-Item -ItemType Directory -Force -Path $resourcesPath | Out-Null

    Copy-Item -Path (Join-Path $PublishPath "*") -Destination $macosPath -Recurse -Force
    Convert-IconToIcns -SourceIconPath $iconPath -OutputIconPath $icnsPath

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>$executableName</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>$bundleIdentifier</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>$appDisplayName</string>
    <key>CFBundleDisplayName</key>
    <string>$appDisplayName</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$Version</string>
    <key>CFBundleVersion</key>
    <string>$Version</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@

    Set-Content -Path $plistPath -Value $plist -Encoding UTF8

    $executablePath = Join-Path $macosPath $executableName
    if (Test-Path $executablePath)
    {
        & chmod +x $executablePath
    }
    else
    {
        throw "macOS executable was not found: $executablePath"
    }
}

function Sign-MacAppBundle
{
    param(
        [string]$BundlePath,
        [string]$Identity
    )

    Assert-CommandExists "codesign"
    Assert-CommandExists "xattr"

    & xattr -cr $BundlePath

    if ($Identity -eq "-")
    {
        Write-Host "Signing macOS app with an ad-hoc signature..."
        & codesign --force --deep --sign $Identity $BundlePath
    }
    else
    {
        Write-Host "Signing macOS app with identity '$Identity'..."
        & codesign --force --deep --options runtime --timestamp --sign $Identity $BundlePath
    }

    if ($LASTEXITCODE -ne 0)
    {
        throw "codesign failed with exit code $LASTEXITCODE."
    }

    & codesign --verify --deep --strict --verbose=2 $BundlePath

    if ($LASTEXITCODE -ne 0)
    {
        throw "codesign verification failed with exit code $LASTEXITCODE."
    }
}

function New-MacDmg
{
    param(
        [string]$BundlePath,
        [string]$ReadmePath,
        [string]$OutputPath
    )

    Assert-CommandExists "ditto"
    Assert-CommandExists "hdiutil"
    Assert-CommandExists "ln"

    $stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) "danmu-neo-dmg-$([System.Guid]::NewGuid().ToString('N'))"
    $stagedBundlePath = Join-Path $stagingPath (Split-Path $BundlePath -Leaf)
    $applicationsLinkPath = Join-Path $stagingPath "Applications"

    New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null

    try
    {
        Write-Host "Preparing macOS DMG contents..."
        & ditto $BundlePath $stagedBundlePath

        if ($LASTEXITCODE -ne 0)
        {
            throw "ditto failed with exit code $LASTEXITCODE."
        }

        Copy-Item -Path $ReadmePath -Destination $stagingPath -Force
        & ln -s "/Applications" $applicationsLinkPath

        if ($LASTEXITCODE -ne 0)
        {
            throw "Creating the Applications link failed with exit code $LASTEXITCODE."
        }

        if (Test-Path $OutputPath)
        {
            Remove-Item -Force $OutputPath
        }

        Write-Host "Creating compressed macOS DMG..."
        & hdiutil create -volname $appName -srcfolder $stagingPath -format UDZO -ov $OutputPath

        if ($LASTEXITCODE -ne 0)
        {
            throw "hdiutil failed with exit code $LASTEXITCODE."
        }
    }
    finally
    {
        Remove-Item -Recurse -Force $stagingPath -ErrorAction SilentlyContinue
    }
}

Assert-PathExists $solutionPath "Solution"
Assert-PathExists $projectPath "Project"
Assert-PathExists $iconPath "Icon"
Assert-PathExists $macReadmePath "macOS first-launch guide"

if ($Clean -and (Test-Path $publishRoot))
{
    Remove-Item -Recurse -Force $publishRoot
}

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

if (-not $SkipRestore)
{
    Write-Host "Restoring solution..."
    Invoke-DotNet @("restore", $solutionPath)
}

$resolvedVersion = Resolve-PublishVersion
$winPublishPath = Join-Path $publishRoot "win-x64"
$macRootPath = Join-Path $publishRoot "osx-arm64"
$macPublishPath = Join-Path $macRootPath "publish"
$macBundlePath = Join-Path $macRootPath "$appName.app"
$macDmgPath = Join-Path $publishRoot "Yohuke-Danmu-Neo-$resolvedVersion-osx-arm64.dmg"

Publish-Runtime -RuntimeIdentifier "win-x64" -OutputPath $winPublishPath -PublishVersion $resolvedVersion
Publish-Runtime -RuntimeIdentifier "osx-arm64" -OutputPath $macPublishPath -PublishVersion $resolvedVersion
New-MacAppBundle -PublishPath $macPublishPath -BundlePath $macBundlePath -Version $resolvedVersion
Sign-MacAppBundle -BundlePath $macBundlePath -Identity $MacCodeSignIdentity
New-MacDmg -BundlePath $macBundlePath -ReadmePath $macReadmePath -OutputPath $macDmgPath

Write-Host "Published Windows app: $winPublishPath"
Write-Host "Published macOS app: $macBundlePath"
Write-Host "Published macOS DMG: $macDmgPath"
