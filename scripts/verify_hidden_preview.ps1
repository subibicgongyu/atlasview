param([string]$Executable,[string]$HiddenIndex,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Use Windows PowerShell 5.1' }
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Executable))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$storeType=$assembly.GetType('AtlasView.HiddenImageStore')
$store=$storeType.GetConstructor($flags,$null,[Type[]]@([string],[bool]),$null).Invoke(@([string][IO.Path]::GetFullPath($HiddenIndex),$true))
$entries=$storeType.GetMethod('Entries',$flags).Invoke($store,$null)
$resolverType=$assembly.GetType('AtlasView.HiddenPreviewResolver')
$resolver=$resolverType.GetConstructors($flags)[0].Invoke([object[]]@($null))
$loader=[Activator]::CreateInstance($assembly.GetType('AtlasView.PreviewLoader'),$true)
$previewType=$assembly.GetType('AtlasView.ResourcePreview')
$load=$previewType.GetMethod('Load',[Reflection.BindingFlags]'Static,NonPublic')
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$reports=@()
foreach($entry in ($entries | Select-Object -First 3)) {
    $group=$resolverType.GetMethod('Resolve',$flags).Invoke($resolver,@($entry.PSObject.BaseObject,[Threading.CancellationToken]::None))
    $preview=$load.Invoke($null,@($group.PSObject.BaseObject,$loader.PSObject.BaseObject,[Threading.CancellationToken]::None,[Action[string]]{}))
    try {
        $image=$previewType.GetField('Image',$flags).GetValue($preview)
        if ($null -eq $image) { throw 'No preview bitmap' }
        $path=Join-Path $OutputDirectory ('preview-'+$reports.Count+'.png')
        $image.Save([IO.Path]::GetFullPath($path),[Drawing.Imaging.ImageFormat]::Png)
        $errors=$previewType.GetField('Errors',$flags).GetValue($preview)
        if ($errors.Count -gt 0) { throw ($errors -join '; ') }
        $reports += [pscustomobject]@{locator=$entry.Locator;textures=$group.Textures.Count;width=$image.Width;height=$image.Height;png=$path}
    } finally { $preview.Dispose() }
}
$reports | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.json') -Encoding UTF8
$reports | ConvertTo-Json
