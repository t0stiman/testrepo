param (
    [switch]$NoArchive,
    [string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"

$FilesToInclude = "info.json","build/runtime/*","LICENSE","locale.csv"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id
$modVersion = $modInfo.Version

$DistDir = "$OutputDirectory/dist"
if ($NoArchive) {
    $ZipWorkDir = "$OutputDirectory"
} else {
    $ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/Mapify"

New-Item "$ZipOutDir" -ItemType Directory -Force
Copy-Item -Force -Path $FilesToInclude -Destination "$ZipOutDir"

if (!$NoArchive)
{
    $FILE_NAME = "$DistDir/${modId}_v$modVersion.zip"
    Compress-Archive -Update -CompressionLevel Fastest -Path "$ZipOutDir" -DestinationPath "$FILE_NAME"
}

# ===================================

Copy-Item -Force -Path "build/editor/*" -Destination "C:\Users\Borre\Projects\game mods\Derail Valley\dv-testimap\Assets\Mapify\Scripts"
Copy-Item -Force -Path "build/editor/*" -Destination "C:\Users\Borre\Projects\game mods\Derail Valley\dv-hamburg-harbor-api\Assets\Mapify\Scripts"
Copy-Item -Force -Path "build/editor/*" -Destination "C:\Users\Borre\Projects\game mods\Derail Valley\dv_san_andreas\Assets\Mapify\Scripts"
Copy-Item -Force -Path "build/editor/*" -Destination "C:\Users\Borre\Projects\game mods\Derail Valley\dv-hamburg-testo\Assets\Mapify\Scripts"
