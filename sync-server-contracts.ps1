param(
  [string]$ServerRoot = (Join-Path $PSScriptRoot '..\ServerS4A12_git')
)

$ErrorActionPreference = 'Stop'
$serverRootBase = (Get-Location).ProviderPath
if (-not [IO.Path]::IsPathRooted($ServerRoot)) {
  $ServerRoot = Join-Path $serverRootBase $ServerRoot
}
$ServerRoot = [IO.Path]::GetFullPath($ServerRoot)
$serverSchema = Join-Path $ServerRoot 'Server\DfoServer\Sqlite\item_schema.sql'
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

foreach ($required in @($serverSchema, $serverPvfLib, $targetPvfLib, $serverQuestRoot, $targetQuestRoot)) {
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

# Upstream commit 211663c made PvfArchive accept only an unguarded header, while
# the same server revision still ships a guarded Script.pvf. Keep this small,
# asserted compatibility patch in the reproducible sync step until upstream
# restores dual-format parsing.
$archivePath = Join-Path $targetPvfLib 'PvfArchive.cs'
$archive = [IO.File]::ReadAllText($archivePath, [Text.Encoding]::UTF8)
$patches = @(
  @(
    '        private PvfHeader _header;',
    "        private PvfHeader _header;`n        private bool _headerUsesGuard;"),
  @(
    "            var header = _header;`n            byte[] headerBytes = StructToBytes(header);`n            PvfDecryptor.Decrypt(`"HeaD`", headerBytes);",
    "            var header = _header;`n            byte[] headerBytes = StructToBytes(header);`n            PvfDecryptor.Decrypt(`"HeaD`", headerBytes);`n            if (_headerUsesGuard)`n                PvfDecryptor.DecryptGuard(headerBytes);"),
  @(
    @'
            byte[] headerBytes = allBytes.Slice(0, 0x30);
            if (PvfDecryptor.Decrypt("HeaD", headerBytes) != 0)
                throw new InvalidDataException("PVF 头部解密失败");

            var header = headerBytes.ToStruct<PvfHeader>();
            if (header.Signature != MagicSignature)
                throw new InvalidDataException("无效的 PVF 签名");
            ValidateHeaderLayout(header, allBytes.Length);
'@,
    @'
            PvfHeader header = default;
            bool decoded = false;
            Exception lastHeaderError = null;
            foreach (var usesGuard in new[] { true, false })
            {
                try
                {
                    header = DecodeHeaderCandidate(allBytes, usesGuard);
                    _headerUsesGuard = usesGuard;
                    decoded = true;
                    break;
                }
                catch (InvalidDataException ex)
                {
                    lastHeaderError = ex;
                }
            }

            if (!decoded)
                throw new InvalidDataException("PVF header did not match a supported format.", lastHeaderError);
'@),
  @(
    '        private static void ValidateHeaderLayout(PvfHeader header, int dataLength)',
    @'
        private static PvfHeader DecodeHeaderCandidate(byte[] allBytes, bool usesGuard)
        {
            byte[] headerBytes = allBytes.Slice(0, 0x30);
            if (usesGuard)
                PvfDecryptor.DecryptGuard(headerBytes);
            if (PvfDecryptor.Decrypt("HeaD", headerBytes) != 0)
                throw new InvalidDataException("PVF header decryption failed.");

            var header = headerBytes.ToStruct<PvfHeader>();
            if (header.Signature != MagicSignature)
                throw new InvalidDataException("PVF signature is invalid.");
            ValidateHeaderLayout(header, allBytes.Length);
            return header;
        }

        private static void ValidateHeaderLayout(PvfHeader header, int dataLength)
'@),
  @(
    "                PvfDecryptor.Decrypt(`"HeaD`", headerBytes);`n`n                using (var outFs",
    "                PvfDecryptor.Decrypt(`"HeaD`", headerBytes);`n                if (_headerUsesGuard)`n                    PvfDecryptor.DecryptGuard(headerBytes);`n`n                using (var outFs")
)
$patchIndex = 0
foreach ($patch in $patches) {
  $patchIndex++
  if (-not $archive.Contains($patch[0])) {
    throw "The upstream PvfArchive layout changed at compatibility patch $patchIndex; review the guarded-header compatibility patch."
  }
  $archive = $archive.Replace($patch[0], $patch[1])
}
[IO.File]::WriteAllText($archivePath, ($archive -replace "`r`n", "`n"), $utf8NoBom)

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

$pvfHashes = [ordered]@{}
foreach ($relative in ($relativeSources.Keys | Sort-Object)) {
  $pvfHashes[$relative] = (Get-FileHash (Join-Path $targetPvfLib $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
  serverCommit = $serverCommit
  schemaVersion = 52
  schemaSha256 = (Get-FileHash $targetSchema -Algorithm SHA256).Hash.ToLowerInvariant()
  compatibilityPatches = @('PvfArchive: accept guarded and unguarded headers; preserve source format')
  questContractSourceFiles = $questHashes
  pvfSourceFiles = $pvfHashes
}
$manifestPath = Join-Path $PSScriptRoot 'server-contract-manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($manifestPath, $manifestJson + "`n", $utf8NoBom)

Write-Host "Synced server schema v52, $($questContractFiles.Count) quest contract files, and $($serverSources.Count) PvfLib source files."
Write-Host "Server commit: $serverCommit"
Write-Host "Manifest: $manifestPath"
