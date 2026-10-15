<#
.SYNOPSIS
    Runs the Home Assistant API tests in Unity batch mode and prints a summary.

.DESCRIPTION
    Unity must be closed (a project can only be open once). Results are written as NUnit XML
    to TestResults/playmode-results.xml and the Unity log to TestResults/test-run.log.
    Exit code 0 = all tests passed (live tests skip themselves when the lab LAN can't be reached).

.EXAMPLE
    pwsh Tools\Run-Tests.ps1                       # everything
.EXAMPLE
    pwsh Tools\Run-Tests.ps1 -Category LiveHomeAssistant   # only the read-only checks against the real Home Assistant
#>
param(
    [string]$Category = "",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$out = Join-Path $project "TestResults"
New-Item -ItemType Directory -Force $out | Out-Null
$results = Join-Path $out "playmode-results.xml"
$log = Join-Path $out "test-run.log"
Remove-Item $results -ErrorAction SilentlyContinue

$args = @('-batchmode', '-projectPath', "`"$project`"", '-runTests', '-testPlatform', 'PlayMode',
          '-testResults', "`"$results`"", '-logFile', "`"$log`"")
if ($Category) { $args += @('-testCategory', $Category) }

Write-Host "Running tests (this compiles the project first; a few minutes)..."
$p = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -Wait

if (-not (Test-Path $results)) {
    Write-Host "No results - Unity is probably still open, or the scripts don't compile. See $log" -ForegroundColor Red
    exit 3
}

[xml]$x = Get-Content $results
$run = $x.'test-run'
Write-Host ("{0} tests: {1} passed, {2} failed, {3} skipped" -f $run.total, $run.passed, $run.failed, $run.skipped)
foreach ($case in $x.SelectNodes("//test-case[@result='Failed']")) {
    Write-Host ("FAILED  " + $case.name) -ForegroundColor Red
    Write-Host ("        " + (($case.failure.message.'#cdata-section' -split "`n")[0]))
}
$skipped = $x.SelectNodes("//test-case[@result='Skipped']")
if ($skipped.Count -gt 0) {
    Write-Host ("Skipped: " + (($skipped[0].reason.message.'#cdata-section') -split "`n")[0]) -ForegroundColor Yellow
}
exit $p.ExitCode
