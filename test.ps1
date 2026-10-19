$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$targetDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
& $compilerPath /nologo /target:exe /platform:anycpu /optimize+ /utf8output '/main:PushToTalk.Tests' "/out:$targetDir\Tests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "$PSScriptRoot\Hotkeys.cs" "$PSScriptRoot\ControlState.cs" "$PSScriptRoot\Localization.cs" "$PSScriptRoot\Settings.cs" "$PSScriptRoot\OverlayLayout.cs" "$PSScriptRoot\SoundFeedback.cs" "$PSScriptRoot\StartupRegistration.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $targetDir 'Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
