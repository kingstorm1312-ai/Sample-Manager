[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Notes = ''
)

$ErrorActionPreference = 'Stop'

$Repository = 'kingstorm1312-ai/Sample-Manager'
$Branch = 'main'
$ApplicationName = 'Sample Manager.exe'
$UpdaterName = 'Sample Manager Updater.exe'
$ManifestUrl = 'https://github.com/kingstorm1312-ai/Sample-Manager/releases/latest/download/update.json'
$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$AssemblyFiles = @(
    (Join-Path $RepositoryRoot 'src\SampleManager\Properties\AssemblyInfo.cs'),
    (Join-Path $RepositoryRoot 'src\SampleManagerUpdater\Properties\AssemblyInfo.cs')
)
$VersionWithRevision = $Version + '.0'
$ReleaseDirectory = Join-Path $RepositoryRoot ('release\' + $Version)
$PayloadDirectory = Join-Path $ReleaseDirectory 'payload'
$ZipPath = Join-Path $ReleaseDirectory ('Sample.Manager-' + $Version + '-final.zip')
$UpdateJsonPath = Join-Path $ReleaseDirectory 'update.json'
$Tag = 'v' + $Version
$ReleaseNotes = if ([string]::IsNullOrWhiteSpace($Notes)) { 'Sample Manager ' + $Version } else { $Notes.Trim() }
$utf8Strict = New-Object System.Text.UTF8Encoding -ArgumentList @($false, $true)
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList @($false)
$locationPushed = $false

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $false)]
        [string[]]$ArgumentList = @()
    )

    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ($FilePath + ' failed with exit code ' + $exitCode + '.')
    }
}

function Set-AssemblyVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ('AssemblyInfo.cs not found: ' + $Path)
    }

    $content = [System.IO.File]::ReadAllText($Path, $utf8Strict)
    $attributes = [ordered]@{
        AssemblyVersion = $VersionWithRevision
        AssemblyFileVersion = $VersionWithRevision
        AssemblyInformationalVersion = $Version
    }

    foreach ($attribute in $attributes.Keys) {
        $pattern = '\[assembly:\s*' + [regex]::Escape($attribute) + '\(".*?"\)\]'
        $matches = [regex]::Matches($content, $pattern)
        if ($matches.Count -ne 1) {
            throw ('Expected exactly one ' + $attribute + ' attribute in ' + $Path + '.')
        }

        $replacement = '[assembly: ' + $attribute + '("' + $attributes[$attribute] + '")]'
        $content = [regex]::Replace($content, $pattern, $replacement)
    }

    [System.IO.File]::WriteAllText($Path, $content, $utf8NoBom)
}

function Invoke-MSBuildRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    $msbuildCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\MSBuild.exe')
    )
    $msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ([string]::IsNullOrEmpty($msbuild)) {
        throw 'MSBuild.exe was not found.'
    }

    $output = @(& $msbuild $ProjectPath '/t:Build' '/p:Configuration=Release' '/v:normal' 2>&1)
    $exitCode = $LASTEXITCODE
    $text = [string]::Join([Environment]::NewLine, @($output | ForEach-Object { $_.ToString() }))
    Write-Host $text

    if ($exitCode -ne 0) {
        throw ('Build failed for ' + $ProjectPath + '.')
    }

    $warningSummary = [regex]::Matches($text, '(?im)^\s*(\d+)\s+Warning\(s\)\s*$')
    $errorSummary = [regex]::Matches($text, '(?im)^\s*(\d+)\s+Error\(s\)\s*$')
    if ($warningSummary.Count -ne 1 -or $warningSummary[0].Groups[1].Value -ne '0') {
        throw ('Build warning gate failed for ' + $ProjectPath + '.')
    }
    if ($errorSummary.Count -ne 1 -or $errorSummary[0].Groups[1].Value -ne '0') {
        throw ('Build error gate failed for ' + $ProjectPath + '.')
    }
}

