using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;

namespace AtlasView
{
    internal static class ResourceBatch
    {
        internal static List<ResourceGroup> Slice(List<ResourceGroup> items, int index, int size)
        {
            if (items.Count == 0 || index < 0)
            {
                return new List<ResourceGroup>();
            }

            size = Math.Max(1, Math.Min(32, size));
            index = Math.Min(index, items.Count - 1);
            return items.Skip(index / size * size).Take(size).ToList();
        }
    }
    internal sealed class ResourceBatchCell
    {
        internal Rectangle Bounds;
        internal ResourceGroup Group;
        internal Texture Texture;
    }
    internal sealed class ResourceBatchPreview : IDisposable
    {
        internal Bitmap Image;
        internal List<ResourceGroup> Groups;
        internal List<ResourceBatchCell> Cells = new List<ResourceBatchCell>();
        internal Dictionary<string, string> Stamps = new Dictionary<string, string>();
        internal List<string> Errors = new List<string>();
        internal static ResourceBatchPreview Load(List<ResourceGroup> groups, PreviewLoader loader, CancellationToken cancellationToken, Action<string> reportProgress)
        {
            var result = new ResourceBatchPreview { Groups = groups.ToList() };
            try
            {
                int count = groups.Sum(resourceGroup => resourceGroup.Textures.Count);
                Bin.Need(count > 0 && count <= 4096, "묶음의 이미지 수가 4096개를 넘습니다. 묶음 크기를 줄여 주세요.");
                int columns = Math.Min(8, (int)Math.Ceiling(Math.Sqrt(count))), rows = (count + columns - 1) / columns;
                int cell = Math.Min(256, Math.Min(8192 / columns, 8192 / rows));
                result.Image = new Bitmap(columns * cell, rows * cell, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(result.Image))
                using (var font = new Font("Malgun Gothic", Math.Max(6, Math.Min(10, cell / 22f))))
                {
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    int nextCellIndex = 0;
                    foreach (var group in groups)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string stamp = null, sourceError = null;
                        try
                        {
                            stamp = PreviewLoader.SourceKey(group.First.Source);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception exception) { sourceError = exception.Message; }
                        foreach (var texture in group.Textures)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int cellIndex = nextCellIndex++;
                            var rect = new Rectangle(cellIndex % columns * cell, cellIndex / columns * cell, cell, cell);
                            result.Cells.Add(new ResourceBatchCell { Bounds = rect, Group = group, Texture = texture });
                            graphics.ClearCell(rect);
                            string error = sourceError;
                            int caption = Math.Min(48, cell / 3), margin = Math.Min(4, cell / 8);
                            if (error == null)
                            {
                                try
                                {
                                    using (var bitmap = loader.Load(texture, cancellationToken, statusMessage => reportProgress((cellIndex + 1) + " / " + count + " · " + group.Label + " · " + statusMessage)))
                                    {
                                        float scale = Math.Min((cell - 2f * margin) / bitmap.Width, (cell - caption - 2f * margin) / bitmap.Height);
                                        graphics.DrawImage(bitmap, new RectangleF(rect.X + margin, rect.Y + caption + margin, bitmap.Width * scale, bitmap.Height * scale));
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception exception) { error = exception.Message; }
                            }

                            using (var brush = new SolidBrush(error == null ? Color.White : Color.OrangeRed))
                            {
                                graphics.DrawString(group.Label + "\n" + texture.Name + " · " + texture.Width + "×" + texture.Height, font, brush, new RectangleF(rect.X + margin, rect.Y + 1, cell - 2 * margin, caption));
                                if (error != null)
                                {
                                    result.Errors.Add(group.Label + " / " + texture.Name + ": " + error);
                                    graphics.DrawString("미지원 / 읽기 오류", font, brush, new RectangleF(rect.X + margin, rect.Y + caption, cell - 2 * margin, cell - caption));
                                }
                            }
                        }
                        try
                        {
                            if (stamp != null && stamp == PreviewLoader.SourceKey(group.First.Source))
                            {
                                result.Stamps[group.Id] = stamp;
                            }
                        }
                        catch (Exception exception) { result.Errors.Add(group.Label + ": " + exception.Message); }
                    }
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
}
