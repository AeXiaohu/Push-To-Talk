$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$targetDir = Join-Path $PSScriptRoot 'bin'
$sourceDir = Join-Path $PSScriptRoot 'src'
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$testSourceNames = @('Hotkeys.cs', 'ControlState.cs', 'Localization.cs', 'Settings.cs', 'OverlayLayout.cs', 'SoundFeedback.cs', 'StartupRegistration.cs', 'SelfElevation.cs', 'Audio.cs')
$testSources = @($testSourceNames | ForEach-Object { Join-Path $sourceDir $_ })
$testSources += Join-Path $PSScriptRoot 'tests\Tests.cs'
& $compilerPath /nologo /target:exe /platform:anycpu /optimize+ /utf8output '/main:PushToTalk.Tests' "/out:$targetDir\Tests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll $testSources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $targetDir 'Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
