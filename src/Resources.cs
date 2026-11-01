using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace AtlasView
{
    public sealed class ResourceGroup
    {
        public List<Texture> Textures = new List<Texture>();
        public Texture First
        {
            get
            {
                return Textures[0];
            }
        }
        public string Id
        {
            get
            {
                return Key(First.Source);
            }
        }
        public string Label
        {
            get
            {
                return Path.GetFileName(First.Source.Path) + " @0x" + First.Source.Offset.ToString("X");
            }
        }
        public bool Supported
        {
            get
            {
                return Textures.Any(texture => texture.Supported);
            }
        }
        public static string Key(Source source)
        {
            return Bin.TextHash("resource-v1|" + Path.GetFullPath(source.Path).ToUpperInvariant() + "|" + source.Offset);
        }
        public static List<ResourceGroup> Group(IEnumerable<Texture> textures)
        {
            return textures.GroupBy(texture => Key(texture.Source)).Select(textureGroup => new ResourceGroup { Textures = textureGroup.OrderBy(texture => texture.DataOffset).ThenBy(texture => texture.Name, StringComparer.Ordinal).ToList() }).ToList();
        }
        internal bool Matches(string query)
        {
            return query.Length == 0 || (First.Source.Path + " " + Label + " " + string.Join(" ", Textures.Select(texture => texture.Locator + " " + texture.Format + " " + texture.Container))).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
    internal sealed class ResourcePage
    {
        internal const int Size = 200;
        List<ResourceGroup> all = new List<ResourceGroup>();
        internal List<ResourceGroup> Items = new List<ResourceGroup>();
        internal int Number
        {
            get; private set;
        }
        internal int Count
        {
            get
            {
                return Math.Max(1, (all.Count + Size - 1) / Size);
            }
        }
        internal void Reset(List<ResourceGroup> values)
        {
            all = values;
            Move(0);
        }
        internal void Move(int page)
        {
            Number = Math.Max(0, Math.Min(Count - 1, page));
            Items = all.Skip(Number * Size).Take(Size).ToList();
        }
    }
    internal sealed class ResourcePreview : IDisposable
    {
        internal Bitmap Image;
        internal List<Rectangle> Cells = new List<Rectangle>();
        internal List<string> Errors = new List<string>();
        internal string SourceStamp;
        internal static ResourcePreview Load(ResourceGroup group, PreviewLoader loader, CancellationToken cancellationToken, Action<string> reportProgress)
        {
            var result = new ResourcePreview();
            try
            {
                string stamp = PreviewLoader.SourceKey(group.First.Source);
                int count = group.Textures.Count, columns = Math.Min(8, (int)Math.Ceiling(Math.Sqrt(count))), rows = (count + columns - 1) / columns;
                int cell = Math.Min(256, Math.Min(8192 / columns, 8192 / rows));
                Bin.Need(cell >= 16, "리소스 이미지 수가 미리보기 한도를 초과했습니다.");
                if (count > 1)
                {
                    result.Image = new Bitmap(columns * cell, rows * cell, PixelFormat.Format32bppArgb);
                }

                for (int textureIndex = 0; textureIndex < count; textureIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Texture texture = group.Textures[textureIndex];
                    Rectangle rect = new Rectangle(textureIndex % columns * cell, textureIndex / columns * cell, cell, cell);
                    result.Cells.Add(rect);
                    string error = "";
                    try
                    {
                        using (var bitmap = loader.Load(texture, cancellationToken, statusMessage => reportProgress((textureIndex + 1) + " / " + count + " · " + texture.Name + " · " + statusMessage)))
                        {
                            if (count == 1)
                            {
                                result.Image = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
                            }
                            else
                            {
                                using (var graphics = Graphics.FromImage(result.Image))
                                {
                                    graphics.ClearCell(rect);
                                    int caption = Math.Min(28, cell / 4);
                                    float scale = Math.Min((cell - 8f) / bitmap.Width, (cell - caption - 8f) / bitmap.Height);
                                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                                    graphics.DrawImage(bitmap, new RectangleF(rect.X + 4, rect.Y + caption, bitmap.Width * scale, bitmap.Height * scale));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { error = exception.Message; result.Errors.Add(texture.Name + ": " + error); }
                    if (count > 1)
                    {
                        using (var graphics = Graphics.FromImage(result.Image))
                        using (var font = new Font("Malgun Gothic", Math.Max(6, Math.Min(10, cell / 20f))))
                        using (var brush = new SolidBrush(error.Length > 0 ? Color.OrangeRed : Color.White))
                        {
                            graphics.DrawString(texture.Name + " · " + texture.Width + "×" + texture.Height, font, brush, new RectangleF(rect.X + 3, rect.Y + 2, cell - 6, Math.Min(28, cell / 4)));
                            if (error.Length > 0)
                            {
                                graphics.DrawString("미지원 / 읽기 오류", font, brush, new RectangleF(rect.X + 4, rect.Y + cell / 2, cell - 8, cell / 2));
                            }
                        }
                    }
                }
                if (stamp == PreviewLoader.SourceKey(group.First.Source))
                {
                    result.SourceStamp = stamp;
                }

                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
    internal static class ResourceDrawing
    {
        internal static void ClearCell(this Graphics graphics, Rectangle cellBounds)
        {
            using (var fill = new SolidBrush(Color.FromArgb(34, 39, 48)))
            {
                graphics.FillRectangle(fill, cellBounds);
            }

            using (var pen = new Pen(Color.FromArgb(90, 100, 110)))
            {
                graphics.DrawRectangle(pen, cellBounds.X, cellBounds.Y, cellBounds.Width - 1, cellBounds.Height - 1);
            }
        }
    }
}
