param([switch]$Test,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /codepage:65001 "/out:$out\PS3AtlasViewer.exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
& $compiler /nologo /optimize+ /platform:x64 /target:exe /codepage:65001 "/out:$out\AtlasCheck.exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
Get-FileHash -LiteralPath "$out\PS3AtlasViewer.exe","$out\AtlasCheck.exe" -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json | Set-Content -LiteralPath "$out\sha256.json" -Encoding UTF8
if ($Test) { & "$out\AtlasCheck.exe" --selftest (Join-Path $root 'artifacts\tests'); if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' } }
Write-Host "Built: $out\PS3AtlasViewer.exe"
