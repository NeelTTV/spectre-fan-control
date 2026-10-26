# Builds SpectreControl.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK to install: .\build.ps1  ->  dist\SpectreControl.exe
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

if (-not (Test-Path "$root\src\app.ico")) { & "$root\tools\make-icon.ps1" }
New-Item -ItemType Directory -Force "$root\dist" | Out-Null

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:"$root\dist\SpectreControl.exe" `
    /win32icon:"$root\src\app.ico" /win32manifest:"$root\src\app.manifest" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    "$root\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Get-Item "$root\dist\SpectreControl.exe" | Select-Object Name, Length
