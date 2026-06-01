$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "MysteriousCharacters.App\MysteriousCharacters.App.csproj"
$runtimeIdentifiers = @("win-x64", "win-x86", "win-arm64")

foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    dotnet publish $project --configuration Release -p:PublishProfile=$runtimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $runtimeIdentifier with exit code $LASTEXITCODE"
    }
}

$compatibleDirectory = Join-Path $PSScriptRoot "artifacts\windows-compatible"
$compatibleExecutable = Join-Path $compatibleDirectory "MysteriousCharacters.exe"
$x86Executable = Join-Path $PSScriptRoot "artifacts\win-x86\MysteriousCharacters.exe"

New-Item -ItemType Directory -Path $compatibleDirectory -Force | Out-Null
Copy-Item -LiteralPath $x86Executable -Destination $compatibleExecutable -Force

$artifactsDirectory = Join-Path $PSScriptRoot "artifacts"
$checksumPath = Join-Path $artifactsDirectory "SHA256SUMS.txt"
$executables = @(
    "win-x64\MysteriousCharacters.exe",
    "win-x86\MysteriousCharacters.exe",
    "win-arm64\MysteriousCharacters.exe",
    "windows-compatible\MysteriousCharacters.exe"
)
$checksums = foreach ($relativePath in $executables) {
    $fullPath = Join-Path $artifactsDirectory $relativePath
    $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
    "$hash  $relativePath"
}
Set-Content -LiteralPath $checksumPath -Value $checksums -Encoding ascii

Write-Host ""
Write-Host "Native x64:   $PSScriptRoot\artifacts\win-x64\MysteriousCharacters.exe"
Write-Host "Native x86:   $PSScriptRoot\artifacts\win-x86\MysteriousCharacters.exe"
Write-Host "Native ARM64: $PSScriptRoot\artifacts\win-arm64\MysteriousCharacters.exe"
Write-Host "Compatible:   $compatibleExecutable"
Write-Host "Checksums:    $checksumPath"