function Assert-PackageStructure {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -ne 1 -or $entries[0].FullName -ne $ApplicationName) {
            throw 'Release ZIP must contain exactly the root Sample Manager.exe entry.'
        }

        foreach ($entry in $entries) {
            if ($entry.FullName -match '(?i)(^|[\\/])\.secrets([\\/]|$)|auth-state\.dat|token|credential') {
                throw ('Protected local state found in release ZIP: ' + $entry.FullName)
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    $output = @(& git @ArgumentList 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ('git ' + ($ArgumentList -join ' ') + ' failed with exit code ' + $exitCode + '.')
    }

    return [string]::Join([Environment]::NewLine, @($output | ForEach-Object { $_.ToString() })).Trim()
}

function Get-GhJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    $output = @(& gh @ArgumentList 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ('gh ' + ($ArgumentList -join ' ') + ' failed with exit code ' + $exitCode + '.')
    }

    $text = [string]::Join([Environment]::NewLine, @($output | ForEach-Object { $_.ToString() }))
    return ($text | ConvertFrom-Json)
}

try {
    Push-Location $RepositoryRoot
    $locationPushed = $true

    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $ghCommand) {
        throw 'GitHub CLI gh was not found in PATH.'
    }

    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        throw 'Git was not found in PATH.'
    }

    & gh auth status --hostname github.com
    if ($LASTEXITCODE -ne 0) {
        throw 'gh auth status failed. Run gh auth login before releasing.'
    }

    Invoke-Native 'gh' @('auth', 'setup-git', '--hostname', 'github.com')

    $gitRoot = Get-GitOutput @('rev-parse', '--show-toplevel')
    $gitRoot = [System.IO.Path]::GetFullPath($gitRoot)
    if ($gitRoot.TrimEnd('\') -ne $RepositoryRoot.TrimEnd('\')) {
        throw ('Git root does not match the Sample Manager workspace: ' + $gitRoot)
    }

    $currentBranch = Get-GitOutput @('branch', '--show-current')
    if ($currentBranch -ne $Branch) {
        throw ('Current branch must be ' + $Branch + ', but is ' + $currentBranch + '.')
    }

    $remoteUrl = Get-GitOutput @('remote', 'get-url', 'origin')
    if ($remoteUrl -notmatch '(?i)github\.com[/:]kingstorm1312-ai/Sample-Manager(?:\.git)?$') {
        throw ('origin does not point to ' + $Repository + ': ' + $remoteUrl)
    }

    $existingRelease = @(& gh release view $Tag '--repo' $Repository 2>$null)
    if ($LASTEXITCODE -eq 0) {
        throw ('GitHub Release already exists: ' + $Tag)
    }

    $remoteTag = @(& git ls-remote '--exit-code' '--quiet' '--tags' origin ('refs/tags/' + $Tag) 2>$null)
    $remoteTagExitCode = $LASTEXITCODE
    if ($remoteTagExitCode -eq 0) {
        throw ('Git tag already exists: ' + $Tag)
    }
    if ($remoteTagExitCode -ne 2) {
        throw ('Could not verify whether Git tag exists: ' + $Tag)
    }

    foreach ($assemblyFile in $AssemblyFiles) {
        Set-AssemblyVersion $assemblyFile
    }

    Invoke-MSBuildRelease (Join-Path $RepositoryRoot 'src\SampleManager\SampleManager.csproj')
    Invoke-MSBuildRelease (Join-Path $RepositoryRoot 'src\SampleManagerUpdater\SampleManagerUpdater.csproj')

    $applicationPath = Join-Path $RepositoryRoot $ApplicationName
    $updaterPath = Join-Path $RepositoryRoot $UpdaterName
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw ('Build output not found: ' + $applicationPath)
    }
    if (-not (Test-Path -LiteralPath $updaterPath -PathType Leaf)) {
        throw ('Build output not found: ' + $updaterPath)
    }

    if (Test-Path -LiteralPath $ReleaseDirectory) {
        throw ('Release directory already exists: ' + $ReleaseDirectory)
    }
    New-Item -ItemType Directory -Path $PayloadDirectory -Force | Out-Null
    Copy-Item -LiteralPath $applicationPath -Destination (Join-Path $PayloadDirectory $ApplicationName)
    Compress-Archive -LiteralPath (Join-Path $PayloadDirectory $ApplicationName) -DestinationPath $ZipPath -CompressionLevel Optimal
    Assert-PackageStructure $ZipPath

    $sha256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $downloadUrl = 'https://github.com/kingstorm1312-ai/Sample-Manager/releases/download/' + $Tag + '/Sample.Manager-' + $Version + '-final.zip'
    $manifest = [ordered]@{
        version = $Version
        downloadUrl = $downloadUrl
        sha256 = $sha256
        mandatory = $false
        notes = $ReleaseNotes
    }
    $manifestText = ($manifest | ConvertTo-Json -Depth 3) + [Environment]::NewLine
    [System.IO.File]::WriteAllText($UpdateJsonPath, $manifestText, $utf8NoBom)

    Invoke-Native 'git' @('add', '-A', '--', '.')
    $stagedLegacy = @(& git diff --cached --name-only | Where-Object { $_ -like '.release/*' -or $_ -like '.release\*' })
    if ($stagedLegacy.Count -gt 0) {
        Invoke-Native 'git' @('reset', '--quiet', '--', '.release')
    }

    $stagedFiles = @(Get-GitOutput @('diff', '--cached', '--name-only') -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($stagedFile in $stagedFiles) {
        if ($stagedFile -match '^(?i)(\.secrets|release)(/|\\)' -or $stagedFile -match '(?i)(auth-state\.dat|google_token|credential)') {
            throw ('Protected or generated file is staged: ' + $stagedFile)
        }
    }
    if ($stagedFiles.Count -eq 0) {
        throw 'No source changes are staged for the release commit.'
    }

    Invoke-Native 'git' @('commit', '-m', ('release: Sample Manager ' + $Version))
    Invoke-Native 'git' @('push', 'origin', $Branch)

    Invoke-Native 'gh' @('release', 'create', $Tag, $ZipPath, $UpdateJsonPath, '--repo', $Repository, '--title', ('Sample Manager ' + $Version), '--notes', $ReleaseNotes)

    $release = Get-GhJson @('release', 'view', $Tag, '--repo', $Repository, '--json', 'tagName,name,assets')
    if ($release.tagName -ne $Tag -or @($release.assets).Count -ne 2) {
        throw 'GitHub Release verification failed: expected the requested tag and exactly 2 assets.'
    }
    $assetNames = @($release.assets | ForEach-Object { $_.name })
    if ($assetNames -notcontains ('Sample.Manager-' + $Version + '-final.zip') -or $assetNames -notcontains 'update.json') {
        throw 'GitHub Release verification failed: ZIP/update.json assets are incomplete.'
    }

    $latestText = (New-Object System.Net.WebClient).DownloadString($ManifestUrl)
    $latest = $latestText | ConvertFrom-Json
    if ($latest.version -ne $Version -or $latest.sha256 -ne $sha256 -or $latest.downloadUrl -ne $downloadUrl) {
        throw 'latest/update.json verification failed.'
    }

    $downloadDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('SampleManager-release-' + [Guid]::NewGuid().ToString('N'))
    $downloadedZip = Join-Path $downloadDirectory ('Sample.Manager-' + $Version + '-downloaded.zip')
    New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
    try {
        (New-Object System.Net.WebClient).DownloadFile($downloadUrl, $downloadedZip)
        $downloadedSha256 = (Get-FileHash -LiteralPath $downloadedZip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($downloadedSha256 -ne $sha256) {
            throw ('Downloaded ZIP SHA-256 mismatch. Expected ' + $sha256 + ', actual ' + $downloadedSha256 + '.')
        }
    }
    finally {
        if (Test-Path -LiteralPath $downloadDirectory) {
            Remove-Item -LiteralPath $downloadDirectory -Recurse -Force
        }
    }

    Write-Host ''
    Write-Host ('PASS: Sample Manager ' + $Version)
    Write-Host ('Release: https://github.com/kingstorm1312-ai/Sample-Manager/releases/tag/' + $Tag)
    Write-Host ('SHA-256: ' + $sha256)
}
catch {
    Write-Error ('[FAIL] ' + $_.Exception.Message)
    exit 1
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}
