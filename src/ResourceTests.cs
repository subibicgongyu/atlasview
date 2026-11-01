using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

namespace AtlasView
{
    internal static class ResourceTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "resources-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "archive.bin");
            File.WriteAllBytes(file, Enumerable.Range(0, 256).Select(imageIndex => (byte)imageIndex).ToArray());
            Func<int, Texture> child = imageIndex => new Texture { Source = new Source { Path = file, Label = "archive", Length = 256 }, Name = "texture " + imageIndex, Width = 1, Height = 1, Format = "L8", BytesPerPixel = 1, DataOffset = imageIndex, DataLength = 1 };
            var firstTexture = child(10);
            var secondTexture = child(50);
            var different = child(70);
            different.Source.Offset = 1;
            different.Source.Length = 255;
            var groups = ResourceGroup.Group(new[] { firstTexture, secondTexture, different });
            var group = groups[0];
            check(groups.Count == 2 && group.Textures.Count == 2 && group.Label == "archive.bin @0x0", "Resource grouping uses file and source offset rather than individual texture offset");
            var caseCopy = child(80);
            caseCopy.Source.Path = file.ToUpperInvariant();
            check(ResourceGroup.Key(caseCopy.Source) == group.Id && group.Matches("texture 50") && group.Textures.Count == 2, "Group identity normalizes path and child search retains full resource");
            var hidden = new HiddenImageStore(Path.Combine(root, "hidden.json"), true);
            check(hidden.Hide(firstTexture) && hidden.Contains(secondTexture) && !hidden.Contains(different) && hidden.Entries().Count == 1, "One resource hide covers siblings but not neighboring offsets");
            hidden.Restore(new[] { group.Id });
            check(!hidden.Contains(firstTexture) && !hidden.Contains(secondTexture), "Restoring resource restores all its images");
            var loader = new PreviewLoader();
            using (var preview = ResourcePreview.Load(group, loader, CancellationToken.None, statusMessage => { }))
            {
                check(preview.Image != null && preview.Cells.Count == 2 && preview.Errors.Count == 0 && preview.SourceStamp != null, "Resource overview renders every child and records source revision");
            }

            int reads = loader.Reads;
            using (var preview = ResourcePreview.Load(group, loader, CancellationToken.None, statusMessage => { }))
            {
            }
            check(loader.Reads == reads, "Repeated resource preview uses memory without reading source again");
            var restarted = new PreviewLoader();
            using (var preview = ResourcePreview.Load(group, restarted, CancellationToken.None, statusMessage => { }))
            {
                check(restarted.Reads == 2 && Directory.GetFileSystemEntries(root).All(imagePath => File.Exists(imagePath)), "Fresh loader decodes originals without creating a disk cache directory");
            }

            var store = new TranslationStore(Path.Combine(root, "records"));
            store.SaveResource(group, loader, "리소스 전체 확인", root, "fixture", PreviewLoader.SourceKey(firstTexture.Source), CancellationToken.None, statusMessage => { });
            var entry = store.Entries().Single();
            check(entry.Id == group.Id && entry.ResourceImages.Count == 2 && entry.ResourceImages.All(resourceImage => File.Exists(Path.Combine(store.Root, "images", resourceImage.PngSha256 + ".png"))), "Translation record stores one resource with all original child PNGs");
            store.SaveResource(group, loader, "새 메모", root, "fixture", PreviewLoader.SourceKey(firstTexture.Source), CancellationToken.None, statusMessage => { });
            check(store.Entries().Count == 1 && store.Entries()[0].Note == "새 메모", "Reregistering resource updates one translation record");
            string exported = store.Export(root);
            check(Directory.GetFiles(Path.Combine(exported, "images"), "*.png").Length == 2 && File.ReadAllText(Path.Combine(exported, "README.txt")).Contains("texture 50"), "Resource handoff includes every child and its decoding location");
            var reopened = new TranslationStore(store.Root);
            check(reopened.ResourceIds().SetEquals(new[] { group.Id }), "Recorded resource exclusion persists across restart");
            using (var form = new MainForm(null))
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(MainForm);
                type.GetField("translations", flags).SetValue(form, reopened);
                type.GetField("hidden", flags).SetValue(form, hidden);
                var scan = new ScanResult();
                scan.Items.AddRange(new[] { firstTexture, secondTexture, different });
                type.GetField("result", flags).SetValue(form, scan);
                Action refresh = () => { type.GetMethod("ReloadRecordedResources", flags).Invoke(form, null); type.GetMethod("Refilter", flags).Invoke(form, null); };
                Func<List<ResourceGroup>> shown = () => (List<ResourceGroup>)type.GetField("visible", flags).GetValue(form);
                refresh();
                check(shown().Count == 1 && shown()[0].Id == groups[1].Id, "Main list excludes recorded siblings but keeps neighboring resource");
                reopened.Remove(new[] { group.Id });
                refresh();
                check(shown().Count == 2, "Removing translation record restores resource to main list");
                hidden.Hide(firstTexture);
                store.SaveResource(group, loader, "hidden and recorded", root, "fixture", PreviewLoader.SourceKey(firstTexture.Source), CancellationToken.None, statusMessage => { });
                refresh();
                reopened.Remove(new[] { group.Id });
                refresh();
                check(shown().Count == 1 && hidden.Contains(firstTexture), "Removing translation record preserves independent hidden state");
                hidden.Restore(new[] { group.Id });
                refresh();
                check(shown().Count == 2, "Restoring hidden resource after record removal returns all siblings");
            }
            var largeResource = new ResourceGroup { Textures = Enumerable.Range(0, 121).Select(child).ToList() };
            using (var preview = ResourcePreview.Load(largeResource, loader, CancellationToken.None, statusMessage => { }))
            {
                check(preview.Cells.Count == 121 && preview.Image.Width <= 8192 && preview.Image.Height <= 8192, "Large resource overview keeps all children within bounded bitmap dimensions");
            }

            var page = new ResourcePage();
            page.Reset(Enumerable.Range(0, 402).Select(imageIndex => new ResourceGroup { Textures = new List<Texture> { child(imageIndex) } }).ToList());
            page.Move(2);
            check(page.Items.Count == 2 && page.Number == 2 && page.Count == 3, "Resource list retains 200-row pagination");
        }
    }
}
