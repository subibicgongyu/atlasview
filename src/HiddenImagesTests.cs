using System;
using System.IO;
using System.Linq;

namespace AtlasView
{
    internal static class HiddenImagesTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "hidden-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.bin");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
            var first = new Texture { Source = new Source { Path = source, Label = "source", Length = 4 }, Name = "first", Format = "L8", Width = 1, Height = 1, BytesPerPixel = 1, DataLength = 1 };
            var second = first.Copy();
            second.DataOffset = 1;
            second.Name = "second";
            string path = Path.Combine(root, "settings", "hidden-images.json");
            var store = new HiddenImageStore(path);
            check(store.Hide(first) && store.Contains(first) && !store.Contains(second), "Hide only selected texture within an archive");
            check(new HiddenImageStore(path).Contains(first), "Hidden selection persists across store restart");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddHours(1));
            check(new HiddenImageStore(path).Contains(first), "Hidden identity survives original timestamp changes");
            check(store.Hide(first) && store.Entries().Count == 1, "Repeated hide does not duplicate entry");
            var other = new HiddenImageStore(path);
            check(other.Hide(second) && store.Restore(new[] { HiddenImageStore.Key(first) }) && new HiddenImageStore(path).Contains(second), "Concurrent store updates merge without losing another hidden entry");
            check(!store.Contains(first) && store.Contains(second), "Restore removes only requested hidden item");
            store.Hide(first);
            using (var dialog = new HiddenImagesDialog(store))
            {
                var handle = dialog.Items.Handle;
                check(dialog.Items.Items.Count == 2, "Hidden manager lists persisted images");
                dialog.Items.Items[0].Checked = true;
                string key = (string)dialog.Items.Items[0].Tag;
                dialog.RestoreMarked();
                check(dialog.Items.Items.Count == 1 && !store.Entries().Any(hiddenEntry => hiddenEntry.Key == key), "Manager checkbox restoration updates store and list");
            }
            check(store.RestoreAll() && new HiddenImageStore(path).Entries().Count == 0, "Restore all persists empty hidden list");
            var independentTexture = first.Copy();
            independentTexture.Source = new Source { Path = source.ToUpperInvariant(), Label = "changed label", Length = 4 };
            check(HiddenImageStore.Key(first) == HiddenImageStore.Key(independentTexture), "Hidden key normalizes source path and ignores display labels");
            for (int index = 0; index < 101; index++)
            {
                var texture = first.Copy();
                texture.DataOffset = index;
                store.Hide(texture);
            }
            check(new HiddenImageStore(path).Entries().Count == 101, "Hidden preferences are independent of the 100-image disk cache limit");
            byte[] original = File.ReadAllBytes(source);
            File.WriteAllText(path, "broken");
            check(!store.RestoreAll() && store.Entries().Count == 101 && File.ReadAllText(path) == "broken", "Failed hidden-settings save preserves previous state and malformed file");
            check(File.ReadAllBytes(source).SequenceEqual(original), "Hide and restore never modify image source bytes");
            var sequence = Enumerable.Range(0, 402).Select(index => new Texture { Name = "image " + index }).ToList();
            check(HideNavigation.Next(sequence, 199, candidateTexture => candidateTexture == sequence[199]) == sequence[200], "Hide advances across a 200-row page boundary");
            check(HideNavigation.Next(sequence, 400, candidateTexture => candidateTexture == sequence[400]) == sequence[401], "Hide preserves forward order on later pages");
            check(HideNavigation.Next(sequence, 401, candidateTexture => candidateTexture == sequence[401]) == sequence[400], "Hiding final image selects preceding visible image");
            check(HideNavigation.Next(sequence, 0, candidateTexture => true) == null, "Hiding all matching images leaves no selection");
            sequence[201].Issue = "unsupported";
            check(HideNavigation.Next(sequence, 199, candidateTexture => candidateTexture == sequence[199] || candidateTexture == sequence[200]) == sequence[202], "Hide advancement skips hidden and unsupported resources");
            using (var input = new System.Windows.Forms.TextBox())
            using (var number = new System.Windows.Forms.NumericUpDown())
            using (var combo = new System.Windows.Forms.ComboBox())
            using (var list = new System.Windows.Forms.ListView())
            {
                check(!HideNavigation.ShortcutAllowed(System.Windows.Forms.Keys.H, input) && !HideNavigation.ShortcutAllowed(System.Windows.Forms.Keys.H, number) && !HideNavigation.ShortcutAllowed(System.Windows.Forms.Keys.H, combo), "H remains ordinary input in text numeric and combo controls");
                check(HideNavigation.ShortcutAllowed(System.Windows.Forms.Keys.H, list) && !HideNavigation.ShortcutAllowed(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.H, list), "Only plain H on navigation controls invokes hide shortcut");
            }
        }
    }
}
