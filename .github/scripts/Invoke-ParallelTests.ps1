param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$clock = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path (Split-Path $PSScriptRoot)
$project = Join-Path $repo 'src/ASPAssistant.Tests/ASPAssistant.Tests.csproj'
$dotnet = (Get-Command dotnet -CommandType Application).Source
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
& $dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$assembly = & $dotnet msbuild $project -p:Configuration=Release -getProperty:TargetPath -nologo
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve test assembly.' }
$buildSeconds = $clock.Elapsed.TotalSeconds
$discovery = & $dotnet vstest $assembly /ListTests '/TestCaseFilter:Category!=LocalScreenshot'
if ($LASTEXITCODE -ne 0) { throw 'Public test discovery failed.' }
$discovery | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'discovery.log') -Encoding utf8
$names = @($discovery | Where-Object { $_ -match '^\s+ASPAssistant\.Tests\.' } | ForEach-Object { $_.Trim() })
if ($names.Count -eq 0) { throw 'No public tests were discovered.' }
$names | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'expected-tests.txt') -Encoding utf8

# Keep WPF, native OCR and update-script state in separate processes, with one build.
$filters = [ordered]@{
    core = 'Category!=LocalScreenshot&FullyQualifiedName!~ASPAssistant.Tests.Views.&FullyQualifiedName!~ASPAssistant.Tests.Services.UpdateScriptExecutionTests'
    ui = 'Category!=LocalScreenshot&FullyQualifiedName~ASPAssistant.Tests.Views.'
    update = 'Category!=LocalScreenshot&FullyQualifiedName~ASPAssistant.Tests.Services.UpdateScriptExecutionTests'
}
$processes = @()
try {
    foreach ($group in $filters.Keys) {
        $folder = Join-Path $ResultsDirectory $group
        New-Item -ItemType Directory -Path (Join-Path $folder 'temp') -Force | Out-Null
        $arguments = @('vstest', "`"$assembly`"", "`"/TestCaseFilter:$($filters[$group])`"",
            '"/Logger:trx;LogFileName=tests.trx"', "`"/ResultsDirectory:$folder`"")
        $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log') `
            -Environment @{ TEMP = (Join-Path $folder 'temp'); TMP = (Join-Path $folder 'temp')
                ASPASSISTANT_TEST_RESULTS_DIR = (Join-Path $folder 'ui'); ASPASSISTANT_SETTINGS_DIR = (Join-Path $folder 'settings') }
        $processes += [pscustomobject]@{ Group = $group; Process = $process }
    }
    $failed = @()
    foreach ($entry in $processes) {
        $entry.Process.WaitForExit()
        Get-Content -LiteralPath (Join-Path $ResultsDirectory "$($entry.Group)/stdout.log") | Write-Output
        Get-Content -LiteralPath (Join-Path $ResultsDirectory "$($entry.Group)/stderr.log") | Write-Output
        if ($entry.Process.ExitCode -ne 0) { $failed += $entry.Group }
    }
    [pscustomobject]@{ BuildSeconds = $buildSeconds; TotalSeconds = $clock.Elapsed.TotalSeconds; FailedProcesses = $failed } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'execution-times.json') -Encoding utf8
    & "$PSScriptRoot/Write-TestTimingSummary.ps1" -ResultsDirectory $ResultsDirectory
    if ($failed.Count -gt 0) { throw "Test processes failed: $($failed -join ', ')" }
}
finally {
    foreach ($entry in $processes) { $entry.Process.WaitForExit(); $entry.Process.Dispose() }
}
