$ErrorActionPreference = 'Stop'
& dotnet build (Join-Path $PSScriptRoot 'BeetleDesktop.csproj') -c Release -o (Join-Path $PSScriptRoot 'Build') --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ('完成：' + (Join-Path $PSScriptRoot 'Build/BeetleDesktop.exe'))
