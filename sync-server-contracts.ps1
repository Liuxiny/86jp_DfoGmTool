param(
  [string]$ServerRoot = (Join-Path $PSScriptRoot '..\ServerS4A21_git')
)

$ErrorActionPreference = 'Stop'
$serverRootBase = (Get-Location).ProviderPath
if (-not [IO.Path]::IsPathRooted($ServerRoot)) {
  $ServerRoot = Join-Path $serverRootBase $ServerRoot
}
$ServerRoot = [IO.Path]::GetFullPath($ServerRoot)
$serverSchema = Join-Path $ServerRoot 'Server\DfoServer\Sqlite\item_schema.sql'
$serverMigrations = Join-Path $ServerRoot 'Server\DfoServer\Sqlite\SqliteMigrations.cs'
$serverPvfLib = Join-Path $ServerRoot 'Tool\PvfLib'
$serverQuestRoot = Join-Path $ServerRoot 'Server\DfoServer\Game\Quests'
$targetSchema = Join-Path $PSScriptRoot 'ServerCore\Sqlite\item_schema.sql'
$targetPvfLib = Join-Path $PSScriptRoot 'PvfLib'
$targetQuestRoot = Join-Path $PSScriptRoot 'ServerCore\Game\Quests'

$questContractFiles = @(
  'ActiveQuest.cs',
  'QuestRepository.cs',
  'QuestSlotLayout.cs'
)

foreach ($required in @($serverSchema, $serverMigrations, $serverPvfLib, $targetPvfLib, $serverQuestRoot, $targetQuestRoot)) {
  if (-not (Test-Path -LiteralPath $required)) {
    throw "Required contract source is missing: $required"
  }
}

[IO.File]::WriteAllBytes(
  $targetSchema,
  [IO.File]::ReadAllBytes($serverSchema))

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$questHashes = [ordered]@{}
foreach ($relative in $questContractFiles) {
  $source = Join-Path $serverQuestRoot $relative
  if (-not (Test-Path -LiteralPath $source)) {
    throw "Required quest contract source is missing: $source"
  }
  $destination = Join-Path $targetQuestRoot $relative
  $content = [IO.File]::ReadAllText($source, [Text.Encoding]::UTF8)
  $content = $content.Replace(
    'namespace DfoServer.Game.Quests',
    'namespace DfoGmTool.ServerCore.Game.Quests')
  [IO.File]::WriteAllText(
    $destination,
    ($content -replace "`r`n", "`n"),
    $utf8NoBom)
  $questHashes[$relative] = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
}

$serverSources = @(
  Get-ChildItem -LiteralPath $serverPvfLib -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
)
$relativeSources = @{}
foreach ($source in $serverSources) {
  $relative = $source.FullName.Substring($serverPvfLib.Length).TrimStart([char]92, [char]47)
  $relativeSources[$relative] = $true
  $destination = Join-Path $targetPvfLib $relative
  $destinationDirectory = Split-Path -Parent $destination
  New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
  $content = [IO.File]::ReadAllText($source.FullName, [Text.Encoding]::UTF8)
  $content = $content.Replace('namespace PvfLib', 'namespace GmPvfLib')
  [IO.File]::WriteAllText(
    $destination,
    ($content -replace "`r`n", "`n"),
    $utf8NoBom)
}

$staleSources = @(
  Get-ChildItem -LiteralPath $targetPvfLib -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' } |
    Where-Object {
      $relative = $_.FullName.Substring($targetPvfLib.Length).TrimStart([char]92, [char]47)
      -not $relativeSources.ContainsKey($relative)
    }
)
if ($staleSources.Count -gt 0) {
  throw "GM PvfLib contains stale source files not present upstream: $($staleSources.FullName -join ', ')"
}

$safeRoot = $ServerRoot -replace '\\', '/'
$serverCommit = (& git -c "safe.directory=$safeRoot" -C $ServerRoot rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($serverCommit)) {
  throw 'Could not read the server commit for the contract manifest.'
}

$migrationSource = [IO.File]::ReadAllText($serverMigrations, [Text.Encoding]::UTF8)
$baselineMatch = [regex]::Match($migrationSource, 'BaselineId\s*=\s*"([^"]+)"')
if (-not $baselineMatch.Success) {
  throw 'Could not read the A21 database baseline from SqliteMigrations.cs.'
}
$schemaMatches = [regex]::Matches($migrationSource, 'new\s+MigrationStep\((\d+)\s*,')
if ($schemaMatches.Count -eq 0) {
  throw 'Could not read the A21 schema version from SqliteMigrations.cs.'
}
$schemaVersion = [int](($schemaMatches | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum)
$baselineId = $baselineMatch.Groups[1].Value

$pvfHashes = [ordered]@{}
foreach ($relative in ($relativeSources.Keys | Sort-Object)) {
  $pvfHashes[$relative] = (Get-FileHash (Join-Path $targetPvfLib $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
  serverCommit = $serverCommit
  baselineId = $baselineId
  schemaVersion = $schemaVersion
  schemaSha256 = (Get-FileHash $targetSchema -Algorithm SHA256).Hash.ToLowerInvariant()
  compatibilityPatches = @()
  questContractSourceFiles = $questHashes
  pvfSourceFiles = $pvfHashes
}
$manifestPath = Join-Path $PSScriptRoot 'server-contract-manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($manifestPath, $manifestJson + "`n", $utf8NoBom)

Write-Host "Synced server schema v$schemaVersion ($baselineId), $($questContractFiles.Count) quest contract files, and $($serverSources.Count) PvfLib source files."
Write-Host "Server commit: $serverCommit"
Write-Host "Manifest: $manifestPath"
