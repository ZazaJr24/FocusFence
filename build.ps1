param(
    [switch]$Test,
    [switch]$CoreTest,
    [string]$OutputDirectory = 'dist-net10'
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$projectRoot = $PSScriptRoot
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$localSdk = Join-Path $projectRoot '.tools\dotnet10\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }

& $dotnet publish (Join-Path $projectRoot 'FocusFence\FocusFence.csproj') -c Release -r win-x64 --self-contained true -o $outputRoot --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET 10 publish failed.' }
foreach ($file in @('README.md', 'README.de.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $outputRoot $file) -Force
}
Write-Host ('Published: ' + (Join-Path $outputRoot 'FocusFence.exe'))

function Invoke-AppCheck([string]$Argument, [string]$ReportName) {
    $report = Join-Path $projectRoot $ReportName
    $process = Start-Process -FilePath (Join-Path $outputRoot 'FocusFence.exe') -ArgumentList @($Argument, ('"' + $report + '"')) -PassThru -WindowStyle Hidden -Wait
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
    if ($process.ExitCode -ne 0) { throw ('Check failed: ' + $Argument + '. See ' + $report) }
}
if ($CoreTest) { Invoke-AppCheck '--core-test' 'core-test-results.txt' }
if ($Test) {
    Invoke-AppCheck '--self-test' 'test-results.txt'
    Invoke-AppCheck '--smoke' 'smoke-results.txt'
}
