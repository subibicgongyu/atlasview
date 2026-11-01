param([string]$ReportDirectory = (Join-Path $PSScriptRoot '..\artifacts\loading-update'), [string]$Executable = (Join-Path $PSScriptRoot '..\dist\AtlasCheck.exe'))
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Executable))
$report = Get-Content -LiteralPath (Join-Path $ReportDirectory 'scan-report.json') -Raw | ConvertFrom-Json
$rows = @{}
Import-Csv -LiteralPath (Join-Path $ReportDirectory 'index.csv') | ForEach-Object { $rows[$_.entry] = $_ }
$loaderType = $assembly.GetType('AtlasView.PreviewLoader')
$loader = [Activator]::CreateInstance($loaderType, $true)
$load = $loaderType.GetMethod('Load')
$checks = @()
foreach ($sample in $report.decoded) {
    $row = $rows[$sample.locator]
    $source = New-Object AtlasView.Source
    $source.Path = $row.source
    $source.Offset = [long]$row.source_offset
    $source.Length = [int]$row.stored_length
    $source.Lzp = [bool]::Parse($row.lzp2)
    $split = $row.entry.LastIndexOf(' / ')
    $source.Label = $row.entry.Substring(0, $split)
    if ($row.container -eq 'Image') {
        $texture = New-Object AtlasView.Texture
        $texture.Source = $source
        $texture.StandardImage = $true
        $texture.Width = [int]$row.width
        $texture.Height = [int]$row.height
        $texture.Name = $row.entry.Substring($split + 3)
        $texture.Format = $row.format
        $texture.Container = $row.container
    } else {
        $data = $source.Read([Threading.CancellationToken]::None)
        $texture = @([AtlasView.Formats]::Parse($source, $data) | Where-Object { $_.Locator -eq $sample.locator })[0]
        if ($null -eq $texture) { throw "Texture missing: $($sample.locator)" }
    }
    $measurements = @()
    $hashes = @()
    for ($i=0; $i -lt 2; $i++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $bitmap = $load.Invoke($loader, @($texture.PSObject.BaseObject, [Threading.CancellationToken]::None, [Action[string]]{}))
        $watch.Stop()
        try { $hashes += [AtlasView.Bin]::Hash([AtlasView.Pixels]::Bgra($bitmap)) } finally { $bitmap.Dispose() }
        $measurements += [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
    }
    $equal = $hashes[0] -eq $sample.bgraSha256 -and $hashes[1] -eq $sample.bgraSha256
    $checks += [pscustomobject]@{ locator=$sample.locator; equal=$equal; firstMs=$measurements[0]; repeatMs=$measurements[1] }
}
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$result = [pscustomobject]@{
    passed=@($checks | Where-Object equal).Count; total=$checks.Count
    imageCacheHits=$loaderType.GetField('ImageHits',$flags).GetValue($loader)
    sourceReads=$loaderType.GetField('Reads',$flags).GetValue($loader)
    sourceDecodes=$loaderType.GetField('Decodes',$flags).GetValue($loader)
    decompressedSourceHits=$loaderType.GetField('SourceHits',$flags).GetValue($loader)
    note='Application cache timings only; OS file cache may already be warm. No modal/render time included.'
    checks=$checks
}
$name = 'preview-cache-verification.json'
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportDirectory $name) -Encoding UTF8
$result | Select-Object passed,total,imageCacheHits,decompressedSourceHits,sourceReads,sourceDecodes
if ($result.passed -ne $result.total) { throw 'Preview cache pixel mismatch.' }
