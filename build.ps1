$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$targetDir = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'app.ico'))) { & (Join-Path $PSScriptRoot 'create-icon.ps1') }
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object { $_.Name -notin @('Tests.cs', 'StartupLauncher.cs', 'StartupLauncherAssemblyInfo.cs') } | ForEach-Object { $_.FullName }
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output '/main:PushToTalk.Program' "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\app.ico" "/out:$targetDir\Push To Talk.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output '/main:PushToTalk.StartupLauncher' "/win32manifest:$PSScriptRoot\startup.manifest" "/win32icon:$PSScriptRoot\app.ico" "/out:$targetDir\Push To Talk Startup.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "$PSScriptRoot\StartupLauncher.cs" "$PSScriptRoot\StartupLauncherAssemblyInfo.cs" "$PSScriptRoot\Localization.cs"
if ($LASTEXITCODE -ne 0) { throw 'Startup launcher build failed.' }
Write-Output (Join-Path $targetDir 'Push To Talk.exe')
Write-Output (Join-Path $targetDir 'Push To Talk Startup.exe')
