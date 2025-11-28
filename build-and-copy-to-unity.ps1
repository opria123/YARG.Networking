#!/usr/bin/env pwsh
# Build YARG.Networking and copy DLL to Unity project
# Usage: .\build-and-copy-to-unity.ps1

Write-Host "Building YARG.Networking..." -ForegroundColor Cyan
dotnet build --configuration Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful! Copying DLL to Unity..." -ForegroundColor Green
    
    $dllSource = ".\src\YARG.Net\bin\Release\netstandard2.1\YARG.Net.dll"
    $dllDest = "..\YARG\Assets\Plugins\YARG.Net\YARG.Net.dll"
    
    Copy-Item $dllSource $dllDest -Force
    
    Write-Host "DLL copied successfully to Unity!" -ForegroundColor Green
    Write-Host "Unity will automatically reload the DLL." -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
