param()
$ErrorActionPreference = 'Stop'
$moduleRoot = $PSScriptRoot
$moduleProject = Join-Path $moduleRoot 'Tactics.Multiplayer.CloudCode.csproj'
$moduleConfig = Join-Path $moduleRoot 'NuGet.Config'
$modulePackages = Join-Path $moduleRoot '.packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $moduleRoot '.nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $moduleRoot '.nuget-plugins'
$buildRoot = Join-Path $moduleRoot ('artifacts/' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $buildRoot 'publish'
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
dotnet restore $moduleProject --runtime linux-x64 -p:SelfContained=false -p:UseAppHost=false -p:UseArtifactsOutput=true -p:ArtifactsPath=$buildRoot --configfile $moduleConfig --packages $modulePackages
if ($LASTEXITCODE -ne 0) { throw 'Cloud Code package restore failed.' }
dotnet publish $moduleProject --no-restore --configuration Release --runtime linux-x64 --self-contained false -p:UseAppHost=false -p:PublishReadyToRun=false -p:UseArtifactsOutput=true -p:ArtifactsPath=$buildRoot -m:1 -nr:false --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Cloud Code module publish failed.' }
$archivePath = Join-Path $buildRoot 'TacticsMultiplayer.ccm'
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $archivePath)
Write-Output $archivePath
# Produces a local deployment archive only. No login or remote deployment occurs.
