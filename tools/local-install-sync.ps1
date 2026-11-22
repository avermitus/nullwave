[CmdletBinding()]
param([ValidateSet('Start','Run','Status','Stop')][string]$Action = 'Status')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$destination = Join-Path $env:LOCALAPPDATA 'Programs\NULLWAVE'
$stateDir = Join-Path $env:LOCALAPPDATA 'NULLWAVE-LocalSync'
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$statusFile = Join-Path $stateDir 'status.json'
$stopFile = Join-Path $stateDir 'stop.request'
$logFile = Join-Path $stateDir 'sync.log'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'NULLWAVE Local Install Sync.lnk'
$mutex = New-Object Threading.Mutex($false, 'Local\NULLWAVE-LocalInstallSync')
function Own-Lock {
    try { return $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { return $true }
}
if ($Action -eq 'Status') {
    $free = Own-Lock
    if ($free) { $mutex.ReleaseMutex() }
    Write-Output "Watcher running: $(-not $free)"
    if (Test-Path $statusFile) { Get-Content -LiteralPath $statusFile }
    exit
}
if ($Action -eq 'Stop') {
    Remove-Item -LiteralPath $startupLink -Force -ErrorAction SilentlyContinue
    New-Item -ItemType File -Path $stopFile -Force | Out-Null
    Write-Output 'Stop requested. The watcher will exit after any active install finishes.'
    exit
}
if ($Action -eq 'Start') {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($startupLink)
    $link.TargetPath = (Get-Command powershell.exe).Source
    $link.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $PSCommandPath + '" -Action Start'
    $link.WorkingDirectory = $repo
    $link.WindowStyle = 7
    $link.Save()
    if (-not (Own-Lock)) { Write-Output 'Existing watcher retained.'; exit }
    $mutex.ReleaseMutex()
    Remove-Item -LiteralPath $stopFile -Force -ErrorAction SilentlyContinue
    $p = Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Action Run') -WorkingDirectory $repo -WindowStyle Hidden -PassThru
    Write-Output "Started hidden watcher PID $($p.Id). Status/logs: $stateDir"
    exit
}
if (-not (Own-Lock)) { exit }
$status = [ordered]@{ pid=$PID; repo=$repo; destination=$destination; state='starting'; heartbeatUtc=''; lastSuccessUtc=$null; installedFingerprint=$null; error=$null; pollSeconds=3; debounceSeconds=6; retrySeconds=30 }
function Report([string]$state, [string]$message = '') {
    $status.state = $state
    $status.heartbeatUtc = [DateTime]::UtcNow.ToString('o')
    $status.error = $message
    $temp = $statusFile + '.tmp'
    $status | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $statusFile -Force
}
function Log([string]$message) { Add-Content -LiteralPath $logFile -Encoding UTF8 -Value "$([DateTime]::Now.ToString('s')) $message" }
function Fingerprint {
    $files = @(
        foreach ($sub in @('src','Assets')) {
            Get-ChildItem -LiteralPath (Join-Path $repo $sub) -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }
        }
        Get-ChildItem -LiteralPath $repo -File | Where-Object { $_.Extension -in @('.props','.targets','.sln','.slnx') -or $_.Name -in @('global.json','NuGet.Config','nuget.config') }
        Get-Item -LiteralPath (Join-Path $repo 'tools\install.ps1')
    )
    $rows = @($files | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($repo.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n")))).Replace('-','') } finally { $sha.Dispose() }
}
try {
    Log "Watcher started PID=$PID source=$repo destination=$destination"
    $observed = ''; $installed = ''; $changed = [DateTime]::MinValue; $retry = [DateTime]::MinValue
    while (-not (Test-Path -LiteralPath $stopFile)) {
        try {
            $current = Fingerprint
            if ($current -ne $observed) { $observed=$current; $changed=[DateTime]::UtcNow; $retry=[DateTime]::MinValue; Log 'Saved inputs changed; debouncing.' }
            if ($current -eq $installed) { Report 'watching' }
            elseif (([DateTime]::UtcNow - $changed).TotalSeconds -lt 6) { Report 'debouncing' }
            elseif ([DateTime]::UtcNow -lt $retry) { Report 'retry-wait' $status.error }
            elseif (Get-Process -Name Nullwave -ErrorAction SilentlyContinue) { Report 'waiting-for-game-exit' }
            else {
                Report 'building'
                Log "Building fingerprint $current"
                $sdk = [IO.Path]::GetFullPath((Join-Path $repo '..\..\.dotnet\dotnet.exe'))
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\install.ps1') -InstallDir $destination -Dotnet $sdk 2>&1 | Out-File -LiteralPath $logFile -Encoding UTF8 -Append
                if ($LASTEXITCODE -ne 0) { throw "Installer failed ($LASTEXITCODE); see $logFile" }
                $after = Fingerprint
                if ($after -eq $current) {
                    $installed=$current; $status.installedFingerprint=$current; $status.lastSuccessUtc=[DateTime]::UtcNow.ToString('o')
                    Report 'watching'; Log 'Install verified; watching for saved changes.'
                } else { Report 'debouncing'; Log 'Inputs changed during build; scheduling another build.' }
            }
        } catch {
            Log "Retrying in 30s: $_"
            $retry=[DateTime]::UtcNow.AddSeconds(30)
            Report 'retry-wait' ([string]$_)
        }
        Start-Sleep -Seconds 3
    }
} finally {
    Report 'stopped'; Log 'Watcher stopped.'
    $mutex.ReleaseMutex(); $mutex.Dispose()
}
