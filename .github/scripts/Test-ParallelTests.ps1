$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $temporaryRoot ('parallel-tests-' + [Guid]::NewGuid().ToString('N'))
$savedSummary = $env:GITHUB_STEP_SUMMARY
$env:GITHUB_STEP_SUMMARY = $null
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $cases = @('valid', 'truncated-names', 'missing', 'duplicate', 'unexpected', 'failed', 'empty-group', 'skipped-group', 'missing-trx', 'empty-discovery')
    foreach ($case in $cases) {
        $folder = Join-Path $root $case
        New-Item -ItemType Directory -Path $folder | Out-Null
        $expected = @('Core.Pass', 'Core.Skip', 'Ui.Pass', 'Update.Pass')
        $groups = [ordered]@{
            core = @(@{Name='Core.Pass'; Outcome='Passed'}, @{Name='Core.Skip'; Outcome='NotExecuted'})
            ui = @(@{Name='Ui.Pass'; Outcome='Passed'})
            update = @(@{Name='Update.Pass'; Outcome='Passed'})
        }
        switch ($case) {
            'truncated-names' { $expected += 'Core.Pass'; $groups.core += @{Name='Core.Pass'; Outcome='Passed'} }
            'missing' { $expected += 'Missing.Pass' }
            'duplicate' { $groups.ui += @{Name='Core.Pass'; Outcome='Passed'} }
            'unexpected' { $groups.ui += @{Name='Unexpected.Pass'; Outcome='Passed'} }
            'failed' { $groups.ui[0].Outcome = 'Failed' }
            'empty-group' { $groups.ui = @() }
            'skipped-group' { $groups.ui[0].Outcome = 'NotExecuted' }
            'empty-discovery' { $expected = @() }
        }
        [IO.File]::WriteAllLines((Join-Path $folder 'expected-tests.txt'), [string[]]$expected)
        foreach ($group in $groups.Keys) {
            $groupFolder = Join-Path $folder $group
            New-Item -ItemType Directory -Path $groupFolder | Out-Null
            if ($case -eq 'missing-trx' -and $group -eq 'update') { continue }
            $index = 0
            $results = $groups[$group] | ForEach-Object {
                '<UnitTestResult testId="{0}:{1}" testName="{2}" outcome="{3}" duration="00:00:00.2500000" />' -f $group, $index++, $_.Name, $_.Outcome
            }
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times start="2026-11-16T00:00:00Z" finish="2026-11-16T00:00:01Z"/><Results>{0}</Results></TestRun>' -f ($results -join '') |
                Set-Content -LiteralPath (Join-Path $groupFolder 'tests.trx')
        }
        $rejected = $false
        try { & "$PSScriptRoot/Write-TestTimingSummary.ps1" -ResultsDirectory $folder *> $null }
        catch { $rejected = $true; if ($case -in @('valid', 'truncated-names')) { throw } }
        if ($rejected -ne ($case -notin @('valid', 'truncated-names'))) { throw "Incorrect parallel test decision: $case" }
        if ($case -in @('valid', 'truncated-names')) {
            $report = Get-Content -LiteralPath (Join-Path $folder 'timing-summary.json') -Raw | ConvertFrom-Json
            $extra = [int]($case -eq 'truncated-names')
            if ($report.Executed -ne (4 + $extra) -or $report.Passed -ne (3 + $extra) -or $report.Skipped -ne 1 -or $report.Failed -ne 0) {
                throw 'Incorrect test counts in timing summary.'
            }
        }
        Write-Output "PASS $case"
    }
    Write-Output "$($cases.Count) parallel test coverage cases passed."
}
finally {
    $env:GITHUB_STEP_SUMMARY = $savedSummary
    $resolved = (Resolve-Path -LiteralPath $root).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot) { throw 'Test cleanup escaped its temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
