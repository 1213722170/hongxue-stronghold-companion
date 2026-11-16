param(
    [Parameter(Mandatory)][ValidateSet('core', 'ui', 'update')][string]$Group,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path (Split-Path $PSScriptRoot)
$project = Join-Path $repo 'src/ASPAssistant.Tests/ASPAssistant.Tests.csproj'
New-Item -ItemType Directory -Path (Join-Path $ResultsDirectory 'temp') -Force | Out-Null
$env:TEMP = $env:TMP = Join-Path $ResultsDirectory 'temp'
$env:ASPASSISTANT_TEST_RESULTS_DIR = Join-Path $ResultsDirectory 'ui'

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$buildSeconds = $clock.Elapsed.TotalSeconds
$discovery = dotnet test $project -c Release --no-build --list-tests --filter 'Category!=LocalScreenshot'
if ($LASTEXITCODE -ne 0) { throw 'Public test discovery failed.' }
$discovery | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'discovery.log') -Encoding utf8
$names = @($discovery | Where-Object { $_ -match '^\s+ASPAssistant\.Tests\.' } | ForEach-Object { $_.Trim() })
if ($names.Count -eq 0) { throw 'No public tests were discovered.' }
$names | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'expected-tests.txt') -Encoding utf8

$filters = @{
    core = 'Category!=LocalScreenshot&FullyQualifiedName!~ASPAssistant.Tests.Views.&FullyQualifiedName!~ASPAssistant.Tests.Services.UpdateScriptExecutionTests'
    ui = 'Category!=LocalScreenshot&FullyQualifiedName~ASPAssistant.Tests.Views.'
    update = 'Category!=LocalScreenshot&FullyQualifiedName~ASPAssistant.Tests.Services.UpdateScriptExecutionTests'
}
dotnet test $project -c Release --no-build --filter $filters[$Group] --logger 'trx;LogFileName=tests.trx' --results-directory $ResultsDirectory *> (Join-Path $ResultsDirectory 'tests.log')
$exitCode = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $ResultsDirectory 'tests.log') | Write-Output
[pscustomobject]@{
    Group = $Group; Sha = (git -C $repo rev-parse HEAD); ExitCode = $exitCode
    BuildSeconds = $buildSeconds; TotalSeconds = $clock.Elapsed.TotalSeconds
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'execution-times.json') -Encoding utf8
if ($exitCode -ne 0) { throw "Test group failed: $Group ($exitCode)" }
