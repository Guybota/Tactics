param([Parameter(Mandatory = $true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
$allowedBuildRoot = [IO.Path]::GetFullPath((Join-Path $workspacePath 'Build')) + [IO.Path]::DirectorySeparatorChar
if (!$buildPath.StartsWith($allowedBuildRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Select a build directory under this workspace Build folder.'
}
$indexPath = Join-Path $buildPath 'index.html'
$html = [IO.File]::ReadAllText($indexPath)
# Unity's default template opts out of automatic IndexedDB persistence. The online
# recovery journal needs file changes to survive a browser reload, alongside PlayerPrefs.
$marker = '// config.autoSyncPersistentDataPath = true;'
if ($html.Contains($marker)) {
    $html = $html.Replace($marker, 'config.autoSyncPersistentDataPath = true;')
    [IO.File]::WriteAllText($indexPath, $html, [Text.UTF8Encoding]::new($false))
}
if ($html -notmatch '(?m)^\s*config\.autoSyncPersistentDataPath\s*=\s*true;') {
    throw 'The WebGL template must explicitly enable persistent-data autosync.'
}
$buildFiles = @(Get-ChildItem -LiteralPath (Join-Path $buildPath 'Build') -File)
if ($buildFiles.Count -lt 4 -or !($buildFiles.Name -match '\.loader\.js$') -or !($buildFiles.Name -match '\.wasm')) {
    throw 'The selected directory does not contain a complete WebGL build.'
}
$sourceRevision = & git -c "safe.directory=$($workspacePath.Replace('\', '/'))" -C $workspacePath rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the source revision.' }
$metadata = [ordered]@{
    sourceRevision = $sourceRevision.Trim()
    preparedAtUtc = [DateTime]::UtcNow.ToString('o')
    itchTarget = 'guybota/lords-of-conflict:web'
    multiplayerEnvironment = 'development'
    autoSyncPersistentDataPath = $true
}
[IO.File]::WriteAllText((Join-Path $buildPath 'release.json'), ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Prepared WebGL build: $buildPath"
Write-Output "Source revision: $($metadata.sourceRevision)"
