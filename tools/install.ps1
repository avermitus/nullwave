<#
.SYNOPSIS
    Builds NULLWAVE and installs it for the current user, with Start Menu and Desktop shortcuts.

.DESCRIPTION
    Publishes a self-contained Windows build (the .NET runtime is bundled, no console window)
    into %LOCALAPPDATA%\Programs\NULLWAVE and creates shortcuts. Run it again to update.
    Needs the .NET 8 SDK only to build: either in .dotnet\ at the repo root
    (tools\dotnet-install.ps1) or on PATH. No admin rights needed.

    Saves, settings and song records live in %LOCALAPPDATA%\Nullwave and are kept on
    update and uninstall. Highlight clips go to Videos\NULLWAVE.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1 -NoDesktopShortcut
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\NULLWAVE'),
    [string]$Dotnet,
    [switch]$NoDesktopShortcut,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exeName = 'Nullwave.exe'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'NULLWAVE.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NULLWAVE.lnk'

function Stop-IfRunning {
    $running = Get-Process -Name 'Nullwave' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        throw "NULLWAVE is running from $InstallDir. Close the game and run this again."
    }
}

function New-Shortcut([string]$Path, [string]$Target) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.WorkingDirectory = Split-Path -Parent $Target
    $link.IconLocation = "$Target,0"
    $link.Description = 'NULLWAVE - a twin-stick bullet hell built from your music'
    $link.Save()
}

if ($Uninstall) {
    Stop-IfRunning
    foreach ($p in @($startMenuLink, $desktopLink)) {
        if (Test-Path $p) { Remove-Item $p -Force }
    }
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    Write-Host "NULLWAVE removed from $InstallDir. Saves in $env:LOCALAPPDATA\Nullwave were kept."
    return
}

# find a .NET 8 SDK: -Dotnet, then the repo's private copy, then PATH
if (-not $Dotnet) {
    $local = Join-Path $repo '.dotnet\dotnet.exe'
    if (Test-Path $local) { $Dotnet = $local }
    else {
        $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($cmd) { $Dotnet = $cmd.Source }
    }
}
if (-not $Dotnet -or -not (Test-Path $Dotnet)) {
    throw "No .NET SDK found. Install one with: powershell -ExecutionPolicy Bypass -File tools\dotnet-install.ps1 -Channel 8.0 -InstallDir .dotnet"
}
$sdks = & $Dotnet --list-sdks 2>$null
if (-not ($sdks | Where-Object { $_ -match '^(8|9|1\d)\.' })) {
    throw "$Dotnet has no .NET 8+ SDK. Install one with tools\dotnet-install.ps1 -Channel 8.0 -InstallDir .dotnet"
}

Stop-IfRunning

$project = Join-Path $repo 'src\Nullwave\Nullwave.csproj'
$staging = Join-Path $repo 'build\install'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host 'Building NULLWAVE (self-contained, win-x64)...'
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:OutputType=WinExe -o $staging --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed (dotnet publish exit code $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $staging $exeName))) { throw "Build produced no $exeName." }

# replace the old install
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $InstallDir) | Out-Null
Move-Item $staging $InstallDir

$exe = Join-Path $InstallDir $exeName
New-Shortcut $startMenuLink $exe
if ($NoDesktopShortcut) {
    if (Test-Path $desktopLink) { Remove-Item $desktopLink -Force }
} else {
    New-Shortcut $desktopLink $exe
}

Write-Host ''
Write-Host "Installed NULLWAVE to $InstallDir"
Write-Host 'Start it from the Start Menu or the Desktop shortcut (NULLWAVE).'
