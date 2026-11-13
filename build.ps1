param([switch]$Test, [switch]$CoreTest)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'FocusFence'
$outputRoot = Join-Path $projectRoot 'dist'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 mit 64-Bit-Compiler wird benötigt.' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll','WPF\UIAutomationProvider.dll')
$compileArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/warn:4','/utf8output',('/out:' + (Join-Path $outputRoot 'FocusFence.exe')),('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),('/resource:' + (Join-Path $sourceRoot 'MainWindow.xaml') + ',FocusFence.MainWindow.xaml'))
if (Test-Path -LiteralPath (Join-Path $sourceRoot 'FocusFence.ico')) { $compileArgs += '/win32icon:' + (Join-Path $sourceRoot 'FocusFence.ico') }
foreach ($ref in $references) { $compileArgs += '/reference:' + (Join-Path $frameworkRoot $ref) }
$compileArgs += @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'App.config') -Destination (Join-Path $outputRoot 'FocusFence.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.de.md') -Destination (Join-Path $outputRoot 'LIESMICH.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $outputRoot 'LICENSE') -Force
Write-Host ('Erstellt: ' + (Join-Path $outputRoot 'FocusFence.exe'))
if ($CoreTest) {
    $coreReport = Join-Path $projectRoot 'core-test-results.txt'
    $core = Start-Process -FilePath (Join-Path $outputRoot 'FocusFence.exe') -ArgumentList @('--core-test', ('"' + $coreReport + '"')) -PassThru -WindowStyle Hidden -Wait
    Get-Content -LiteralPath $coreReport
    if ($core.ExitCode -ne 0) { throw 'Core tests failed.' }
}
if ($Test) {
    $report = Join-Path $projectRoot 'test-results.txt'
    $process = Start-Process -FilePath (Join-Path $outputRoot 'FocusFence.exe') -ArgumentList @('--self-test', ('"' + $report + '"')) -PassThru -WindowStyle Hidden -Wait
    Get-Content -LiteralPath $report
    if ($process.ExitCode -ne 0) { throw 'Tests fehlgeschlagen.' }
    $smokeReport = Join-Path $projectRoot 'smoke-results.txt'
    $smoke = Start-Process -FilePath (Join-Path $outputRoot 'FocusFence.exe') -ArgumentList @('--smoke', ('"' + $smokeReport + '"')) -PassThru -WindowStyle Hidden -Wait
    Get-Content -LiteralPath $smokeReport
    if ($smoke.ExitCode -ne 0) { throw 'Startprüfung fehlgeschlagen. Ist FocusFence bereits geöffnet oder Strg+Alt+F12 belegt?' }
}
