using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AtlasView
{
    public interface ITextureReader
    {
        string Name
        {
            get;
        }
        bool Matches(byte[] header);
        List<Texture> Parse(Source source, byte[] data);
    }
    public static class Formats
    {
        public static readonly ITextureReader[] Readers = { new DdsReader(), new GtfReader(), new G1tReader() };
        public static List<Texture> Parse(Source source, byte[] data)
        {
            foreach (var textureReader in Readers)
            {
                if (textureReader.Matches(data))
                {
                    return textureReader.Parse(source, data);
                }
            }

            throw new InvalidDataException("지원되는 텍스처 헤더가 없습니다.");
        }
        public static void Validate(Texture texture, byte[] data)
        {
            if (!texture.Supported)
            {
                return;
            }

            try
            {
                int requiredByteCount = Pixels.ByteCount(texture);
                Bin.Range(data, texture.DataOffset, requiredByteCount);
                Bin.Need(requiredByteCount <= texture.DataLength, "픽셀 데이터가 서브리소스 범위를 넘습니다.");
            }
            catch (Exception exception) { texture.Issue = exception.Message; }
        }
    }
    public sealed class DdsReader : ITextureReader
    {
        public string Name
        {
            get
            {
                return "DDS";
            }
        }
        public bool Matches(byte[] data)
        {
            return Bin.Magic(data, 0, "DDS ");
        }
        public List<Texture> Parse(Source source, byte[] data)
        {
            Bin.Range(data, 0, 128);
            Bin.Need(Bin.U32(data, 4, false) == 124 && Bin.U32(data, 76, false) == 32, "DDS 헤더 크기 오류");
            var texture = new Texture { Source = source, Name = "texture 0", Container = "DDS", Width = Bin.I32(data, 16, false), Height = Bin.I32(data, 12, false), DataOffset = 128, Note = "최상위 밉 / 첫 면만 표시" };
            uint flags = Bin.U32(data, 80, false);
            string fourcc = Encoding.ASCII.GetString(data, 84, 4);
            if ((flags & 4) != 0)
            {
                texture.Format = fourcc == "DXT1" ? "BC1" : fourcc == "DXT3" ? "BC2" : fourcc == "DXT5" ? "BC3" : fourcc;
                if (texture.Format != "BC1" && texture.Format != "BC2" && texture.Format != "BC3")
                {
                    texture.Issue = "미지원 DDS FourCC: " + fourcc + " (DX10/BC7 등은 별도 디코더 필요)";
                }
            }
            else if ((flags & 0x40) != 0)
            {
                texture.Format = "MASK";
                texture.BytesPerPixel = Bin.I32(data, 88, false) / 8;
                texture.RMask = Bin.U32(data, 92, false);
                texture.GMask = Bin.U32(data, 96, false);
                texture.BMask = Bin.U32(data, 100, false);
                texture.AMask = (flags & 1) != 0 ? Bin.U32(data, 104, false) : 0;
                if ((Bin.U32(data, 8, false) & 8) != 0)
                {
                    texture.Pitch = Bin.I32(data, 20, false);
                }

                if (texture.BytesPerPixel < 2 || texture.BytesPerPixel > 4)
                {
                    texture.Issue = "DDS RGB는 16/24/32비트만 지원";
                }
            }
            else if ((flags & 0x20000) != 0 && Bin.U32(data, 88, false) == 8)
            {
                texture.Format = "L8";
                texture.BytesPerPixel = 1;
                if ((Bin.U32(data, 8, false) & 8) != 0)
                {
                    texture.Pitch = Bin.I32(data, 20, false);
                }
            }
            else
            {
                texture.Format = "DDS unknown";
                texture.Issue = "미지원 DDS 픽셀 형식";
            }
            if (Bin.U32(data, 24, false) > 1)
            {
                texture.Issue = "3D DDS는 미지원";
            }

            texture.DataLength = data.Length - texture.DataOffset;
            Formats.Validate(texture, data);
            return new List<Texture> { texture };
        }
    }
    public sealed class GtfReader : ITextureReader
    {
        public string Name
        {
            get
            {
                return "GTF";
            }
        }
        public bool Matches(byte[] data)
        {
            return data.Length >= 12 && data[0] == 2 && (Bin.U32(data, 0) == 0x020200ff || Bin.U32(data, 0) == 0x020100ff || Bin.U32(data, 0) == 0x020000ff);
        }
        public List<Texture> Parse(Source source, byte[] data)
        {
            int count = Bin.I32(data, 8);
            Bin.Need(count > 0 && count <= 4096, "GTF 항목 수 오류");
            Bin.Range(data, 12, count * 36);
            var list = new List<Texture>();
            for (int textureIndex = 0; textureIndex < count; textureIndex++)
            {
                int attributeOffset = 12 + textureIndex * 36, textureHeaderOffset = attributeOffset + 12, formatCode = data[textureHeaderOffset] & 0x9f;
                var texture = new Texture { Source = source, Name = "texture " + textureIndex, Container = "GTF", Width = Bin.U16(data, textureHeaderOffset + 8), Height = Bin.U16(data, textureHeaderOffset + 10), DataOffset = Bin.I32(data, attributeOffset + 4), DataLength = Bin.I32(data, attributeOffset + 8), Pitch = Bin.I32(data, textureHeaderOffset + 16), BigEndian = true, Note = "GTF ID " + Bin.U32(data, attributeOffset) + " / 밉 " + data[textureHeaderOffset + 1] + " / remap 0x" + Bin.U32(data, textureHeaderOffset + 4).ToString("X8") + " / 최상위 밉·첫 면 / remap 미적용" };
                texture.Swizzle = (data[textureHeaderOffset] & 0x20) == 0;
                texture.BytesPerPixel = formatCode == 0x81 ? 1 : formatCode == 0x85 ? 4 : 2;
                switch (formatCode)
                {
                    case 0x81:
                        texture.Format = "L8";
                        break;
                    case 0x85:
                        texture.Format = "ARGB8";
                        break;
                    case 0x86:
                        texture.Format = "BC1";
                        break;
                    case 0x87:
                        texture.Format = "BC2";
                        break;
                    case 0x88:
                        texture.Format = "BC3";
                        break;
                    case 0x82:
                        texture.Format = "MASK";
                        texture.RMask = 0x7c00;
                        texture.GMask = 0x3e0;
                        texture.BMask = 31;
                        texture.AMask = 0x8000;
                        break;
                    case 0x83:
                        texture.Format = "MASK";
                        texture.RMask = 0xf00;
                        texture.GMask = 0xf0;
                        texture.BMask = 15;
                        texture.AMask = 0xf000;
                        break;
                    case 0x84:
                        texture.Format = "MASK";
                        texture.RMask = 0xf800;
                        texture.GMask = 0x7e0;
                        texture.BMask = 31;
                        break;
                    default:
                        texture.Format = "GCM 0x" + formatCode.ToString("X2");
                        texture.Issue = "미지원 GCM 포맷";
                        break;
                }
                // RSX DXT payloads are block-linear, independent of the uncompressed swizzle flag.
                if (formatCode >= 0x86 && formatCode <= 0x88)
                {
                    texture.Swizzle = false;
                    texture.Pitch = 0;
                }
                if (data[textureHeaderOffset + 2] != 2 || Bin.U16(data, textureHeaderOffset + 12) > 1)
                {
                    texture.Issue = "2D GTF만 지원";
                }

                if (texture.DataOffset < 12 + 36 * count || (long)texture.DataOffset + texture.DataLength > data.Length)
                {
                    texture.Issue = "GTF 데이터 범위 오류";
                }

                Formats.Validate(texture, data);
                list.Add(texture);
            }
            return list;
        }
    }
    public sealed class G1tReader : ITextureReader
    {
        public string Name
        {
            get
            {
                return "G1T";
            }
        }
        public bool Matches(byte[] data)
        {
            return Bin.Magic(data, 0, "G1TG");
        }
        public List<Texture> Parse(Source source, byte[] data)
        {
            Bin.Range(data, 0, 28);
            string version = Encoding.ASCII.GetString(data, 4, 4);
            Bin.Need(version == "0050" || version == "0060", "G1T 버전 " + version + " 미지원");
            int total = Bin.I32(data, 8), table = Bin.I32(data, 12), count = Bin.I32(data, 16);
            uint platform = Bin.U32(data, 20);
            Bin.Need(total <= data.Length && total >= 28 && table >= 28 && count > 0 && count <= 16384 && (long)table + count * 4 <= total, "G1T 인덱스 범위 오류");
            var offsets = new int[count + 1];
            for (int textureIndex = 0; textureIndex < count; textureIndex++)
            {
                offsets[textureIndex] = checked(table + Bin.I32(data, table + textureIndex * 4));
                Bin.Need(offsets[textureIndex] >= table + count * 4 && offsets[textureIndex] <= total - 8 && (textureIndex == 0 || offsets[textureIndex] > offsets[textureIndex - 1]), "G1T 텍스처 오프셋 오류");
            }
            offsets[count] = total;
            var list = new List<Texture>();
            for (int textureIndex = 0; textureIndex < count; textureIndex++)
            {
                int textureOffset = offsets[textureIndex], formatCode = data[textureOffset + 1], packedDimensions = data[textureOffset + 2];
                var texture = new Texture { Source = source, Name = "texture " + textureIndex.ToString("D3"), Container = "G1T " + version, Width = 1 << (packedDimensions >> 4), Height = 1 << (packedDimensions & 15), DataOffset = textureOffset + 8, Format = formatCode == 6 ? "BC1" : formatCode == 7 ? "BC2" : formatCode == 8 ? "BC3" : "G1T 0x" + formatCode.ToString("X2"), Note = "플랫폼 " + platform + " / texture " + textureIndex + " / 최상위 밉" };
                if (version == "0060")
                {
                    int extra = Bin.I32(data, textureOffset + 8);
                    Bin.Need(extra >= 4 && extra <= 256 && (long)textureOffset + 8 + extra <= offsets[textureIndex + 1], "G1T 확장 헤더 범위 오류");
                    texture.DataOffset += extra;
                    if (extra != 12)
                    {
                        texture.Issue = "미지원 G1T 확장 헤더 길이 " + extra;
                    }
                }
                texture.DataLength = offsets[textureIndex + 1] - texture.DataOffset;
                if (platform != 1)
                {
                    texture.Issue = "G1T 플랫폼 " + platform + " 미지원 (PS3 프로필 1만 지원)";
                }

                if (formatCode == 1 && version == "0060")
                {
                    texture.Format = "ARGB8";
                    texture.BytesPerPixel = 4;
                    texture.Note += " / 선형 ARGB";
                }
                else if (formatCode != 6 && formatCode != 7 && formatCode != 8)
                {
                    texture.Issue = "미지원 G1T 포맷 0x" + formatCode.ToString("X2");
                }

                Formats.Validate(texture, data);
                list.Add(texture);
            }
            return list;
        }
    }

    public interface IContainerReader
    {
        bool Matches(byte[] header);
        IEnumerable<Source> Entries(string file, byte[] header);
    }
    public sealed class BnsReader : IContainerReader
    {
        public bool Matches(byte[] data)
        {
            return data.Length >= 16 && Bin.U32(data, 0) == 0x19dad;
        }
        public IEnumerable<Source> Entries(string path, byte[] header)
        {
            int count = Bin.I32(header, 4), unit = Bin.I32(header, 8);
            long length = new FileInfo(path).Length;
            Bin.Need(count > 0 && count <= 100000 && unit >= 16 && unit <= 65536 && (unit & (unit - 1)) == 0, "미지원 BNS 인덱스 프로필");
            var table = Bin.Read(path, 16, count * 8);
            long previousEntryEnd = 16 + table.Length;
            for (int entryIndex = 0; entryIndex < count; entryIndex++)
            {
                long offset = (long)Bin.U32(table, entryIndex * 8) * unit;
                int entryLength = Bin.I32(table, entryIndex * 8 + 4);
                if (entryLength == 0)
                {
                    continue;
                }

                Bin.Need(offset >= previousEntryEnd && offset <= length - entryLength, "BNS 항목 범위 또는 중첩 오류: " + entryIndex);
                previousEntryEnd = offset + entryLength;
                yield return new Source { Path = path, Offset = offset, Length = entryLength, Label = System.IO.Path.GetFileName(path) + " / entry " + entryIndex.ToString("D6") };
            }
        }
    }

    public sealed class ScanResult
    {
        public List<Texture> Items = new List<Texture>();
        public List<string> Messages = new List<string>();
        public int Files;
        public string Root, Game = "";
        public bool Cancelled;
    }
    public static class Scanner
    {
        public static readonly IContainerReader[] Containers = { new BnsReader() };
        public static Texture Unknown(Source source, string reason)
        {
            return new Texture { Source = source, Name = "미지원 / RAW 가능", Container = "Unknown", Format = "—", Issue = reason };
        }
        static IEnumerable<string> Files(string rootDirectory, ScanResult result, CancellationToken cancellationToken)
        {
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootDirectory);
            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directoryPath = pendingDirectories.Pop();
                string[] subdirectories, filePaths;
                try
                {
                    filePaths = Directory.GetFiles(directoryPath);
                    subdirectories = Directory.GetDirectories(directoryPath);
                }
                catch (Exception exception) { result.Messages.Add(directoryPath + ": " + exception.Message); continue; }
                Array.Sort(filePaths, StringComparer.OrdinalIgnoreCase);
                foreach (var filePath in filePaths)
                {
                    yield return filePath;
                }

                foreach (var subdirectoryPath in subdirectories)
                {
                    try
                    {
                        if ((File.GetAttributes(subdirectoryPath) & FileAttributes.ReparsePoint) == 0)
                        {
                            pendingDirectories.Push(subdirectoryPath);
                        }
                        else
                        {
                            result.Messages.Add("링크 폴더 제외: " + subdirectoryPath);
                        }
                    }
                    catch (Exception exception) { result.Messages.Add(subdirectoryPath + ": " + exception.Message); }
                }
            }
        }
        public static ScanResult Scan(string rootDirectory, bool searchFileContents, CancellationToken cancellationToken, Action<string> reportProgress)
        {
            var scanResult = new ScanResult { Root = System.IO.Path.GetFullPath(rootDirectory) };
            try
            {
                foreach (var filePath in Files(scanResult.Root, scanResult, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scanResult.Files++;
                    reportProgress("파일 " + scanResult.Files + " · " + System.IO.Path.GetFileName(filePath));
                    long fileLength = new FileInfo(filePath).Length;
                    var source = new Source { Path = filePath, Offset = 0, Length = (int)Math.Min(fileLength, Bin.MaxBytes), Label = filePath.Substring(scanResult.Root.TrimEnd('\\').Length).TrimStart('\\') };
                    try
                    {
                        var header = Bin.Read(filePath, 0, (int)Math.Min(128, fileLength));
                        if (System.IO.Path.GetFileName(filePath).Equals("PARAM.SFO", StringComparison.OrdinalIgnoreCase))
                        {
                            scanResult.Game = ReadSfo(Bin.Read(filePath, 0, (int)Math.Min(fileLength, 1048576)));
                            continue;
                        }
                        bool containerMatched = false;
                        foreach (var containerReader in Containers)
                        {
                            if (containerReader.Matches(header))
                            {
                                containerMatched = true;
                                int entryIndex = 0;
                                foreach (var entry in containerReader.Entries(filePath, header))
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    if ((entryIndex++ % 10) == 0)
                                    {
                                        reportProgress(source.Label + " · " + entry.Label);
                                    }

                                    try
                                    {
                                        ParseSource(entry, scanResult, cancellationToken);
                                    }
                                    catch (OperationCanceledException) { throw; }
                                    catch (Exception exception) { scanResult.Items.Add(Unknown(entry, exception.Message)); }
                                }
                                break;
                            }
                        }

                        if (containerMatched)
                        {
                            continue;
                        }

                        if (IsStandard(header))
                        {
                            if (fileLength > Bin.MaxBytes)
                            {
                                scanResult.Items.Add(Unknown(source, "이미지 파일 128 MiB 한도 초과"));
                                continue;
                            }
                            using (var stream = File.OpenRead(filePath))
                            using (var image = System.Drawing.Image.FromStream(stream, false, false))
                            {
                                var texture = new Texture { Source = source, Name = "image", Container = "Image", Format = System.IO.Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(), StandardImage = true, Width = image.Width, Height = image.Height };
                                if ((long)texture.Width * texture.Height > 16777216)
                                {
                                    texture.Issue = "16M 픽셀 한도 초과";
                                }

                                scanResult.Items.Add(texture);
                            }
                            continue;
                        }
                        if (Formats.Readers.Any(textureReader => textureReader.Matches(header)) || Bin.Magic(header, 0, "LZP2"))
                        {
                            Bin.Need(fileLength <= Bin.MaxBytes, "텍스처 컨테이너 128 MiB 한도 초과");
                            ParseSource(source, scanResult, cancellationToken);
                            continue;
                        }
                        scanResult.Items.Add(Unknown(source, fileLength > Bin.MaxBytes ? "대형 파일: RAW는 지정 오프셋에서 필요한 범위만 읽습니다." : "자동 디코더 없음. RAW 또는 내부 검색을 사용하세요."));
                        string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                        if (searchFileContents && extension != ".pam" && extension != ".at3" && extension != ".g1l" && extension != ".pup")
                        {
                            Deep(filePath, scanResult, cancellationToken, reportProgress);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { scanResult.Items.Add(Unknown(source, exception.Message)); scanResult.Messages.Add(source.Label + ": " + exception.Message); }
                }
            }
            catch (OperationCanceledException) { scanResult.Cancelled = true; }
            return scanResult;
        }
        static bool IsStandard(byte[] header)
        {
            return Bin.Magic(header, 0, "\u0089PNG\r\n\u001a\n") || Bin.Magic(header, 0, "BM") || Bin.Magic(header, 0, "GIF8") || (header.Length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255);
        }
        static void ParseSource(Source source, ScanResult scanResult, CancellationToken cancellationToken)
        {
            Bin.Need(source.Length <= Bin.MaxBytes, "항목당 128 MiB 한도 초과");
            var header = Bin.Read(source.Path, source.Offset, Math.Min(16, source.Length));
            source.Lzp = Bin.Magic(header, 0, "LZP2");
            if (!source.Lzp && !Formats.Readers.Any(textureReader => textureReader.Matches(header)))
            {
                scanResult.Items.Add(Unknown(source, "지원 텍스처 시그니처 없음"));
                return;
            }
            byte[] sourceData = source.Read(cancellationToken);
            var textures = Formats.Parse(source, sourceData);
            scanResult.Items.AddRange(textures);
        }
        static void Deep(string path, ScanResult result, CancellationToken cancellationToken, Action<string> reportProgress)
        {
            // Fixed-size windows and a three-byte overlap: no whole-archive allocation.
            const int chunkByteCount = 4 * 1024 * 1024;
            int candidateCount = 0;
            long nextCandidateOffset = 1;
            using (var fileStream = File.OpenRead(path))
            {
                byte[] scanBuffer = new byte[chunkByteCount + 3];
                long chunkOffset = 0;
                while (chunkOffset < fileStream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fileStream.Position = chunkOffset;
                    int bytesRead = fileStream.Read(scanBuffer, 0, (int)Math.Min(scanBuffer.Length, fileStream.Length - chunkOffset));
                    reportProgress("내부 검색 · " + System.IO.Path.GetFileName(path) + " · " + (chunkOffset * 100 / Math.Max(1, fileStream.Length)) + "%");
                    for (int bufferIndex = 0; bufferIndex < bytesRead - 3; bufferIndex++)
                    {
                        if ((bufferIndex & 65535) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        long candidateOffset = chunkOffset + bufferIndex;
                        if (candidateOffset < nextCandidateOffset)
                        {
                            continue;
                        }

                        byte signatureFirstByte = scanBuffer[bufferIndex];
                        bool signatureMatched = (signatureFirstByte == 68 && scanBuffer[bufferIndex + 1] == 68 && scanBuffer[bufferIndex + 2] == 83 && scanBuffer[bufferIndex + 3] == 32) || (signatureFirstByte == 71 && scanBuffer[bufferIndex + 1] == 49 && scanBuffer[bufferIndex + 2] == 84 && scanBuffer[bufferIndex + 3] == 71) || (signatureFirstByte == 2 && scanBuffer[bufferIndex + 3] == 255 && scanBuffer[bufferIndex + 1] <= 2 && scanBuffer[bufferIndex + 2] == 0);
                        if (!signatureMatched)
                        {
                            continue;
                        }

                        if (++candidateCount > 20000)
                        {
                            result.Messages.Add(path + ": 내부 검색 후보 20,000개 한도 도달");
                            return;
                        }
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var header = Bin.Read(path, candidateOffset, (int)Math.Min(128, fileStream.Length - candidateOffset));
                            long candidateLength = 0;
                            if (Bin.Magic(header, 0, "G1TG"))
                            {
                                candidateLength = Bin.U32(header, 8);
                            }
                            else if (Bin.Magic(header, 0, "DDS "))
                            {
                                int width = Bin.I32(header, 16, false), height = Bin.I32(header, 12, false);
                                Bin.Need(width > 0 && height > 0 && width <= 8192 && height <= 8192, "크기 오류");
                                candidateLength = Math.Min(fileStream.Length - candidateOffset, 128L + (long)width * height * 4 + 4096);
                            }
                            else
                            {
                                int count = Bin.I32(header, 8);
                                Bin.Need(count > 0 && count <= 4096, "항목 수 오류");
                                var index = Bin.Read(path, candidateOffset, 12 + count * 36);
                                for (int textureIndex = 0; textureIndex < count; textureIndex++)
                                {
                                    candidateLength = Math.Max(candidateLength, (long)Bin.U32(index, 16 + textureIndex * 36) + Bin.U32(index, 20 + textureIndex * 36));
                                }
                            }
                            Bin.Need(candidateLength >= 32 && candidateLength <= Bin.MaxBytes && candidateOffset <= fileStream.Length - candidateLength, "내부 후보 범위 오류");
                            var source = new Source { Path = path, Offset = candidateOffset, Length = (int)candidateLength, Label = System.IO.Path.GetFileName(path) + " @0x" + candidateOffset.ToString("X") };
                            var textures = Formats.Parse(source, source.Read(cancellationToken));
                            if (textures.Any(texture => texture.Supported))
                            {
                                result.Items.AddRange(textures);
                                nextCandidateOffset = candidateOffset + 4;
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) {/* A signature is only a candidate until structural validation succeeds. */}
                    }
                    if (bytesRead <= 3)
                    {
                        break;
                    }

                    chunkOffset += chunkByteCount;
                }
            }
        }
        static string ReadSfo(byte[] sfoData)
        {
            Bin.Need(Bin.Magic(sfoData, 0, "\0PSF"), "SFO 시그니처 오류");
            int keyTableOffset = Bin.I32(sfoData, 8, false), valueTableOffset = Bin.I32(sfoData, 12, false), entryCount = Bin.I32(sfoData, 16, false);
            Bin.Need(entryCount <= 4096, "SFO 항목 수 오류");
            var fields = new Dictionary<string, string>();
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                int entryOffset = 20 + entryIndex * 16;
                Bin.Range(sfoData, entryOffset, 16);
                int keyOffset = keyTableOffset + Bin.U16(sfoData, entryOffset, false), valueOffset = checked(valueTableOffset + Bin.I32(sfoData, entryOffset + 12, false)), valueLength = Bin.I32(sfoData, entryOffset + 4, false);
                Bin.Range(sfoData, keyOffset, 1);
                int keyEndOffset = Array.IndexOf(sfoData, (byte)0, keyOffset);
                Bin.Need(keyEndOffset >= keyOffset, "SFO 키 종료 오류");
                string name = Encoding.UTF8.GetString(sfoData, keyOffset, keyEndOffset - keyOffset);
                Bin.Range(sfoData, valueOffset, valueLength);
                if (name == "TITLE" || name == "TITLE_ID" || name == "APP_VER")
                {
                    fields[name] = Encoding.UTF8.GetString(sfoData, valueOffset, valueLength).TrimEnd('\0');
                }
            }
            return string.Join(" · ", new[] { "TITLE", "TITLE_ID", "APP_VER" }.Where(fields.ContainsKey).Select(fieldName => fields[fieldName]).ToArray());
        }
    }
}
