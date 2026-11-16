param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expected = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
$expectedCount = 0
foreach ($name in Get-Content -LiteralPath (Join-Path $ResultsDirectory 'expected-tests.txt')) {
    if (-not $expected.ContainsKey($name)) { $expected[$name] = 0 }
    $expected[$name]++
    $expectedCount++
}
if ($expectedCount -eq 0) { throw 'No public tests were discovered.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$tests = @()
$groups = foreach ($group in @('core', 'ui', 'update')) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $ResultsDirectory "$group/tests.trx") -Raw
    $results = @($trx.TestRun.Results.UnitTestResult)
    $passed = 0
    $skipped = 0
    $failed = 0
    foreach ($result in $results) {
        $name = [string]$result.testName
        if (-not $expected.ContainsKey($name)) { throw "Unexpected test: $name" }
        if (-not $seen.Add([string]$result.testId)) { throw "Test ran in more than one group: $name" }
        # xUnit truncates long theory arguments, so different cases can share a display name.
        $expected[$name]--
        if ($expected[$name] -lt 0) { throw "More results than discovered cases: $name" }
        switch ([string]$result.outcome) {
            'Passed' { $passed++ }
            'NotExecuted' { $skipped++ }
            default { $failed++ }
        }
        $tests += [pscustomobject]@{
            Group = $group; Name = $name; Outcome = [string]$result.outcome
            Seconds = [TimeSpan]::Parse([string]$result.duration, [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
        }
    }
    [pscustomobject]@{
        Group = $group; Passed = $passed; Skipped = $skipped; Failed = $failed
        Seconds = ([DateTimeOffset]::Parse($trx.TestRun.Times.finish) - [DateTimeOffset]::Parse($trx.TestRun.Times.start)).TotalSeconds
    }
}
$missing = @($expected.Keys | Where-Object { $expected[$_] -gt 0 })
$report = [ordered]@{
    Discovered = $expectedCount; Executed = $seen.Count; Missing = $missing
    Passed = ($groups.Passed | Measure-Object -Sum).Sum
    Skipped = ($groups.Skipped | Measure-Object -Sum).Sum
    Failed = ($groups.Failed | Measure-Object -Sum).Sum
    Groups = @($groups); Slowest = @($tests | Sort-Object Seconds -Descending | Select-Object -First 30)
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'timing-summary.json') -Encoding utf8
$groups | Format-Table | Out-Host
Write-Output "Public tests: $($seen.Count)/$expectedCount; passed=$($report.Passed), skipped=$($report.Skipped), failed=$($report.Failed)."
if ($env:GITHUB_STEP_SUMMARY) {
    @('| Group | Seconds | Passed | Skipped | Failed |', '|---|---:|---:|---:|---:|') +
        @($groups | ForEach-Object { "| $($_.Group) | $([Math]::Round($_.Seconds, 2)) | $($_.Passed) | $($_.Skipped) | $($_.Failed) |" }) |
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}
if ($missing.Count -gt 0) { throw "Missing public tests: $($missing -join ', ')" }
if ($report.Failed -gt 0 -or @($groups | Where-Object Passed -eq 0).Count -gt 0) { throw 'A test group failed or had no passing tests.' }
