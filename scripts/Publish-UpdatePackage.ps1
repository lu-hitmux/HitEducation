param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PackageSuffix = "",

    [bool]$UpdateUpdater = $true,

    [string[]]$Notes = @(),

    [switch]$Upload,

    [string]$Server = "lu@154.89.153.201",

    [string]$ServerDirectory = "/var/www/html/HitPrograms/HitEducation",

    [string]$BaseUrl = "https://pack.hitmc.net/HitPrograms/HitEducation"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$assemblyInfo = Join-Path $repoRoot "app\Properties\AssemblyInfo.cs"
$appProject = Join-Path $repoRoot "app\HitEducation.App.csproj"
$publish = Join-Path $repoRoot "app\bin\Release\net8.0-windows\win-x64\publish"
$packageDir = Join-Path $repoRoot "app\bin\Release\net8.0-windows\win-x64\packages"

if ([string]::IsNullOrWhiteSpace($PackageSuffix)) {
    $PackageSuffix = $Version
}

$version4 = "$Version.0"
$content = Get-Content -LiteralPath $assemblyInfo -Raw
$content = [regex]::Replace($content, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$version4`")")
$content = [regex]::Replace($content, 'AssemblyInformationalVersion\("[^"]+"\)', "AssemblyInformationalVersion(`"$Version`")")
$content = [regex]::Replace($content, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$version4`")")
Set-Content -LiteralPath $assemblyInfo -Value $content -Encoding UTF8

$runningApps = @(Get-Process -Name "HitEducation.App" -ErrorAction SilentlyContinue)
if ($runningApps.Count -gt 0) {
    $runningApps | Stop-Process -Force
}

dotnet build $appProject
dotnet publish $appProject -c Release -r win-x64 --self-contained false

$publishedVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish "HitEducation.App.dll")).ProductVersion
if ($publishedVersion -ne $Version) {
    throw "Published app version is $publishedVersion, expected $Version."
}

if (!(Test-Path -LiteralPath $packageDir)) {
    New-Item -ItemType Directory -Path $packageDir | Out-Null
}

$appZipName = "HitEducation.App-$PackageSuffix.zip"
$updaterZipName = "HitEducation.Updater-$PackageSuffix.zip"
$appZip = Join-Path $packageDir $appZipName
$updaterZip = Join-Path $packageDir $updaterZipName

foreach ($zip in @($appZip, $updaterZip)) {
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }
}

$appFiles = @(Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -notlike "HitEducation.Updater*" } | ForEach-Object { $_.FullName })
$updaterFiles = @(Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -like "HitEducation.Updater*" } | ForEach-Object { $_.FullName })
Compress-Archive -Path $appFiles -DestinationPath $appZip -Force
Compress-Archive -Path $updaterFiles -DestinationPath $updaterZip -Force

$checkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("HitEducation-package-check-" + [Guid]::NewGuid().ToString("N"))
Expand-Archive -LiteralPath $appZip -DestinationPath $checkDir -Force
$zipVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $checkDir "HitEducation.App.dll")).ProductVersion
Remove-Item -LiteralPath $checkDir -Recurse -Force
if ($zipVersion -ne $Version) {
    throw "App zip version is $zipVersion, expected $Version."
}

$appSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $appZip).Hash
$updaterSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $updaterZip).Hash

if ($Notes.Count -eq 0) {
    $Notes = @("HitEducation $Version update.")
}

$manifest = [ordered]@{
    version = $Version
    title = "HitEducation $Version"
    downloadUrl = "$BaseUrl/$appZipName"
    sha256 = $appSha256
    publishedAt = (Get-Date -Format "yyyy-MM-dd")
    updateUpdater = $UpdateUpdater
    updaterDownloadUrl = "$BaseUrl/$updaterZipName"
    updaterSha256 = $updaterSha256
    required = $false
    notes = $Notes
}

$versionJson = Join-Path $packageDir "version.json"
$manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath $versionJson -Encoding UTF8

if ($Upload) {
    scp $appZip $updaterZip $versionJson "${Server}:${ServerDirectory}/"
}

[pscustomobject]@{
    Version = $Version
    PackageSuffix = $PackageSuffix
    AppZip = $appZip
    AppSha256 = $appSha256
    UpdaterZip = $updaterZip
    UpdaterSha256 = $updaterSha256
    VersionJson = $versionJson
    Uploaded = [bool]$Upload
}
