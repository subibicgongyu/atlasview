using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AtlasView
{
    internal static class HiddenPreviewTests
    {
        static HiddenImageEntry Entry(string path, long offset)
        {
            return new HiddenImageEntry { SourcePath = path, SourceOffset = offset, Key = ResourceGroup.Key(new Source { Path = path, Offset = offset }), Locator = Path.GetFileName(path) + " @0x" + offset.ToString("X"), HiddenUtcTicks = DateTime.UtcNow.Ticks };
        }
        static void U(byte[] buffer, int offset, uint value, bool bigEndian)
        {
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                buffer[offset + byteIndex] = (byte)(value >> (8 * (bigEndian ? 3 - byteIndex : byteIndex)));
            }
        }
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "hidden-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string fixtures = Path.Combine(output, "fixtures");
            var resolver = new HiddenPreviewResolver(null);
            var loader = new PreviewLoader();
            foreach (string name in new[] { "sample.dds", "sample.gtf", "sample.g1t", "alpha.png", "embedded.bin" })
            {
                string file = Path.Combine(fixtures, name);
                var entry = Entry(file, name == "embedded.bin" ? 4096 : 0);
                string hash = Bin.FileHash(file);
                var group = resolver.Resolve(entry, CancellationToken.None);
                using (var preview = ResourcePreview.Load(group, loader, CancellationToken.None, statusMessage => { }))
                {
                    check(preview.Image != null && preview.Errors.Count == 0 && group.Id == entry.Key && Bin.FileHash(file) == hash, "Hidden preview resolves existing " + name + " without a folder rescan or source modification");
                }
            }
            byte[] g1tData = File.ReadAllBytes(Path.Combine(fixtures, "sample.g1t")), lzp2Data = new byte[80];
            System.Text.Encoding.ASCII.GetBytes("LZP2").CopyTo(lzp2Data, 0);
            U(lzp2Data, 4, 0x5c8f823f, true);
            U(lzp2Data, 8, (uint)g1tData.Length, false);
            U(lzp2Data, 12, 64, false);
            lzp2Data[16] = (byte)g1tData.Length;
            g1tData.CopyTo(lzp2Data, 17);
            string packed = Path.Combine(root, "packed.bin");
            File.WriteAllBytes(packed, lzp2Data);
            var compressed = resolver.Resolve(Entry(packed, 0), CancellationToken.None);
            check(compressed.First.Source.Lzp && compressed.First.Supported, "Hidden preview reconstructs bounded LZP2 resources from stored offset");
            string rawFile = Path.Combine(root, "raw.bin");
            File.WriteAllBytes(rawFile, new byte[] { 255, 127 });
            var rawTexture = new Texture { Source = new Source { Path = rawFile, Length = 2 }, Name = "RAW", Container = "수동 해석", Format = "L8", BytesPerPixel = 1, Width = 2, Height = 1, DataLength = 2 };
            var rawStore = new HiddenImageStore(Path.Combine(root, "raw-hidden.json"), true);
            rawStore.Hide(rawTexture);
            var rawEntry = new HiddenImageStore(rawStore.FilePath, true).Entries().Single();
            using (var preview = ResourcePreview.Load(resolver.Resolve(rawEntry, CancellationToken.None), loader, CancellationToken.None, statusMessage => { }))
            {
                check(preview.Image.Width == 2 && preview.Image.GetPixel(1, 0).R == 127, "New hidden RAW entries retain exact decoding settings across restart");
            }

            var index = new HiddenImageIndex();
            for (int hiddenEntryIndex = 0; hiddenEntryIndex < 405; hiddenEntryIndex++)
            {
                var entry = Entry(rawFile, hiddenEntryIndex);
                entry.Locator = "fixture-" + hiddenEntryIndex.ToString("D3");
                entry.HiddenUtcTicks += hiddenEntryIndex;
                index.Images.Add(entry.Key, entry);
            }
            string path = Path.Combine(root, "many.json");
            File.WriteAllText(path, new JavaScriptSerializer { MaxJsonLength = 16777216 }.Serialize(index));
            var store = new HiddenImageStore(path, true);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(HiddenImagesDialog);
            using (var dialog = new HiddenImagesDialog(store))
            {
                var handle = dialog.Items.Handle;
                check(dialog.Items.Items.Count == 200 && store.Count == 405, "Hidden manager exposes at most 200 native rows for a large hidden list");
                dialog.Items.Items[0].Checked = true;
                string first = (string)dialog.Items.Items[0].Tag;
                dialog.MovePage(1);
                dialog.Items.Items[0].Checked = true;
                string second = (string)dialog.Items.Items[0].Tag;
                dialog.MovePage(-1);
                check(dialog.Items.Items[0].Checked, "Hidden check marks survive page navigation");
                dialog.RestoreMarked();
                check(store.Count == 403 && store.Entries().All(hiddenEntry => hiddenEntry.Key != first && hiddenEntry.Key != second), "Checked restore includes marks on different pages");
                dialog.MovePage(2);
                check(dialog.Items.Items.Count == 3, "Hidden final page retains all remaining entries");
                var search = (TextBox)type.GetField("search", flags).GetValue(dialog);
                search.Text = "fixture-000";
                dialog.RefreshItems();
                check(dialog.Items.Items.Count == 1 && ((string)dialog.Items.Items[0].Tag) == Entry(rawFile, 0).Key, "Hidden search covers entries outside the current page");
                search.Text = "";
                dialog.RefreshItems();
                dialog.MarkFiltered();
                check(((HashSet<string>)type.GetField("marked", flags).GetValue(dialog)).Count == 403 && dialog.Items.Items.Count == 200, "Select search results marks all pages without creating all native rows");
            }
            using (var dialog = new HiddenImagesDialog(rawStore))
            {
                var handle = dialog.Handle;
                dialog.StartPreview(Entry(Path.Combine(fixtures, "sample.dds"), 0), null);
                dialog.StartPreview(Entry(Path.Combine(fixtures, "alpha.png"), 0), null);
                var watch = Stopwatch.StartNew();
                while (type.GetField("previewCancel", flags).GetValue(dialog) != null && watch.ElapsedMilliseconds < 10000)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                var canvas = (AtlasCanvas)type.GetField("canvas", flags).GetValue(dialog);
                check(canvas.Image != null && canvas.Image.Width == 2 && canvas.Image.Height == 1, "Rapid hidden selections discard cancelled preview and display only latest image");
                dialog.StartPreview(Entry(Path.Combine(root, "missing.png"), 0), null);
                watch.Restart();
                while (type.GetField("previewCancel", flags).GetValue(dialog) != null && watch.ElapsedMilliseconds < 10000)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                check(canvas.Image == null && ((Label)type.GetField("detail", flags).GetValue(dialog)).Text.Contains("미리보기 오류") && rawStore.Count == 1, "Missing hidden source shows an error without restoring or deleting its record");
            }
        }
    }
}
