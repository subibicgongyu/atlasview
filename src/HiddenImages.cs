using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AtlasView
{
    internal static class HideNavigation
    {
        internal static Texture Next(List<Texture> items, int selectedIndex, Func<Texture, bool> hidden)
        {
            for (int candidateIndex = Math.Max(0, selectedIndex + 1); candidateIndex < items.Count; candidateIndex++)
            {
                if (items[candidateIndex].Supported && !hidden(items[candidateIndex]))
                {
                    return items[candidateIndex];
                }
            }

            for (int candidateIndex = Math.Min(selectedIndex - 1, items.Count - 1); candidateIndex >= 0; candidateIndex--)
            {
                if (items[candidateIndex].Supported && !hidden(items[candidateIndex]))
                {
                    return items[candidateIndex];
                }
            }

            return null;
        }
        internal static bool ShortcutAllowed(Keys keys, Control active)
        {
            if (keys != Keys.H)
            {
                return false;
            }

            while (active is ContainerControl && ((ContainerControl)active).ActiveControl != null)
            {
                active = ((ContainerControl)active).ActiveControl;
            }

            for (Control activeControl = active; activeControl != null; activeControl = activeControl.Parent)
            {
                if (activeControl is TextBoxBase || activeControl is ComboBox || activeControl is UpDownBase)
                {
                    return false;
                }
            }

            return true;
        }
    }
    public sealed class HiddenImageEntry
    {
        public string Key, SourcePath, Name, Locator, Format;
        public int Width, Height, DataOffset;
        public long SourceOffset, HiddenUtcTicks;
        public Texture PreviewTexture;
    }
    public sealed class HiddenImageIndex
    {
        public int Version = 1;
        public Dictionary<string, HiddenImageEntry> Images = new Dictionary<string, HiddenImageEntry>();
    }
    internal sealed class HiddenImageStore
    {
        internal readonly string FilePath;
        HiddenImageIndex index = new HiddenImageIndex();
        readonly bool resources;
        internal string LastError = "";
        internal HiddenImageStore(string path, bool resourceMode = false)
        {
            resources = resourceMode;
            FilePath = Path.GetFullPath(path);
            Reload();
        }
        // Stable across disk-cache eviction, process restarts and source timestamp changes.
        internal static string Key(Texture texture)
        {
            return Bin.TextHash(string.Join("|", new object[] { "hidden-v1", Path.GetFullPath(texture.Source.Path).ToUpperInvariant(), texture.Source.Offset, texture.DataOffset, texture.StandardImage, texture.Format, texture.Width, texture.Height, texture.Pitch, texture.BytesPerPixel, texture.Swizzle, texture.BigEndian, texture.RMask, texture.GMask, texture.BMask, texture.AMask }));
        }
        internal bool Contains(Texture texture)
        {
            return index.Images.ContainsKey(resources ? ResourceGroup.Key(texture.Source) : Key(texture));
        }
        internal int Count
        {
            get
            {
                return index.Images.Count;
            }
        }
        internal List<HiddenImageEntry> Entries()
        {
            return index.Images.Values.OrderByDescending(hiddenEntry => hiddenEntry.HiddenUtcTicks).ToList();
        }
        static void Plain(string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Bin.Need((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "숨김 설정 경로 링크는 사용할 수 없습니다.");
            }
        }
        HiddenImageIndex Read()
        {
            Plain(Path.GetDirectoryName(FilePath));
            Plain(FilePath);
            if (!File.Exists(FilePath))
            {
                return new HiddenImageIndex();
            }

            Bin.Need(new FileInfo(FilePath).Length <= 16 * 1024 * 1024, "숨김 목록 파일 크기 초과");
            var value = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<HiddenImageIndex>(File.ReadAllText(FilePath, Encoding.UTF8));
            Bin.Need(value != null && value.Version == 1 && value.Images != null, "숨김 목록 형식 오류");
            foreach (var item in value.Images)
            {
                Bin.Need(item.Key.Length == 64 && item.Value != null && item.Value.Key == item.Key && item.Value.SourcePath != null && item.Value.HiddenUtcTicks > 0 && item.Value.HiddenUtcTicks <= DateTime.MaxValue.Ticks, "숨김 항목 형식 오류");
            }

            return value;
        }
        internal bool Reload()
        {
            try
            {
                index = Read();
                LastError = "";
                return true;
            }
            catch (Exception exception) { LastError = exception.Message; return false; }
        }
        bool Change(Action<HiddenImageIndex> edit)
        {
            try
            {
                using (var mutex = new Mutex(false, "Local\\PS3AtlasHidden_" + Bin.TextHash(FilePath.ToUpperInvariant())))
                {
                    bool held = false;
                    try
                    {
                        try
                        {
                            held = mutex.WaitOne(2000);
                        }
                        catch (AbandonedMutexException) { held = true; }
                        if (!held)
                        {
                            throw new IOException("다른 뷰어가 숨김 목록을 저장 중입니다.");
                        }

                        var next = Read();
                        edit(next);
                        string directory = Path.GetDirectoryName(FilePath);
                        Directory.CreateDirectory(directory);
                        Plain(directory);
                        byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(next));
                        Bin.Need(bytes.Length <= 16 * 1024 * 1024, "숨김 목록 파일 크기 초과");
                        string temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                        try
                        {
                            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                            {
                                stream.Write(bytes, 0, bytes.Length);
                                stream.Flush(true);
                            }
                            if (File.Exists(FilePath))
                            {
                                File.Replace(temp, FilePath, null);
                            }
                            else
                            {
                                File.Move(temp, FilePath);
                            }
                        }
                        finally { if (File.Exists(temp))
                            {
                                File.Delete(temp);
                            }
                        }
                        index = next;
                        LastError = "";
                        return true;
                    }
                    finally { if (held)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
            }
            catch (Exception exception) { LastError = exception.Message; return false; }
        }
        internal bool Hide(Texture texture)
        {
            return HideMany(new[] { texture });
        }
        internal bool HideMany(IEnumerable<Texture> textures)
        {
            var items = textures.ToList();
            return Change(next => { foreach (var texture in items) { string key = resources ? ResourceGroup.Key(texture.Source) : Key(texture); if (!next.Images.ContainsKey(key)) { next.Images.Add(key, new HiddenImageEntry { Key = key, SourcePath = Path.GetFullPath(texture.Source.Path), Name = resources ? "리소스 전체" : texture.Name, Locator = resources ? Path.GetFileName(texture.Source.Path) + " @0x" + texture.Source.Offset.ToString("X") + " / 리소스 전체" : texture.Locator, Format = resources ? "전체" : texture.Format, Width = resources ? 0 : texture.Width, Height = resources ? 0 : texture.Height, SourceOffset = texture.Source.Offset, DataOffset = texture.DataOffset, PreviewTexture = texture.Container == "수동 해석" ? texture.Copy() : null, HiddenUtcTicks = DateTime.UtcNow.Ticks }); } } });
        }
        internal bool Restore(IEnumerable<string> keys)
        {
            var remove = keys.ToArray();
            return Change(next => { foreach (string key in remove) { next.Images.Remove(key); } });
        }
        internal bool RestoreAll()
        {
            return Change(next => next.Images.Clear());
        }
    }

}
