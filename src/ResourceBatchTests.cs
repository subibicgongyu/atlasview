using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AtlasView {
internal static class ResourceBatchTests {
    internal static void Run(string output,Action<bool,string> check){
        string root=Path.Combine(output,"batch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"archive.bin");File.WriteAllBytes(path,Enumerable.Range(0,256).Select(i=>(byte)i).ToArray());
        var groups=Enumerable.Range(0,10).Select(i=>new ResourceGroup{Textures=new List<Texture>{new Texture{Source=new Source{Path=path,Offset=i*4,Length=4},Name="texture",Width=1,Height=1,Format="L8",BytesPerPixel=1,DataLength=1}}}).ToList();
        var sibling=groups[0].First.Copy();sibling.DataOffset=1;groups[0].Textures.Add(sibling);
        check(ResourceBatch.Slice(groups,7,8).Count==8 && ResourceBatch.Slice(groups,8,8).SequenceEqual(groups.Skip(8)),"Automatic batches keep ordered resources and final remainder");
        check(ResourceBatch.Slice(groups,4,3).SequenceEqual(groups.Skip(3).Take(3)) && ResourceBatch.Slice(groups,4,1).Single()==groups[4],"Changing batch size preserves ordered membership including single-resource mode");
        var loader=new PreviewLoader();var chosen=groups.Take(8).ToList();Dictionary<string,string> stamps;
        using(var preview=ResourceBatchPreview.Load(chosen,loader,CancellationToken.None,s=>{})){
            check(preview.Cells.Count==9 && preview.Cells[0].Group==groups[0] && preview.Cells[1].Texture==sibling && preview.Cells[8].Group==groups[7] && preview.Errors.Count==0,"Batch overview retains every child and its original resource identity");
            check(preview.Stamps.Count==8 && preview.Cells.All(c=>new Rectangle(Point.Empty,preview.Image.Size).Contains(c.Bounds)),"Batch hit regions fit bitmap and every resource keeps its own source stamp");stamps=preview.Stamps;
        }
        check(Directory.GetFileSystemEntries(root).SequenceEqual(new[]{path}),"Batch preview creates no disk-cache PNGs or JSON");
        var hidden=new HiddenImageStore(Path.Combine(root,"hidden.json"),true);check(hidden.HideMany(chosen.Select(g=>g.First)) && hidden.Entries().Count==8 && !hidden.Contains(groups[8].First),"One batch hide stores eight separate resource records");
        hidden.Restore(new[]{groups[2].Id});check(!hidden.Contains(groups[2].First) && hidden.Contains(groups[1].First),"One resource can be restored independently from a hidden batch");hidden.RestoreAll();
        var store=new TranslationStore(Path.Combine(root,"records"));store.SaveResources(chosen,loader,"batch",root,"fixture",stamps,CancellationToken.None,s=>{});
        check(store.Entries().Count==8 && store.Entries().Sum(e=>e.ResourceImages.Count)==9,"Batch translation saves individual resource entries and all child snapshots");
        store.Remove(new[]{groups[2].Id});check(store.ResourceIds().Count==7 && !store.ResourceIds().Contains(groups[2].Id),"Removing one batch translation entry restores only that resource");
        string before=File.ReadAllText(store.IndexPath);var badTexture=groups[9].First.Copy();badTexture.DataOffset=99;var bad=new ResourceGroup{Textures=new List<Texture>{badTexture}};var failing=new List<ResourceGroup>{groups[8],bad};var failingStamps=failing.ToDictionary(g=>g.Id,g=>PreviewLoader.SourceKey(g.First.Source));bool rejected=false;
        try{store.SaveResources(failing,loader,"failure",root,"fixture",failingStamps,CancellationToken.None,s=>{});}catch(InvalidDataException){rejected=true;}
        check(rejected && File.ReadAllText(store.IndexPath)==before,"A later decode failure cannot partially register a translation batch");
        using(var cts=new CancellationTokenSource()){bool cancelled=false;try{store.SaveResources(chosen,loader,"cancel",root,"fixture",stamps,cts.Token,s=>{if(s.StartsWith("리소스 2 /"))cts.Cancel();});}catch(OperationCanceledException){cancelled=true;}check(cancelled && File.ReadAllText(store.IndexPath)==before,"Cancelling a batch preserves all previous translation records");}
        using(var form=new MainForm(null)){
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(MainForm);
            type.GetField("hidden",flags).SetValue(form,hidden);type.GetField("loader",flags).SetValue(form,loader);
            var scan=new ScanResult();foreach(var group in groups)scan.Items.AddRange(group.Textures);type.GetField("result",flags).SetValue(form,scan);type.GetMethod("Refilter",flags).Invoke(form,null);
            var list=(ListView)type.GetField("list",flags).GetValue(form);var handle=list.Handle;
            type.GetMethod("SelectResource",flags).Invoke(form,new object[]{groups[0],false});type.GetMethod("PreviewSelected",flags).Invoke(form,new object[]{true});
            check(((ResourceBatchPreview)type.GetField("batchPreview",flags).GetValue(form)).Groups.Count==8,"Main form defaults to previewing eight different source locations");
            var canvas=(AtlasCanvas)type.GetField("canvas",flags).GetValue(form);var batch=(ResourceBatchPreview)type.GetField("batchPreview",flags).GetValue(form);var target=batch.Cells.First(c=>c.Group.Id==groups[3].Id);canvas.ImageClicked(new Point(target.Bounds.X+10,target.Bounds.Y+10));
            check(((ResourceGroup)type.GetField("currentResource",flags).GetValue(form)).Id==groups[3].Id && list.SelectedIndices[0]==3 && canvas.Highlights.Count==1,"Clicking a batch image selects that source for single-resource actions");
            type.GetMethod("HideCurrent",flags).Invoke(form,null);check(hidden.Entries().Count==1 && hidden.Contains(groups[3].First) && !hidden.Contains(groups[0].First),"Single hide in a batch affects only the clicked resource");
            type.GetMethod("SelectResource",flags).Invoke(form,new object[]{groups[0],false});type.GetMethod("PreviewSelected",flags).Invoke(form,new object[]{true});type.GetMethod("HideBatch",flags).Invoke(form,null);
            var remaining=(List<ResourceGroup>)type.GetField("visible",flags).GetValue(form);check(remaining.Count==1 && remaining[0].Id==groups[9].Id && list.SelectedIndices.Count==1,"Batch hide removes displayed resources and selects the next survivor");
        }
        check(File.ReadAllBytes(path).SequenceEqual(Enumerable.Range(0,256).Select(i=>(byte)i)),"Batch preview hide and translation never modify source bytes");
    }
}
}
