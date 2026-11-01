using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AtlasView
{
    internal static class TranslationQueueTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "translation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "archive.bin");
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            File.WriteAllBytes(source, original);
            var texture = new Texture { Source = new Source { Path = source, Label = "entry 001", Offset = 2, Length = 4, Lzp = true }, Name = "texture 001", Format = "BC3", Container = "test", Width = 2, Height = 2, DataOffset = 1, DataLength = 3, Pitch = 8, BytesPerPixel = 4, BigEndian = true };
            var store = new TranslationStore(Path.Combine(root, "records"));
            string stamp = PreviewLoader.SourceKey(texture.Source);
            using (var image = new Bitmap(2, 2))
            {
                image.SetPixel(0, 0, Color.FromArgb(100, 22, 33, 44));
                image.SetPixel(1, 0, Color.FromArgb(0, 12, 34, 56));
                store.Save(texture, image, new Rectangle(1, 0, 1, 2), "메뉴 오른쪽 일본어", root, "sample game", stamp);
                var entry = new TranslationStore(store.Root).Entries().Single();
                check(entry.Texture.Source.Offset == 2 && entry.Texture.Source.Lzp && entry.Texture.DataOffset == 1 && entry.Texture.Pitch == 8 && entry.Texture.BigEndian && entry.RelativePath == "archive.bin" && entry.Region == new Rectangle(1, 0, 1, 2), "Translation restart preserves exact source decoding metadata and selected region");
                using (var bitmap = new Bitmap(store.ImagePath(entry)))
                {
                    check(bitmap.GetPixel(0, 0).ToArgb() == image.GetPixel(0, 0).ToArgb() && bitmap.GetPixel(1, 0).ToArgb() == image.GetPixel(1, 0).ToArgb(), "Translation PNG preserves alpha and hidden RGB independently of cache");
                }

                store.Save(texture, image, Rectangle.Empty, "수정 메모", root, "sample game", stamp);
                check(store.Entries().Count == 1 && store.Entries()[0].Note == "수정 메모", "Repeated translation mark updates existing image without duplication");
                var other = texture.Copy();
                other.DataOffset = 2;
                other.Name = "texture 002";
                new TranslationStore(store.Root).Save(other, image, Rectangle.Empty, "다른 이미지", root, "sample game", stamp);
                check(store.Entries().Count == 2 && store.Entries().Select(translationEntry => translationEntry.Id).Distinct().Count() == 2, "Different images within one source remain distinct across store instances");
                string exported = store.Export(root);
                var exportedIndex = new JavaScriptSerializer().Deserialize<TranslationIndex>(File.ReadAllText(Path.Combine(exported, "untranslated.json")));
                check(exportedIndex.Images.Count == 2 && exportedIndex.Images.All(translationEntry => Bin.FileHash(Path.Combine(exported, "images", translationEntry.PngSha256 + ".png")) == translationEntry.PngSha256) && File.ReadAllText(Path.Combine(exported, "README.txt")).Contains("0x2"), "Codex handoff contains readable report exact JSON and verified PNG snapshots");
                string secondExport = store.Export(root);
                check(secondExport != exported && File.Exists(Path.Combine(exported, "untranslated.json")), "Repeated handoff export never overwrites previous package");
                store.Remove(new[] { HiddenImageStore.Key(texture) });
                check(store.Entries().Count == 1 && store.Entries()[0].Id == HiddenImageStore.Key(other) && File.Exists(store.ImagePath(entry)), "Removing translation record preserves other entries and snapshot files");
                File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddHours(1));
                bool rejected = false;
                try
                {
                    store.Save(texture, image, Rectangle.Empty, "stale", root, "", stamp);
                }
                catch { rejected = true; }
                check(rejected && store.Entries().Count == 1, "Changed source cannot be recorded against stale preview");
                string good = File.ReadAllText(store.IndexPath);
                File.WriteAllText(store.IndexPath, "broken");
                rejected = false;
                try
                {
                    store.Save(other, image, Rectangle.Empty, "", root, "", PreviewLoader.SourceKey(other.Source));
                }
                catch { rejected = true; }
                check(rejected && File.ReadAllText(store.IndexPath) == "broken", "Malformed translation index is preserved on save failure");
                File.WriteAllText(store.IndexPath, good);
                var kept = store.Entries().Single();
                File.WriteAllBytes(store.ImagePath(kept), new byte[] { 0 });
                rejected = false;
                try
                {
                    store.Export(root);
                }
                catch { rejected = true; }
                check(rejected, "Handoff rejects corrupted snapshot instead of exporting misleading preview");
                check(File.ReadAllBytes(source).SequenceEqual(original), "Translation recording removal and export preserve original game bytes");
            }
        }
    }
}
