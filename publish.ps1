$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "MysteriousCharacters.App\MysteriousCharacters.App.csproj"

dotnet publish $project --configuration Release -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Published: $PSScriptRoot\artifacts\win-x64\MysteriousCharacters.exe"
