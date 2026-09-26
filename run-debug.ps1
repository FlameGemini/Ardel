# Fast launch (no debugger attach - much closer to real startup)
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$env:VSLANG = '1033'
$proj = Join-Path $PSScriptRoot 'src\Ardel.Launcher\Ardel.Launcher.csproj'
$exe = if ($Configuration -eq 'Release') {
    Join-Path $PSScriptRoot 'src\Ardel.Launcher\bin\x64\Release\net8.0-windows10.0.19041.0\Ardel.Launcher.exe'
} else {
    Join-Path $PSScriptRoot 'src\Ardel.Launcher\bin\x64\Debug\net8.0-windows10.0.19041.0\Ardel.Launcher.exe'
}

Get-Process -Name 'Ardel.Launcher', 'XamlCompiler', 'VBCSCompiler', 'MSBuild' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

Write-Host "Building $Configuration|x64..."
dotnet build $proj -c $Configuration -p:Platform=x64 -nr:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit $LASTEXITCODE). Close anything locking the output and retry."
}

if (!(Test-Path $exe)) { throw "Missing: $exe" }

Write-Host "Starting (no debugger): $exe"
$exeDir = Split-Path $exe

# ----------------------------------------------------------------------------------
# NUCLEAR OPTION: The IDE terminal's handle redirection (stdin/stdout) or deeply
# nested Job Objects are still interfering with WinUI 3's DWM composition.
# We bypass the IDE ENTIRELY by asking Windows Task Scheduler to launch the app.
# This guarantees it runs natively under the Desktop Session, identical to a double-click.
# ----------------------------------------------------------------------------------
Write-Host "Registering ephemeral Scheduled Task for clean launch..."
$taskName = "ArdelLauncher_DebugRun_$(Get-Random)"
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $exeDir
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0

Register-ScheduledTask -TaskName $taskName -Action $action -Settings $settings -Force | Out-Null
Write-Host "Triggering Desktop launch..."
Start-ScheduledTask -TaskName $taskName

# Give it a second to spawn before cleaning up the task
Start-Sleep -Seconds 1
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false | Out-Null
Write-Host "Launch complete. Task cleaned up."
