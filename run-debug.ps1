# Fast Debug launch (no debugger attach — much closer to real startup)
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "src\Ardel.Launcher\Ardel.Launcher.csproj"
$exe = Join-Path $PSScriptRoot "src\Ardel.Launcher\bin\x64\Debug\net8.0-windows10.0.19041.0\Ardel.Launcher.exe"

# Avoid file-lock build failures from a previous instance / XAML compiler.
Get-Process -Name "Ardel.Launcher" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

Write-Host "Building Debug|x64..."
dotnet build $proj -c Debug -p:Platform=x64 | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit $LASTEXITCODE). Close anything locking the output and retry."
}

if (!(Test-Path $exe)) { throw "Missing: $exe" }

Write-Host "Starting (no debugger): $exe"
Start-Process $exe

$log = Join-Path $env:LOCALAPPDATA "Ardel\startup.log"
Write-Host "Startup timings will be written to:`n  $log"
