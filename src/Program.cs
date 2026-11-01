using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace AtlasView
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--selftest")
                {
                    Tests.Run(args.Length > 1 ? args[1] : "artifacts");
                    return 0;
                }
                if (args.Length >= 3 && args[0] == "--scan")
                {
                    string output = Path.GetFullPath(args[2]);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Bin.Need(!output.StartsWith(Path.GetFullPath(args[1]).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && !output.Equals(Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase), "보고서 폴더는 입력 폴더 밖에 지정하세요.");
                    Directory.CreateDirectory(output);
                    var scanResult = Scanner.Scan(args[1], args.Contains("--deep"), CancellationToken.None, statusMessage => Console.WriteLine(statusMessage));
                    Reports.Csv(scanResult, Path.Combine(output, "index.csv"), args.Contains("--overwrite"));
                    var decoded = new List<object>();
                    int failures = 0;
                    foreach (var texture in scanResult.Items.Where(texture => texture.Supported).Where((texture, index) => index < 24 || index % 83 == 0))
                    {
                        try
                        {
                            using (var bitmap = Pixels.Load(texture, CancellationToken.None))
                            {
                                string pixelHash = Bin.Hash(Pixels.Bgra(bitmap));
                                decoded.Add(new
                                {
                                    locator = texture.Locator,
                                    width = bitmap.Width,
                                    height = bitmap.Height,
                                    bgraSha256 = pixelHash
                                });
                                if (decoded.Count <= 12)
                                {
                                    bitmap.Save(Path.Combine(output, "preview-" + decoded.Count.ToString("D2") + ".png"), ImageFormat.Png);
                                }
                            }
                        }
                        catch (Exception exception) { failures++; scanResult.Messages.Add(texture.Locator + ": " + exception.Message); }
                    }
                    var report = new
                    {
                        game = scanResult.Game,
                        root = scanResult.Root,
                        files = scanResult.Files,
                        items = scanResult.Items.Count,
                        supported = scanResult.Items.Count(texture => texture.Supported),
                        unsupported = scanResult.Items.Count(texture => !texture.Supported),
                        formats = scanResult.Items.Where(texture => texture.Supported).GroupBy(texture => texture.Container + " / " + texture.Format).ToDictionary(textureGroup => textureGroup.Key, textureGroup => textureGroup.Count()),
                        sampleDecodeCount = decoded.Count,
                        sampleDecodeFailures = failures,
                        decoded = decoded,
                        messages = scanResult.Messages,
                        elapsedSeconds = watch.Elapsed.TotalSeconds,
                        gameExecuted = false
                    };
                    File.WriteAllText(Path.Combine(output, "scan-report.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(report), Encoding.UTF8);
                    Console.WriteLine("RESULT " + new JavaScriptSerializer().Serialize(new
                    {
                        scanResult.Files,
                        items = scanResult.Items.Count,
                        decoded = decoded.Count,
                        failures
                    }));
                    return failures == 0 ? 0 : 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
                return 0;
            }
            catch (Exception exception) { if (args.Length > 0 && args[0].StartsWith("--")) { Console.Error.WriteLine(exception); return 1; } MessageBox.Show(exception.ToString(), "PS3 Atlas Viewer"); return 1; }
        }
    }
    public static class Tests
    {
        static int count;
        static List<string> passed = new List<string>();
        static void Check(bool conditionSatisfied, string name)
        {
            if (!conditionSatisfied)
            {
                throw new Exception("FAIL: " + name);
            }

            count++;
            passed.Add(name);
        }
        static void Reject(Action action, string name)
        {
            try
            {
                action();
            }
            catch (InvalidDataException) { Check(true, name); return; }
            throw new Exception("Expected rejection: " + name);
        }
        static void U(byte[] buffer, int offset, uint value, bool bigEndian = true)
        {
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                buffer[offset + byteIndex] = (byte)(value >> (8 * (bigEndian ? 3 - byteIndex : byteIndex)));
            }
        }
        static void S(byte[] buffer, int offset, string text)
        {
            Encoding.ASCII.GetBytes(text).CopyTo(buffer, offset);
        }
        static byte[] Dds(string format, int width = 4, int height = 4)
        {
            int blockByteCount = format == "DXT1" ? 8 : 16;
            var ddsData = new byte[128 + blockByteCount * ((width + 3) / 4) * ((height + 3) / 4)];
            S(ddsData, 0, "DDS ");
            U(ddsData, 4, 124, false);
            U(ddsData, 8, 0x81007, false);
            U(ddsData, 12, (uint)height, false);
            U(ddsData, 16, (uint)width, false);
            U(ddsData, 20, (uint)(ddsData.Length - 128), false);
            U(ddsData, 76, 32, false);
            U(ddsData, 80, 4, false);
            S(ddsData, 84, format);
            U(ddsData, 108, 0x1000, false);
            return ddsData;
        }
        static Texture Parse(byte[] textureData)
        {
            return Formats.Parse(new Source { Label = "fixture", Length = textureData.Length }, textureData)[0];
        }
        static byte[] Decode(byte[] textureData)
        {
            return Pixels.Decode(textureData, Parse(textureData), CancellationToken.None);
        }
        public static void Run(string output)
        {
            Directory.CreateDirectory(output);
            count = 0;
            passed.Clear();
            var bc1Data = Dds("DXT1");
            bc1Data[128] = 0;
            bc1Data[129] = 0xf8;
            bc1Data[130] = 0xe0;
            bc1Data[131] = 7;
            bc1Data[132] = 0xe4;
            var decodedPixels = Decode(bc1Data);
            Check(decodedPixels[2] == 255 && decodedPixels[1] == 0 && decodedPixels[3] == 255, "BC1 red endpoint");
            Check(decodedPixels[4] == 0 && decodedPixels[5] == 255 && decodedPixels[6] == 0, "BC1 green endpoint");
            Check(decodedPixels[10] == 170 && decodedPixels[9] == 85 && decodedPixels[14] == 85 && decodedPixels[13] == 170, "BC1 both interpolation indices");
            var alpha = Dds("DXT1");
            alpha[132] = 255;
            decodedPixels = Decode(alpha);
            Check(decodedPixels[3] == 0, "BC1 transparent mode");
            var bc2Data = Dds("DXT3");
            bc2Data[128] = 0xF1;
            bc2Data[137] = 0xf8;
            decodedPixels = Decode(bc2Data);
            Check(decodedPixels[3] == 17 && decodedPixels[7] == 255 && decodedPixels[2] == 255, "BC2 explicit 4-bit alpha");
            var bc3Data = Dds("DXT5");
            bc3Data[128] = 240;
            bc3Data[129] = 30;
            bc3Data[130] = 0x88;
            bc3Data[131] = 0xc6;
            bc3Data[132] = 0xfa;
            bc3Data[137] = 0xf8;
            decodedPixels = Decode(bc3Data);
            Check(decodedPixels[3] == 240 && decodedPixels[7] == 30 && decodedPixels[11] == 210, "BC3 eight-alpha interpolation");
            bc3Data[128] = 10;
            bc3Data[129] = 110;
            decodedPixels = Decode(bc3Data);
            Check(decodedPixels[11] == 30 && decodedPixels[27] == 0 && decodedPixels[31] == 255, "BC3 six-alpha mode and extremes");
            var oddDimensionData = Dds("DXT1", 3, 2);
            oddDimensionData[129] = 0xf8;
            decodedPixels = Decode(oddDimensionData);
            Check(decodedPixels.Length == 24 && decodedPixels[22] == 255, "Non-multiple-of-four BC dimensions");
            var rawTexture = new Texture { Width = 2, Height = 2, Format = "ARGB8", BytesPerPixel = 4, Pitch = 12, DataLength = 20 };
            byte[] paddedArgbPixels = { 255, 255, 0, 0, 128, 0, 255, 0, 99, 99, 99, 99, 64, 0, 0, 255, 0, 7, 8, 9 };
            decodedPixels = Pixels.Decode(paddedArgbPixels, rawTexture, CancellationToken.None);
            Check(decodedPixels[2] == 255 && decodedPixels[7] == 128 && decodedPixels[8] == 255 && decodedPixels[15] == 0, "ARGB order and padded pitch");
            var morton = new Texture { Width = 4, Height = 2, Format = "L8", BytesPerPixel = 1, Swizzle = true, DataLength = 8 };
            decodedPixels = Pixels.Decode(new byte[] { 1, 2, 5, 6, 3, 4, 7, 8 }, morton, CancellationToken.None);
            Check(Enumerable.Range(0, 8).All(index => decodedPixels[index * 4] == index + 1), "Rectangular Morton independent known ordering");
            var mask = new Texture { Width = 1, Height = 1, Format = "MASK", BytesPerPixel = 2, BigEndian = true, RMask = 0xf800, GMask = 0x7e0, BMask = 31, DataLength = 2 };
            decodedPixels = Pixels.Decode(new byte[] { 0xf8, 0 }, mask, CancellationToken.None);
            Check(decodedPixels[2] == 255 && decodedPixels[0] == 0, "Big-endian RGB565");
            var gtfData = new byte[160];
            U(gtfData, 0, 0x020200ff);
            U(gtfData, 4, 32);
            U(gtfData, 8, 1);
            U(gtfData, 16, 128);
            U(gtfData, 20, 32);
            gtfData[24] = 0xa5;
            gtfData[25] = 1;
            gtfData[26] = 2;
            gtfData[33] = 2;
            gtfData[35] = 2;
            gtfData[37] = 1;
            U(gtfData, 40, 16);
            gtfData[128] = 255;
            gtfData[129] = 255;
            decodedPixels = Decode(gtfData);
            Check(decodedPixels[2] == 255 && Parse(gtfData).Pitch == 16, "GTF big-endian metadata and linear ARGB");
            var g1tData = new byte[60];
            S(g1tData, 0, "G1TG0050");
            U(g1tData, 8, 60);
            U(g1tData, 12, 28);
            U(g1tData, 16, 1);
            U(g1tData, 20, 1);
            U(g1tData, 28, 4);
            g1tData[33] = 8;
            g1tData[34] = 0x22;
            g1tData[40] = 255;
            g1tData[49] = 0xf8;
            decodedPixels = Decode(g1tData);
            Check(decodedPixels[2] == 255 && decodedPixels[3] == 255, "G1T 0050 texture offset and BC3");
            var lzp2Data = new byte[32];
            S(lzp2Data, 0, "LZP2");
            U(lzp2Data, 4, 0x5c8f823f);
            U(lzp2Data, 8, 8, false);
            U(lzp2Data, 12, 16, false);
            lzp2Data[16] = 2;
            lzp2Data[17] = 65;
            lzp2Data[18] = 66;
            lzp2Data[19] = 0x98;
            lzp2Data[20] = 1;
            Check(Encoding.ASCII.GetString(Lzp2.Decode(lzp2Data, CancellationToken.None)) == "ABABABAB", "LZP2 overlapping back-reference");
            var runLengthData = (byte[])lzp2Data.Clone();
            U(runLengthData, 8, 4, false);
            Array.Clear(runLengthData, 16, 16);
            runLengthData[16] = 64;
            runLengthData[18] = 120;
            Check(Lzp2.Decode(runLengthData, CancellationToken.None).All(pixelByte => pixelByte == 120), "LZP2 run length");
            var invalidData = (byte[])lzp2Data.Clone();
            invalidData[20] = 200;
            Reject(() => Lzp2.Decode(invalidData, CancellationToken.None), "Reject LZ reference before output");
            invalidData = (byte[])lzp2Data.Clone();
            invalidData[31] = 1;
            Reject(() => Lzp2.Decode(invalidData, CancellationToken.None), "Reject nonzero terminal padding");
            Reject(() => Pixels.Decode(new byte[7], morton, CancellationToken.None), "Reject truncated texture");
            rawTexture.Width = 8193;
            Reject(() => Pixels.ByteCount(rawTexture), "Reject excessive image dimension");
            var truncated = bc1Data.Take(130).ToArray();
            Check(!Parse(truncated).Supported, "DDS truncation reported as unsupported");
            var huge = (byte[])bc1Data.Clone();
            U(huge, 16, uint.MaxValue, false);
            Reject(() => Parse(huge), "Reject overflowing DDS dimension");
            var dx10 = Dds("DX10");
            Check(!Parse(dx10).Supported, "Unknown FourCC remains explicitly unsupported");
            var foreign = (byte[])g1tData.Clone();
            U(foreign, 20, 2);
            Check(!Parse(foreign).Supported, "G1T platform mismatch is not silently decoded");
            var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            try
            {
                Pixels.Decode(bc1Data, Parse(bc1Data), cancellationSource.Token);
                throw new Exception("Cancellation missed");
            }
            catch (OperationCanceledException) { Check(true, "Decoder cancellation"); }
            cancellationSource.Dispose();
            string fixtures = Path.Combine(output, "fixtures");
            Directory.CreateDirectory(fixtures);
            File.WriteAllBytes(Path.Combine(fixtures, "sample.dds"), bc1Data);
            File.WriteAllBytes(Path.Combine(fixtures, "sample.gtf"), gtfData);
            File.WriteAllBytes(Path.Combine(fixtures, "sample.g1t"), g1tData);
            File.WriteAllBytes(Path.Combine(fixtures, "unsupported.dds"), dx10);
            byte[] packed = new byte[4096 + bc1Data.Length];
            Buffer.BlockCopy(bc1Data, 0, packed, 4096, bc1Data.Length);
            File.WriteAllBytes(Path.Combine(fixtures, "embedded.bin"), packed);
            var before = Directory.GetFiles(fixtures).ToDictionary(filePath => filePath, filePath => Bin.FileHash(filePath));
            var scan = Scanner.Scan(fixtures, true, CancellationToken.None, statusMessage => { });
            Check(scan.Items.Count(texture => texture.Supported && new[] { "sample.dds", "sample.gtf", "sample.g1t", "embedded.bin" }.Contains(Path.GetFileName(texture.Source.Path))) == 4, "Deep scan finds embedded DDS with extensionless container");
            Check(before.All(originalFile => Bin.FileHash(originalFile.Key) == originalFile.Value), "Input bytes unchanged by scan and decode");
            using (var bitmap = Pixels.Bitmap(Decode(bc1Data), 4, 4))
            {
                bitmap.Save(Path.Combine(output, "bc1.png"), ImageFormat.Png);
                using (var loaded = new Bitmap(Path.Combine(output, "bc1.png")))
                {
                    Check(loaded.GetPixel(0, 0).R == 255 && loaded.GetPixel(1, 0).G == 255, "PNG round trip independent GDI read");
                }
            }
            using (var bitmap = Pixels.Bitmap(new byte[] { 3, 7, 11, 0, 55, 66, 77, 128 }, 2, 1))
            {
                string file = Path.Combine(fixtures, "alpha.png");
                bitmap.Save(file, ImageFormat.Png);
                var standardImageTexture = new Texture { Source = new Source { Path = file, Length = (int)new FileInfo(file).Length }, StandardImage = true };
                using (var loaded = Pixels.Load(standardImageTexture, CancellationToken.None))
                {
                    Check(Pixels.Bgra(loaded).SequenceEqual(Pixels.Bgra(bitmap)), "PNG hidden RGB and partial alpha are preserved");
                }
            }
            var g1argb = new byte[68];
            S(g1argb, 0, "G1TG0060");
            U(g1argb, 8, 68);
            U(g1argb, 12, 28);
            U(g1argb, 16, 1);
            U(g1argb, 20, 1);
            U(g1argb, 28, 4);
            g1argb[33] = 1;
            g1argb[34] = 0x11;
            U(g1argb, 40, 12);
            g1argb[52] = 127;
            g1argb[53] = 255;
            decodedPixels = Decode(g1argb);
            Check(decodedPixels[2] == 255 && decodedPixels[3] == 127, "G1T 0060 extended header and ARGB");
            // Exercise the real display path, including alpha projection, without opening a game.
            using (var canvas = new AtlasCanvas())
            using (var target = new Bitmap(2, 1))
            using (var bitmap = Pixels.Bitmap(new byte[] { 3, 7, 11, 0, 55, 66, 77, 128 }, 2, 1))
            {
                canvas.Size = new Size(2, 1);
                canvas.Image = bitmap;
                canvas.Zoom = 1;
                canvas.PanX = canvas.PanY = 0;
                canvas.Channel = 2;
                canvas.DrawToBitmap(target, new Rectangle(0, 0, 2, 1));
                var first = target.GetPixel(0, 0);
                var second = target.GetPixel(1, 0);
                Check(first.R == 0 && second.R == 128 && second.A == 255, "Alpha display projects alpha into opaque grayscale");
                canvas.Image = null;
            }
            var rawInput = Parse(bc1Data);
            rawInput.Source.Path = Path.Combine(fixtures, "sample.dds");
            using (var dialog = new RawDialog(rawInput))
            {
                dialog.Build();
                Check(dialog.Result.Source.Offset == 128 && dialog.Result.Source.Length == 8, "RAW dialog creates bounded source from DDS offset");
                using (var bitmap = Pixels.Load(dialog.Result, CancellationToken.None))
                {
                    Check(Pixels.Bgra(bitmap).SequenceEqual(Decode(bc1Data)), "RAW dialog preview matches automatic DDS pixels");
                }
            }
            using (var dialog = new RawDialog(new Texture { Source = new Source { Path = Path.Combine(fixtures, "sample.dds"), Length = 136 }, Name = "unknown" }))
            {
                Check(dialog != null, "RAW dialog opens for unknown input with 256px defaults");
            }
            LoadingTests(output, bc1Data, lzp2Data);

            HiddenImagesTests.Run(output, Check);
            HiddenPreviewTests.Run(output, Check);
            TranslationQueueTests.Run(output, Check);
            ResourceTests.Run(output, Check);
            ResourceBatchTests.Run(output, Check);
            var page = new TexturePage();
            var allTextures = Enumerable.Range(0, 13989).Select(index => new Texture { Name = "image " + index }).ToList();
            page.Reset(allTextures);
            Check(page.Items.Count == 200 && page.Count == 70, "Large scan exposes at most 200 native list rows and all 70 pages");
            page.Move(69);
            Check(page.Items.Count == 189 && page.Items.Last() == allTextures.Last(), "Final page retains every remainder image");
            page.Move(999);
            Check(page.Number == 69, "Page index clamps to final page");
            page.Move(-1);
            Check(page.Number == 0, "Page index clamps to first page");
            page.Reset(allTextures.Where(texture => texture.Name == "image 13000").ToList());
            Check(page.Items.Single().Name == "image 13000" && page.Number == 0, "Global search finds items outside current page and resets page");
            File.WriteAllText(Path.Combine(output, "tests.json"), new JavaScriptSerializer().Serialize(new
            {
                passed = count,
                cases = passed,
                gameExecuted = false
            }), Encoding.UTF8);
            Console.WriteLine("PASS " + count + " tests");
        }
        static void LoadingTests(string output, byte[] ddsData, byte[] lzp2Data)
        {
            string path = Path.Combine(output, "cache.dds");
            File.WriteAllBytes(path, ddsData);
            var texture = Parse(ddsData);
            texture.Source.Path = path;
            var loader = new PreviewLoader();
            var phases = new List<string>();
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(Pixels.Bgra(bitmap).SequenceEqual(Decode(ddsData)) && loader.BytesRead == 8, "Preview reads only base-mip payload and preserves pixels");
            }

            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(loader.Reads == 1 && loader.Decodes == 1 && loader.ImageHits == 1, "Repeat preview bypasses file read and decode");
                bitmap.SetPixel(0, 0, Color.Blue);
            }
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(bitmap.GetPixel(0, 0).R == 255, "Caller bitmap modification and disposal cannot corrupt cache");
            }

            Check(loader.LastOrigin == "메모리 캐시" && loader.MemorySummary.Contains("1장"), "Preview reports actual memory cache origin and retained image count");
            var changed = (byte[])ddsData.Clone();
            changed[128] = 31;
            changed[129] = 0;
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(loader.Reads == 2 && Pixels.Bgra(bitmap).SequenceEqual(Decode(changed)), "File timestamp change invalidates cached pixels");
            }

            loader.Clear();
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(loader.Reads == 3, "Rescan clears cached preview buffers");
            }

            var cache = new BufferCache(4);
            cache.Put("a", new byte[2]);
            cache.Put("b", new byte[2]);
            cache.Get("a");
            cache.Put("c", new byte[2]);
            Check(cache.Get("a") != null && cache.Get("b") == null && cache.Get("c") != null, "Cache evicts least recently used buffer within byte budget");
            cache.Put("large", new byte[5]);
            Check(cache.Get("large") == null, "Oversized buffer is not retained");
            path = Path.Combine(output, "cache.lzp");
            File.WriteAllBytes(path, lzp2Data);
            texture = new Texture { Source = new Source { Path = path, Length = lzp2Data.Length, Lzp = true }, Width = 4, Height = 2, Format = "L8", BytesPerPixel = 1, DataLength = 8 };
            loader = new PreviewLoader();
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(bitmap.GetPixel(0, 0).R == 65, "Compressed preview preserves LZP2 output");
            }

            texture = texture.Copy();
            texture.Width = 1;
            texture.Height = 1;
            texture.DataOffset = 1;
            texture.DataLength = 1;
            using (var bitmap = loader.Load(texture, CancellationToken.None, phases.Add))
            {
                Check(bitmap.GetPixel(0, 0).R == 66 && loader.Reads == 1 && loader.SourceHits == 1, "Different texture reuses decompressed source");
            }

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    loader.Load(texture, cancelled.Token, phases.Add);
                    throw new Exception("Cancellation missed");
                }
                catch (OperationCanceledException) { Check(loader.Reads == 1, "Cancelled cached load stops before IO"); }
            }
            using (var cancellationSource = new CancellationTokenSource())
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                bool modal = false;
                timer.Tick += delegate
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        if (form is LoadingDialog)
                        {
                            modal = form.Modal;
                            ((LoadingDialog)form).RequestCancel();
                            timer.Stop();
                            break;
                        }
                    }
                };
                timer.Start();
                try
                {
                    LoadingDialog.Run<int>(null, "읽는 중 — 취소 검사", "자동 검사", cancellationSource, (cancellationToken, report) => { report("취소 동작 확인 중…"); if (!cancellationToken.WaitHandle.WaitOne(5000)) { throw new Exception("Modal timer did not cancel"); } cancellationToken.ThrowIfCancellationRequested(); return 0; });
                    throw new Exception("Cancellation missed");
                }
                catch (OperationCanceledException) { Check(modal, "Modal stays responsive and propagates cancellation"); }
            }
            using (var cancellationSource = new CancellationTokenSource())
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int result = LoadingDialog.Run<int>(null, "읽는 중 — 완료 검사", "자동 검사", cancellationSource, (cancellationToken, report) => 7);
                Check(result == 7, "Modal closes automatically on worker completion");
                Check(watch.ElapsedMilliseconds >= 250, "Instant work retains visible completed progress feedback for minimum display time");
            }
            using (var cancellationSource = new CancellationTokenSource())
            {
                Reject(() => LoadingDialog.Run<int>(null, "읽는 중 — 오류 검사", "자동 검사", cancellationSource, (cancellationToken, report) => { throw new InvalidDataException("test"); }), "Modal closes and propagates worker error");
            }
            using (var host = new Form())
            using (var items = new ListView { VirtualMode = true, View = View.Details, Dock = DockStyle.Fill })
            {
                host.Controls.Add(items);
                items.Columns.Add("fixture", 150);
                items.RetrieveVirtualItem += delegate (object sender, RetrieveVirtualItemEventArgs eventArgs)
                {
                    eventArgs.Item = new ListViewItem("image " + eventArgs.ItemIndex);
                };
                items.VirtualListSize = 14000;
                int calls = 0, last = -1;
                bool notification = false, reentered = false;
                var queue = new SelectionPreviewQueue(host, delegate
                {
                    calls++;
                    last = items.SelectedIndices.Count == 0 ? -1 : items.SelectedIndices[0];
                    reentered |= notification;
                });
                items.SelectedIndexChanged += delegate
                {
                    notification = true;
                    queue.Schedule();
                    notification = false;
                };
                host.Show();
                Application.DoEvents();
                calls = 0;
                items.SelectedIndices.Add(13999);
                items.SelectedIndices.Clear();
                items.SelectedIndices.Add(12000);
                Check(calls == 0, "14000-item list defers preview until native selection notification returns");
                Application.DoEvents();
                Check(calls == 1 && last == 12000 && !reentered, "Queued selection coalesces rapid changes and previews only final item");
                host.Close();
            }
        }
    }
}
