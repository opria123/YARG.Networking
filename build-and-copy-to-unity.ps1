#!/usr/bin/env pwsh
# Build YARG.Networking and copy DLLs to Unity project
# Usage: .\build-and-copy-to-unity.ps1

Write-Host "Building YARG.Networking..." -ForegroundColor Cyan
dotnet build --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful! Copying DLLs to Unity..." -ForegroundColor Green
    
    $sourceDir = ".\src\YARG.Net\bin\Release\netstandard2.1"
    $destDir = "..\Assets\Plugins\YARG.Net"
    
    # List of DLLs to copy (YARG.Net and its dependencies)
    $dllsToCopy = @(
        "YARG.Net.dll",
        "LiteNetLib.dll",
        "Newtonsoft.Json.dll",
        "System.Text.Json.dll"
    )
    
    foreach ($dll in $dllsToCopy) {
        $source = Join-Path $sourceDir $dll
        $dest = Join-Path $destDir $dll
        if (Test-Path $source) {
            Copy-Item $source $dest -Force
            Write-Host "  Copied $dll" -ForegroundColor Gray
        } else {
            Write-Host "  Warning: $dll not found in build output" -ForegroundColor Yellow
        }
    }
    
    Write-Host "DLLs copied successfully to Unity!" -ForegroundColor Green
    Write-Host "Unity will automatically reload the DLLs." -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
