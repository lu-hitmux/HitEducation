param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PackageSuffix = "",

    [bool]$UpdateUpdater = $true,

    [string[]]$Notes = @(),

    [switch]$Upload,

    [switch]$UploadGitHub,

    [switch]$UploadGitee,

    [string]$Server = "lu@154.89.153.201",

    [string]$ServerDirectory = "/var/www/html/HitPrograms/HitEducation",

    [string]$BaseUrl = "https://pack.hitmc.net/HitPrograms/HitEducation",

    [string]$GitHubRepo = "lu-hitmux/HitEducation",

    [string]$GitHubTag = "",

    [string]$GiteeOwner = "lu-hitmux",

    [string]$GiteeRepo = "HitEducation",

    [string]$GiteeTag = "",

    [string]$GiteeToken = $env:GITEE_TOKEN
)

$ErrorActionPreference = "Stop"

function Get-CommandPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string[]]$FallbackPaths = @()
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $FallbackPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw "Required command '$Name' was not found."
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$DownloadBaseUrl
    )

    $manifest = [ordered]@{
        version = $Version
        title = "HitEducation $Version"
        downloadUrl = "$DownloadBaseUrl/$appZipName"
        sha256 = $appSha256
        publishedAt = (Get-Date -Format "yyyy-MM-dd")
        updateUpdater = $UpdateUpdater
        updaterDownloadUrl = "$DownloadBaseUrl/$updaterZipName"
        updaterSha256 = $updaterSha256
        required = $false
        notes = $Notes
    }

    $manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Publish-GitHubRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    $gh = Get-CommandPath -Name "gh" -FallbackPaths @("C:\Program Files\GitHub CLI\gh.exe")
    $title = "HitEducation $Version"
    $body = ($Notes -join "`n")
    if ([string]::IsNullOrWhiteSpace($body)) {
        $body = "HitEducation $Version update."
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $gh release view $Tag --repo $GitHubRepo *> $null
    $releaseViewExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference

    if ($releaseViewExitCode -eq 0) {
        & $gh release upload $Tag $appZip $updaterZip $ManifestPath --repo $GitHubRepo --clobber
    }
    else {
        & $gh release create $Tag $appZip $updaterZip $ManifestPath --repo $GitHubRepo --title $title --notes $body
    }
}

function New-GiteeRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    if ([string]::IsNullOrWhiteSpace($GiteeToken)) {
        throw "Gitee token is required. Pass -GiteeToken or set GITEE_TOKEN."
    }

    $bodyText = ($Notes -join "`n")
    if ([string]::IsNullOrWhiteSpace($bodyText)) {
        $bodyText = "HitEducation $Version update."
    }

    $body = @{
        access_token = $GiteeToken
        tag_name = $Tag
        target_commitish = "main"
        name = "HitEducation $Version"
        body = $bodyText
        prerelease = "false"
    }

    try {
        return Invoke-RestMethod -Method Post -Uri "https://gitee.com/api/v5/repos/$GiteeOwner/$GiteeRepo/releases" -Body $body -TimeoutSec 60
    }
    catch {
        $message = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = $_.Exception.Message
        }

        throw "Failed to create Gitee release '$Tag'. Use a unique -GiteeTag to avoid duplicate attachment/cache issues. $message"
    }
}

