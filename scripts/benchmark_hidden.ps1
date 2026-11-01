param([string]$Executable,[string]$Output,[int]$Count=4000)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Use Windows PowerShell 5.1' }
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Executable))
Add-Type -AssemblyName System.Web.Extensions
$root = Join-Path ([IO.Path]::GetFullPath('artifacts/hidden-performance')) ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$index = New-Object AtlasView.HiddenImageIndex
for ($i=0; $i -lt $Count; $i++) {
    $entry = New-Object AtlasView.HiddenImageEntry
    $entry.Key = $i.ToString('x64'); $entry.SourcePath = Join-Path $root 'archive.bin'
    $entry.Locator = 'resource-' + $i.ToString('D5'); $entry.Name='resource'; $entry.Format='ALL'
    $entry.SourceOffset=$i; $entry.HiddenUtcTicks=[DateTime]::UtcNow.Ticks+$i
    $index.Images.Add($entry.Key,$entry)
}
$serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength=16777216
$jsonPath=Join-Path $root 'hidden.json'
[IO.File]::WriteAllText($jsonPath,$serializer.Serialize($index))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$storeType=$assembly.GetType('AtlasView.HiddenImageStore')
$store=$storeType.GetConstructor($flags,$null,[Type[]]@([string],[bool]),$null).Invoke(@([string]$jsonPath,$true))
$type=$assembly.GetType('AtlasView.HiddenImagesDialog')
$watch=[Diagnostics.Stopwatch]::StartNew()
$dialog=$type.GetConstructor($flags,$null,[Type[]]@($storeType),$null).Invoke(@($store.PSObject.BaseObject))
try {
    $items=$type.GetField('Items',$flags).GetValue($dialog)
    $handle=$items.Handle
    $openMs=$watch.Elapsed.TotalMilliseconds
    $nativeRows=$items.Items.Count
    $watch.Restart()
    for ($i=0; $i -lt [Math]::Min(100,$items.Items.Count); $i++) { $items.Items[$i].Checked=$true }
    $checkMs=$watch.Elapsed.TotalMilliseconds
    $search=$type.GetField('search',$flags).GetValue($dialog)
    $watch.Restart(); $search.Text='resource-03999'
    $type.GetMethod('RefreshItems',$flags).Invoke($dialog,$null)
    $searchMs=$watch.Elapsed.TotalMilliseconds
    $report=[pscustomobject]@{count=$Count;nativeRows=$nativeRows;openMs=[Math]::Round($openMs,2);check100Ms=[Math]::Round($checkMs,2);searchMs=[Math]::Round($searchMs,2);matchedRows=$items.Items.Count}
    $report | ConvertTo-Json | Set-Content -LiteralPath $Output -Encoding UTF8
    $report | ConvertTo-Json
} finally { $dialog.Dispose() }
