$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$targetDir = Join-Path $PSScriptRoot 'bin'
$sourceDir = Join-Path $PSScriptRoot 'src'
$assetsDir = Join-Path $PSScriptRoot 'assets'
$manifestPath = Join-Path $sourceDir 'app.manifest'
$iconPath = Join-Path $assetsDir 'app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $PSScriptRoot 'tools\create-icon.ps1') }
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$sources = Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File | ForEach-Object { $_.FullName }
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output '/main:PushToTalk.Program' "/win32manifest:$manifestPath" "/win32icon:$iconPath" "/out:$targetDir\Push To Talk.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$legacyLauncherPath = Join-Path $targetDir 'Push To Talk Startup.exe'
if (Test-Path -LiteralPath $legacyLauncherPath -PathType Leaf) { Remove-Item -LiteralPath $legacyLauncherPath -Force }
Write-Output (Join-Path $targetDir 'Push To Talk.exe')
