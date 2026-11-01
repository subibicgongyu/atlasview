using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AtlasView
{
    // Do not enter a modal message loop inside a native ListView selection notification.
    internal sealed class SelectionPreviewQueue
    {
        readonly Control ownerControl;
        readonly Action showPreview;
        int latestRequestVersion;
        internal SelectionPreviewQueue(Control control, Action action)
        {
            ownerControl = control;
            showPreview = action;
        }
        internal void Schedule()
        {
            int requestVersion = ++latestRequestVersion;
            if (ownerControl.IsDisposed || !ownerControl.IsHandleCreated)
            {
                return;
            }

            ownerControl.BeginInvoke((Action)delegate
            {
                if (!ownerControl.IsDisposed && requestVersion == latestRequestVersion)
                {
                    showPreview();
                }
            });
        }
    }
    // Managed buffers only: each caller owns its Bitmap independently of eviction.
    internal sealed class BufferCache
    {
        sealed class Entry
        {
            public string Key;
            public byte[] Bytes;
        }
        readonly long capacityBytes;
        long usedBytes;
        readonly object cacheLock = new object();
        readonly Dictionary<string, LinkedListNode<Entry>> cacheEntries = new Dictionary<string, LinkedListNode<Entry>>();
        readonly LinkedList<Entry> recencyOrder = new LinkedList<Entry>();
        public BufferCache(long bytes)
        {
            capacityBytes = bytes;
        }
        internal int Count
        {
            get
            {
                lock (cacheLock)
                {
                    return cacheEntries.Count;
                }
            }
        }
        internal long UsedBytes
        {
            get
            {
                lock (cacheLock)
                {
                    return usedBytes;
                }
            }
        }
        public byte[] Get(string cacheKey)
        {
            lock (cacheLock)
            {
                LinkedListNode<Entry> cacheNode;
                if (!cacheEntries.TryGetValue(cacheKey, out cacheNode))
                {
                    return null;
                }

                recencyOrder.Remove(cacheNode);
                recencyOrder.AddFirst(cacheNode);
                return cacheNode.Value.Bytes;
            }
        }
        public void Put(string cacheKey, byte[] buffer)
        {
            lock (cacheLock)
            {
                if (buffer.LongLength > capacityBytes)
                {
                    return;
                }

                LinkedListNode<Entry> cacheNode;
                if (cacheEntries.TryGetValue(cacheKey, out cacheNode))
                {
                    usedBytes -= cacheNode.Value.Bytes.LongLength;
                    recencyOrder.Remove(cacheNode);
                    cacheEntries.Remove(cacheKey);
                } while (usedBytes + buffer.LongLength > capacityBytes && recencyOrder.Count > 0)
                {
                    var leastRecentlyUsed = recencyOrder.Last;
                    usedBytes -= leastRecentlyUsed.Value.Bytes.LongLength;
                    cacheEntries.Remove(leastRecentlyUsed.Value.Key);
                    recencyOrder.RemoveLast();
                }
                cacheEntries[cacheKey] = recencyOrder.AddFirst(new Entry { Key = cacheKey, Bytes = buffer });
                usedBytes += buffer.LongLength;
            }
        }
        public void Clear()
        {
            lock (cacheLock)
            {
                cacheEntries.Clear();
                recencyOrder.Clear();
                usedBytes = 0;
            }
        }
    }

    internal sealed class PreviewLoader
    {
        readonly BufferCache decompressedBuffers = new BufferCache(64L * 1024 * 1024), pixelBuffers = new BufferCache(128L * 1024 * 1024);
        internal int Reads, Decodes, SourceHits, ImageHits;
        internal long BytesRead;
        internal string LastOrigin = "";
        internal string MemorySummary
        {
            get
            {
                return string.Format("메모리 이미지 {0}장 · {1:0.#}/128 MiB · 압축 해제 {2:0.#}/64 MiB", pixelBuffers.Count, pixelBuffers.UsedBytes / 1048576.0, decompressedBuffers.UsedBytes / 1048576.0);
            }
        }
        public PreviewLoader()
        {
        }
        internal static string SourceKey(Source source)
        {
            var fileInfo = new FileInfo(source.Path);
            return string.Join("|", new object[] { fileInfo.FullName.ToUpperInvariant(), fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, source.Offset, source.Length, source.Lzp });
        }
        static string ImageKey(Texture texture, string sourceCacheKey)
        {
            return string.Join("|", new object[] { sourceCacheKey, texture.StandardImage, texture.Format, texture.Width, texture.Height, texture.DataOffset, texture.DataLength, texture.Pitch, texture.BytesPerPixel, texture.Swizzle, texture.BigEndian, texture.RMask, texture.GMask, texture.BMask, texture.AMask });
        }
        public void Clear()
        {
            decompressedBuffers.Clear();
            pixelBuffers.Clear();
        }
        public Bitmap Load(Texture texture, CancellationToken cancellationToken, Action<string> reportProgress)
        {
            LastOrigin = "";
            cancellationToken.ThrowIfCancellationRequested();
            Bin.Need(texture.Supported, texture.Issue);
            reportProgress("파일 정보 확인 중…");
            string sourceCacheKey = SourceKey(texture.Source), imageCacheKey = ImageKey(texture, sourceCacheKey);
            byte[] bgraPixels = pixelBuffers.Get(imageCacheKey);
            int width = texture.Width, height = texture.Height;
            // Standard-image cache includes dimensions in its buffer header.
            if (bgraPixels != null)
            {
                ImageHits++;
                LastOrigin = "메모리 캐시";
                reportProgress("메모리 캐시에서 이미지 읽는 중…");
                cancellationToken.ThrowIfCancellationRequested();
                return FromBuffer(bgraPixels);
            }
            LastOrigin = "원본 디코딩";
            if (texture.StandardImage)
            {
                Reads++;
                BytesRead += texture.Source.Length;
                reportProgress("이미지 파일 읽는 중…");
                using (var bitmap = Pixels.Load(texture, cancellationToken))
                {
                    width = bitmap.Width;
                    height = bitmap.Height;
                    bgraPixels = Pixels.Bgra(bitmap);
                }
                Decodes++;
            }
            else
            {
                byte[] data;
                Texture decodeTexture = texture;
                if (texture.Source.Lzp)
                {
                    data = decompressedBuffers.Get(sourceCacheKey);
                    if (data == null)
                    {
                        Reads++;
                        BytesRead += texture.Source.Length;
                        reportProgress("압축 데이터 읽는 중…");
                        data = Bin.Read(texture.Source.Path, texture.Source.Offset, texture.Source.Length, cancellationToken);
                        reportProgress("LZP2 압축 해제 중…");
                        data = Lzp2.Decode(data, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (sourceCacheKey == SourceKey(texture.Source))
                        {
                            decompressedBuffers.Put(sourceCacheKey, data);
                        }
                    }
                    else
                    {
                        SourceHits++;
                        LastOrigin = "압축 해제 메모리 캐시 + 픽셀 디코딩";
                    }
                }
                else
                {
                    int length = Pixels.ByteCount(texture);
                    Bin.Need(texture.DataOffset >= 0 && (long)texture.DataOffset + length <= texture.Source.Length, "소스 범위 초과");
                    Reads++;
                    BytesRead += length;
                    reportProgress("픽셀 구간 읽는 중…");
                    data = Bin.Read(texture.Source.Path, checked(texture.Source.Offset + texture.DataOffset), length, cancellationToken);
                    decodeTexture = texture.Copy();
                    decodeTexture.DataOffset = 0;
                }
                reportProgress("텍스처 디코딩 중…");
                bgraPixels = Pixels.Decode(data, decodeTexture, cancellationToken);
                Decodes++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress("화면 이미지 준비 중…");
            byte[] buffer = ToBuffer(bgraPixels, width, height);
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceCacheKey == SourceKey(texture.Source))
            {
                pixelBuffers.Put(imageCacheKey, buffer);
            }

            return Pixels.Bitmap(bgraPixels, width, height);
        }
        static byte[] ToBuffer(byte[] bgraPixels, int width, int height)
        {
            byte[] cachedImage = new byte[bgraPixels.Length + 8];
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, cachedImage, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, cachedImage, 4, 4);
            Buffer.BlockCopy(bgraPixels, 0, cachedImage, 8, bgraPixels.Length);
            return cachedImage;
        }
        static Bitmap FromBuffer(byte[] cachedImage)
        {
            int width = BitConverter.ToInt32(cachedImage, 0), height = BitConverter.ToInt32(cachedImage, 4);
            byte[] bgraPixels = new byte[cachedImage.Length - 8];
            Buffer.BlockCopy(cachedImage, 8, bgraPixels, 0, bgraPixels.Length);
            return Pixels.Bitmap(bgraPixels, width, height);
        }
    }

    internal sealed class LoadingDialog : Form
    {
        readonly Label detail = new Label();
        readonly Button cancel = new Button();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly CancellationTokenSource cancellation;
        string latest = "작업 준비 중…";
        bool finished;
        readonly ProgressBar bar = new ProgressBar();
        internal LoadingDialog(string title, string item, CancellationTokenSource cancellationSource)
        {
            cancellation = cancellationSource;
            Text = title;
            ClientSize = new Size(520, 195);
            Font = new Font("Malgun Gothic", 10);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(28, 32, 40);
            ForeColor = Color.FromArgb(223, 230, 237);
            var name = new Label { Text = item, AutoEllipsis = true, Location = new Point(20, 18), Size = new Size(480, 27) };
            detail.Location = new Point(20, 52);
            detail.Size = new Size(480, 48);
            detail.AutoEllipsis = true;
            bar.Location = new Point(20, 105);
            bar.Size = new Size(480, 20);
            bar.Style = ProgressBarStyle.Marquee;
            bar.MarqueeAnimationSpeed = 25;
            cancel.Text = "취소";
            cancel.Location = new Point(400, 145);
            cancel.Size = new Size(100, 32);
            cancel.Click += delegate
            {
                if (finished)
                {
                    Close();
                }
                else
                {
                    RequestCancel();
                }
            };
            Controls.AddRange(new Control[] { name, detail, bar, cancel });
            timer.Interval = 80;
            timer.Tick += delegate
            {
                detail.Text = cancellation.IsCancellationRequested ? "취소 중… 현재 작업을 정리하고 있습니다." : Volatile.Read(ref latest);
            };
            timer.Start();
            FormClosing += delegate (object sender, FormClosingEventArgs eventArgs)
            {
                if (!finished)
                {
                    eventArgs.Cancel = true;
                    RequestCancel();
                }
            };
        }
        internal void Report(string message)
        {
            Volatile.Write(ref latest, message);
        }
        string Latest
        {
            get
            {
                return Volatile.Read(ref latest);
            }
        }
        internal void RequestCancel()
        {
            cancellation.Cancel();
            cancel.Enabled = false;
            detail.Text = "취소 중…";
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
            }

            base.Dispose(disposing);
        }
        internal static T Run<T>(IWin32Window owner, string title, string item, CancellationTokenSource cancellationSource, Func<CancellationToken, Action<string>, T> work)
        {
            T value = default(T);
            Exception error = null;
            using (var dialog = new LoadingDialog(title, item, cancellationSource))
            {
                dialog.Shown += async delegate
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        dialog.detail.Text = "읽는 중…";
                        dialog.Update();
                        value = await Task.Run(() => work(cancellationSource.Token, dialog.Report));
                        dialog.finished = true;
                        dialog.timer.Stop();
                        dialog.detail.Text = cancellationSource.IsCancellationRequested ? "취소 완료" : "완료 · " + dialog.Latest;
                        dialog.bar.Style = ProgressBarStyle.Blocks;
                        dialog.bar.Value = 100;
                        dialog.cancel.Text = "닫기";
                        dialog.Update();
                        int remaining = 300 - (int)watch.ElapsedMilliseconds;
                        if (!cancellationSource.IsCancellationRequested && remaining > 0)
                        {
                            await Task.Delay(remaining);
                        }
                    }
                    catch (Exception exception) { error = exception; }
                    finally { dialog.finished = true; if (!dialog.IsDisposed)
                        {
                            dialog.Close();
                        }
                    }
                };
                dialog.ShowDialog(owner);
            }
            if (error != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }

            return value;
        }
    }
}