function Publish-GiteeAttachment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseId,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$FileName = ""
    )

    $curl = Get-CommandPath -Name "curl.exe"
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ([string]::IsNullOrWhiteSpace($FileName)) {
        & $curl -sS -X POST "https://gitee.com/api/v5/repos/$GiteeOwner/$GiteeRepo/releases/$ReleaseId/attach_files" -F "access_token=$GiteeToken" -F "file=@$resolved"
    }
    else {
        & $curl -sS -X POST "https://gitee.com/api/v5/repos/$GiteeOwner/$GiteeRepo/releases/$ReleaseId/attach_files" -F "access_token=$GiteeToken" -F "file=@$resolved;filename=$FileName"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload Gitee attachment '$Path'."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$assemblyInfo = Join-Path $repoRoot "app\Properties\AssemblyInfo.cs"
$appProject = Join-Path $repoRoot "app\HitEducation.App.csproj"
$publish = Join-Path $repoRoot "app\bin\Release\net8.0-windows\win-x64\publish"
$packageDir = Join-Path $repoRoot "app\bin\Release\net8.0-windows\win-x64\packages"
$sourceManifest = Join-Path $repoRoot "update\version.json"

if ([string]::IsNullOrWhiteSpace($PackageSuffix)) {
    $PackageSuffix = $Version
}

if ([string]::IsNullOrWhiteSpace($GitHubTag)) {
    $GitHubTag = "v$Version"
}

if ([string]::IsNullOrWhiteSpace($GiteeTag)) {
    $GiteeTag = "v$PackageSuffix"
}

$version4 = "$Version.0"
$content = [System.IO.File]::ReadAllText($assemblyInfo)
$content = [regex]::Replace($content, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$version4`")")
$content = [regex]::Replace($content, 'AssemblyInformationalVersion\("[^"]+"\)', "AssemblyInformationalVersion(`"$Version`")")
$content = [regex]::Replace($content, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$version4`")")
[System.IO.File]::WriteAllText($assemblyInfo, $content, [System.Text.UTF8Encoding]::new($false))

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

$updaterExe = Join-Path $publish "HitEducation.Updater.exe"
$updaterAppHostText = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($updaterExe))
if ($updaterAppHostText.Contains("HitEducation.App.dll")) {
    throw "Updater apphost is bound to HitEducation.App.dll."
}
if (!$updaterAppHostText.Contains("HitEducation.Updater.dll")) {
    throw "Updater apphost is not bound to HitEducation.Updater.dll."
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

$versionJson = Join-Path $packageDir "version.json"
Write-Manifest -Path $versionJson -DownloadBaseUrl $BaseUrl

$githubVersionJson = $null
if ($UploadGitHub) {
    $githubBaseUrl = "https://github.com/$GitHubRepo/releases/download/$GitHubTag"
    $githubVersionJson = Join-Path $packageDir "version-github.json"
    Write-Manifest -Path $githubVersionJson -DownloadBaseUrl $githubBaseUrl
    Publish-GitHubRelease -Tag $GitHubTag -ManifestPath $githubVersionJson
}

$giteeVersionJson = $null
$giteeReleaseUrl = $null
if ($UploadGitee) {
    $giteeBaseUrl = "https://gitee.com/$GiteeOwner/$GiteeRepo/releases/download/$GiteeTag"
    $giteeVersionJson = Join-Path $packageDir "version-gitee.json"
    Write-Manifest -Path $giteeVersionJson -DownloadBaseUrl $giteeBaseUrl
    $sourceManifestDirectory = Split-Path -Parent $sourceManifest
    if (!(Test-Path -LiteralPath $sourceManifestDirectory)) {
        New-Item -ItemType Directory -Path $sourceManifestDirectory | Out-Null
    }
    Copy-Item -LiteralPath $giteeVersionJson -Destination $sourceManifest -Force
    $giteeRelease = New-GiteeRelease -Tag $GiteeTag
    $giteeReleaseUrl = "https://gitee.com/$GiteeOwner/$GiteeRepo/releases/tag/$GiteeTag"
    Publish-GiteeAttachment -ReleaseId $giteeRelease.id -Path $appZip | Out-Null
    Publish-GiteeAttachment -ReleaseId $giteeRelease.id -Path $updaterZip | Out-Null
}

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
    GitHubVersionJson = $githubVersionJson
    GiteeVersionJson = $giteeVersionJson
    GitHubReleaseUrl = if ($UploadGitHub) { "https://github.com/$GitHubRepo/releases/tag/$GitHubTag" } else { $null }
    GiteeReleaseUrl = $giteeReleaseUrl
    UploadedServer = [bool]$Upload
    UploadedGitHub = [bool]$UploadGitHub
    UploadedGitee = [bool]$UploadGitee
}
