$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$testBuild = Join-Path $testRoot 'bin\Direct'
$sdkLines = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK is required to run the simulation checks.' }
$sdkLine = $sdkLines | Select-Object -Last 1
if ($sdkLine -notmatch '^([^ ]+) \[(.+)\]$') { throw 'Unable to locate the .NET SDK.' }
$sdkVersion = $Matches[1]
$sdkFolder = $Matches[2]
$dotnetRoot = Split-Path $sdkFolder -Parent
$sdkMajor = [int]($sdkVersion.Split('.')[0])
$referenceRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
$referencePack = Get-ChildItem -LiteralPath $referenceRoot -Directory |
    Where-Object { $_.Name -match ('^' + $sdkMajor + '\.') } |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1
if ($null -eq $referencePack) { throw 'Unable to locate the SDK reference assemblies.' }
$targetFramework = 'net' + $sdkMajor + '.0'
$referenceAssemblies = Get-ChildItem -LiteralPath (Join-Path $referencePack.FullName ('ref\' + $targetFramework)) -Filter '*.dll'
New-Item -ItemType Directory -Path $testBuild -Force | Out-Null
$testAssembly = Join-Path $testBuild 'SimulationTests.dll'
$compilerArguments = @('-nologo', '-target:exe', '-langversion:9', ('-out:"' + $testAssembly + '"'))
$compilerArguments += $referenceAssemblies | ForEach-Object { '-r:"' + $_.FullName + '"' }
$sources = @((Join-Path $testRoot 'Program.cs'))
$sources += Get-ChildItem -LiteralPath (Join-Path $testRoot '..\Simulation') -Filter '*.cs' | ForEach-Object { $_.FullName }
$compilerArguments += $sources | ForEach-Object { '"' + $_ + '"' }
$responseFile = Join-Path $testBuild 'compile.rsp'
$compilerArguments | Set-Content -LiteralPath $responseFile
& dotnet (Join-Path $sdkFolder ($sdkVersion + '\Roslyn\bincore\csc.dll')) ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$runtimeConfiguration = @{ runtimeOptions = @{ tfm = $targetFramework; framework = @{ name = 'Microsoft.NETCore.App'; version = $referencePack.Name } } }
$runtimeConfiguration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testBuild 'SimulationTests.runtimeconfig.json')
& dotnet $testAssembly
exit $LASTEXITCODE

