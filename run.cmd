@echo off
cd /d "%~dp0"
".dotnet\dotnet.exe" run --project "src\Nullwave\Nullwave.csproj" -c Release -- %*
