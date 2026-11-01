using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace AtlasView
{
    public static class Bin
    {
        public const int MaxBytes = 128 * 1024 * 1024;
        public static void Need(bool conditionSatisfied, string message)
        {
            if (!conditionSatisfied)
            {
                throw new InvalidDataException(message);
            }
        }
        public static uint U32(byte[] data, int offset, bool bigEndian = true)
        {
            Range(data, offset, 4);
            return bigEndian ? ((uint)data[offset] << 24 | (uint)data[offset + 1] << 16 | (uint)data[offset + 2] << 8 | data[offset + 3]) : ((uint)data[offset + 3] << 24 | (uint)data[offset + 2] << 16 | (uint)data[offset + 1] << 8 | data[offset]);
        }
        public static int I32(byte[] data, int offset, bool bigEndian = true)
        {
            uint unsignedValue = U32(data, offset, bigEndian);
            Need(unsignedValue <= int.MaxValue, "정수 범위 초과");
            return (int)unsignedValue;
        }
        public static int U16(byte[] data, int offset, bool bigEndian = true)
        {
            Range(data, offset, 2);
            return bigEndian ? data[offset] * 256 + data[offset + 1] : data[offset] + data[offset + 1] * 256;
        }
        public static void Range(byte[] data, int offset, int length)
        {
            Need(offset >= 0 && length >= 0 && (long)offset + length <= data.Length, "데이터 범위를 벗어났습니다.");
        }
        public static bool Magic(byte[] data, int offset, string signature)
        {
            if (offset < 0 || offset + signature.Length > data.Length)
            {
                return false;
            }

            for (int signatureIndex = 0; signatureIndex < signature.Length; signatureIndex++)
            {
                if (data[offset + signatureIndex] != (byte)signature[signatureIndex])
                {
                    return false;
                }
            }

            return true;
        }
        public static byte[] Read(string path, long offset, int length)
        {
            return Read(path, offset, length, CancellationToken.None);
        }
        public static byte[] Read(string path, long offset, int length, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Need(length >= 0 && length <= MaxBytes, "읽기 한도 128 MiB 초과");
            using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Need(offset >= 0 && offset <= fileStream.Length - length, "파일 범위 초과");
                fileStream.Position = offset;
                var buffer = new byte[length];
                int totalBytesRead = 0;
                while (totalBytesRead < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesRead = fileStream.Read(buffer, totalBytesRead, Math.Min(1024 * 1024, length - totalBytesRead));
                    if (bytesRead == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    totalBytesRead += bytesRead;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return buffer;
            }
        }
        public static string Hash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
        }
        public static string TextHash(string text)
        {
            return Hash(System.Text.Encoding.UTF8.GetBytes(text));
        }
        public static string FileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var fileStream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public sealed class Source
    {
        public string Path, Label;
        public long Offset;
        public int Length;
        public bool Lzp;
        public byte[] Read(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = Bin.Read(Path, Offset, Length, cancellationToken);
            return Lzp ? Lzp2.Decode(data, cancellationToken) : data;
        }
    }
    public sealed class Texture
    {
        public Source Source;
        public string Name, Container, Format, Issue = "", Note = "";
        public int Width, Height, DataOffset, DataLength, Pitch, BytesPerPixel;
        public bool Swizzle, BigEndian, StandardImage;
        public uint RMask, GMask, BMask, AMask;
        public bool Supported
        {
            get
            {
                return Issue.Length == 0;
            }
        }
        public string Locator
        {
            get
            {
                return Source.Label + " / " + Name;
            }
        }
        public Texture Copy()
        {
            return (Texture)MemberwiseClone();
        }
    }

    public static class Lzp2
    {
        public static byte[] Decode(byte[] compressedData, CancellationToken cancellationToken)
        {
            Bin.Need(compressedData.Length >= 16 && Bin.Magic(compressedData, 0, "LZP2") && Bin.U32(compressedData, 4) == 0x5c8f823f, "미지원 LZP2 프로필");
            int decompressedSize = Bin.I32(compressedData, 8, false);
            Bin.Need(decompressedSize <= 64 * 1024 * 1024 && Bin.U32(compressedData, 12, false) + 16L == compressedData.Length && compressedData.Length % 16 == 0, "LZP2 길이 오류");
            var decompressedData = new byte[decompressedSize];
            int inputOffset = 16, outputOffset = 0, commandCount = 0;
            while (outputOffset < decompressedSize)
            {
                if ((commandCount++ & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                Bin.Range(compressedData, inputOffset, 1);
                int command = compressedData[inputOffset++], runLength;
                if (command < 64)
                {
                    runLength = command;
                    Bin.Range(compressedData, inputOffset, runLength);
                    Bin.Need(outputOffset + runLength <= decompressedSize, "LZP2 리터럴 범위 오류");
                    Buffer.BlockCopy(compressedData, inputOffset, decompressedData, outputOffset, runLength);
                    inputOffset += runLength;
                    outputOffset += runLength;
                }
                else if (command < 128)
                {
                    Bin.Range(compressedData, inputOffset, 2);
                    runLength = ((command & 63) << 8) + compressedData[inputOffset++] + 4;
                    byte repeatedByte = compressedData[inputOffset++];
                    Bin.Need(outputOffset + runLength <= decompressedSize, "LZP2 반복 범위 오류");
                    for (int copyIndex = 0; copyIndex < runLength; copyIndex++)
                    {
                        decompressedData[outputOffset++] = repeatedByte;
                    }
                }
                else
                {
                    Bin.Range(compressedData, inputOffset, 1);
                    int distance = ((command & 7) << 8) + compressedData[inputOffset++] + 1;
                    runLength = ((command >> 3) & 15) + 3;
                    Bin.Need(distance <= outputOffset && outputOffset + runLength <= decompressedSize, "LZP2 참조 범위 오류");
                    for (int copyIndex = 0; copyIndex < runLength; copyIndex++, outputOffset++)
                    {
                        decompressedData[outputOffset] = decompressedData[outputOffset - distance];
                    }
                }
            }
            Bin.Need(compressedData.Length - inputOffset >= 1 && compressedData.Length - inputOffset <= 16, "LZP2 패딩 길이 오류");
            for (; inputOffset < compressedData.Length; inputOffset++)
            {
                Bin.Need(compressedData[inputOffset] == 0, "LZP2 패딩 오류");
            }

            return decompressedData;
        }
    }

    public static class Pixels
    {
        public static int ByteCount(Texture texture)
        {
            Bin.Need(texture.Width > 0 && texture.Height > 0 && texture.Width <= 8192 && texture.Height <= 8192 && (long)texture.Width * texture.Height <= 16777216, "이미지 한도: 최대 변 8192 / 16M 픽셀");
            int bytesPerPixel = texture.BytesPerPixel;
            if (texture.Format == "BC1" || texture.Format == "BC2" || texture.Format == "BC3")
            {
                return checked(((texture.Width + 3) / 4) * ((texture.Height + 3) / 4) * (texture.Format == "BC1" ? 8 : 16));
            }

            Bin.Need(bytesPerPixel >= 1 && bytesPerPixel <= 4, "미지원 픽셀 크기");
            int pitch = texture.Pitch == 0 ? texture.Width * bytesPerPixel : texture.Pitch;
            Bin.Need(pitch >= texture.Width * bytesPerPixel, "피치가 행 길이보다 작습니다.");
            if (texture.Swizzle)
            {
                Bin.Need((texture.Width & (texture.Width - 1)) == 0 && (texture.Height & (texture.Height - 1)) == 0, "Morton은 2의 거듭제곱 크기만 지원");
                return checked(texture.Width * texture.Height * bytesPerPixel);
            }
            return checked(pitch * (texture.Height - 1) + texture.Width * bytesPerPixel);
        }
        public static int Morton(int x, int y, int width, int height)
        {
            int address = 0, outputBitIndex = 0;
            for (int mask = 1; mask < width || mask < height; mask <<= 1)
            {
                if (mask < width)
                {
                    if ((x & mask) != 0)
                    {
                        address |= 1 << outputBitIndex;
                    }

                    outputBitIndex++;
                }
                if (mask < height)
                {
                    if ((y & mask) != 0)
                    {
                        address |= 1 << outputBitIndex;
                    }

                    outputBitIndex++;
                }
            }
            return address;
        }
        static int Channel(uint pixelValue, uint mask, int fallback)
        {
            if (mask == 0)
            {
                return fallback;
            }

            int bitShift = 0;
            while ((mask & 1) == 0)
            {
                bitShift++;
                mask >>= 1;
            }
            return (int)(((pixelValue >> bitShift) & mask) * 255UL / mask);
        }
        static int[] Rgb565(int packedColor)
        {
            int red = packedColor >> 11, green = (packedColor >> 5) & 63, blue = packedColor & 31;
            return new[] { (red << 3) | (red >> 2), (green << 2) | (green >> 4), (blue << 3) | (blue >> 2), 255 };
        }
        public static byte[] Decode(byte[] textureData, Texture texture, CancellationToken cancellationToken)
        {
            int requiredByteCount = ByteCount(texture);
            Bin.Range(textureData, texture.DataOffset, requiredByteCount);
            if (texture.DataLength > 0)
            {
                Bin.Need(requiredByteCount <= texture.DataLength, "텍스처 선언 범위보다 픽셀 데이터가 큽니다.");
            }

            var bgraPixels = new byte[texture.Width * texture.Height * 4];
            if (texture.Format == "BC1" || texture.Format == "BC2" || texture.Format == "BC3")
            {
                Bin.Need(!texture.Swizzle, "블록 Morton은 자동 적용하지 않습니다.");
                int blocksPerRow = (texture.Width + 3) / 4, blockByteCount = texture.Format == "BC1" ? 8 : 16;
                for (int blockY = 0; blockY < (texture.Height + 3) / 4; blockY++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int blockX = 0; blockX < blocksPerRow; blockX++)
                    {
                        int blockOffset = texture.DataOffset + (blockY * blocksPerRow + blockX) * blockByteCount, colorBlockOffset = blockOffset + (blockByteCount == 8 ? 0 : 8);
                        int firstColorEndpoint = Bin.U16(textureData, colorBlockOffset, false), secondColorEndpoint = Bin.U16(textureData, colorBlockOffset + 2, false);
                        int[][] colorPalette = { Rgb565(firstColorEndpoint), Rgb565(secondColorEndpoint), new int[4], new int[4] };
                        for (int channelIndex = 0; channelIndex < 3; channelIndex++)
                        {
                            if (firstColorEndpoint > secondColorEndpoint || blockByteCount == 16)
                            {
                                colorPalette[2][channelIndex] = (2 * colorPalette[0][channelIndex] + colorPalette[1][channelIndex]) / 3;
                                colorPalette[3][channelIndex] = (colorPalette[0][channelIndex] + 2 * colorPalette[1][channelIndex]) / 3;
                            }
                            else
                            {
                                colorPalette[2][channelIndex] = (colorPalette[0][channelIndex] + colorPalette[1][channelIndex]) / 2;
                            }
                        }
                        colorPalette[2][3] = 255;
                        colorPalette[3][3] = (firstColorEndpoint > secondColorEndpoint || blockByteCount == 16) ? 255 : 0;
                        uint colorIndices = Bin.U32(textureData, colorBlockOffset + 4, false);
                        int[] alphaPalette = new int[8];
                        ulong alphaIndices = 0;
                        if (texture.Format == "BC3")
                        {
                            alphaPalette[0] = textureData[blockOffset];
                            alphaPalette[1] = textureData[blockOffset + 1];
                            if (alphaPalette[0] > alphaPalette[1])
                            {
                                for (int alphaIndex = 2; alphaIndex < 8; alphaIndex++)
                                {
                                    alphaPalette[alphaIndex] = ((8 - alphaIndex) * alphaPalette[0] + (alphaIndex - 1) * alphaPalette[1]) / 7;
                                }
                            }
                            else
                            {
                                for (int alphaIndex = 2; alphaIndex < 6; alphaIndex++)
                                {
                                    alphaPalette[alphaIndex] = ((6 - alphaIndex) * alphaPalette[0] + (alphaIndex - 1) * alphaPalette[1]) / 5;
                                }

                                alphaPalette[6] = 0;
                                alphaPalette[7] = 255;
                            }
                            for (int alphaByteIndex = 0; alphaByteIndex < 6; alphaByteIndex++)
                            {
                                alphaIndices |= (ulong)textureData[blockOffset + 2 + alphaByteIndex] << (8 * alphaByteIndex);
                            }
                        }
                        for (int blockPixelIndex = 0; blockPixelIndex < 16; blockPixelIndex++)
                        {
                            int x = blockX * 4 + blockPixelIndex % 4, y = blockY * 4 + blockPixelIndex / 4;
                            if (x >= texture.Width || y >= texture.Height)
                            {
                                continue;
                            }

                            int[] colorChannels = colorPalette[(colorIndices >> (2 * blockPixelIndex)) & 3];
                            int alphaValue = colorChannels[3];
                            if (texture.Format == "BC2")
                            {
                                alphaValue = ((textureData[blockOffset + blockPixelIndex / 2] >> ((blockPixelIndex % 2) * 4)) & 15) * 17;
                            }
                            else if (texture.Format == "BC3")
                            {
                                alphaValue = alphaPalette[(alphaIndices >> (3 * blockPixelIndex)) & 7];
                            }

                            int outputOffset = (y * texture.Width + x) * 4;
                            bgraPixels[outputOffset] = (byte)colorChannels[2];
                            bgraPixels[outputOffset + 1] = (byte)colorChannels[1];
                            bgraPixels[outputOffset + 2] = (byte)colorChannels[0];
                            bgraPixels[outputOffset + 3] = (byte)alphaValue;
                        }
                    }
                }
                return bgraPixels;
            }
            int rowPitch = texture.Pitch == 0 ? texture.Width * texture.BytesPerPixel : texture.Pitch;
            for (int y = 0; y < texture.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < texture.Width; x++)
                {
                    int pixelOffset = texture.DataOffset + (texture.Swizzle ? Morton(x, y, texture.Width, texture.Height) * texture.BytesPerPixel : y * rowPitch + x * texture.BytesPerPixel), outputOffset = (y * texture.Width + x) * 4;
                    int red = 0, green = 0, blue = 0, alphaValue = 255;
                    if (texture.Format == "L8" || texture.Format == "A8")
                    {
                        red = green = blue = texture.Format == "A8" ? 255 : textureData[pixelOffset];
                        if (texture.Format == "A8")
                        {
                            alphaValue = textureData[pixelOffset];
                        }
                    }
                    else if (texture.Format == "RGBA8")
                    {
                        red = textureData[pixelOffset];
                        green = textureData[pixelOffset + 1];
                        blue = textureData[pixelOffset + 2];
                        alphaValue = textureData[pixelOffset + 3];
                    }
                    else if (texture.Format == "BGRA8")
                    {
                        blue = textureData[pixelOffset];
                        green = textureData[pixelOffset + 1];
                        red = textureData[pixelOffset + 2];
                        alphaValue = textureData[pixelOffset + 3];
                    }
                    else if (texture.Format == "ARGB8")
                    {
                        alphaValue = textureData[pixelOffset];
                        red = textureData[pixelOffset + 1];
                        green = textureData[pixelOffset + 2];
                        blue = textureData[pixelOffset + 3];
                    }
                    else if (texture.Format == "MASK")
                    {
                        uint packedPixel = 0;
                        for (int byteIndex = 0; byteIndex < texture.BytesPerPixel; byteIndex++)
                        {
                            packedPixel |= (uint)textureData[pixelOffset + byteIndex] << ((texture.BigEndian ? texture.BytesPerPixel - 1 - byteIndex : byteIndex) * 8);
                        }

                        red = Channel(packedPixel, texture.RMask, 0);
                        green = Channel(packedPixel, texture.GMask, 0);
                        blue = Channel(packedPixel, texture.BMask, 0);
                        alphaValue = Channel(packedPixel, texture.AMask, 255);
                    }
                    else
                    {
                        throw new InvalidDataException("미지원 픽셀 포맷: " + texture.Format);
                    }

                    bgraPixels[outputOffset] = (byte)blue;
                    bgraPixels[outputOffset + 1] = (byte)green;
                    bgraPixels[outputOffset + 2] = (byte)red;
                    bgraPixels[outputOffset + 3] = (byte)alphaValue;
                }
            }
            return bgraPixels;
        }
        public static Bitmap Bitmap(byte[] pixels, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * width * 4, IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), width * 4);
                }
            }
            finally { bitmap.UnlockBits(bitmapData); }
            return bitmap;
        }
        public static byte[] Bgra(Bitmap bitmap)
        {
            var bgraPixels = new byte[bitmap.Width * bitmap.Height * 4];
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), bgraPixels, y * bitmap.Width * 4, bitmap.Width * 4);
                }
            }
            finally { bitmap.UnlockBits(bitmapData); }
            return bgraPixels;
        }
        public static Bitmap Load(Texture texture, CancellationToken cancellationToken)
        {
            Bin.Need(texture.Supported, texture.Issue);
            byte[] textureData;
            if (!texture.StandardImage && !texture.Source.Lzp)
            {
                int size = ByteCount(texture);
                Bin.Need(texture.DataOffset >= 0 && (long)texture.DataOffset + size <= texture.Source.Length, "소스 범위 초과");
                textureData = Bin.Read(texture.Source.Path, checked(texture.Source.Offset + texture.DataOffset), size, cancellationToken);
                texture = texture.Copy();
                texture.DataOffset = 0;
            }
            else
            {
                textureData = texture.Source.Read(cancellationToken);
            }

            if (!texture.StandardImage)
            {
                return Bitmap(Decode(textureData, texture, cancellationToken), texture.Width, texture.Height);
            }

            using (var memoryStream = new MemoryStream(textureData))
            using (var image = Image.FromStream(memoryStream, false, true))
            {
                Bin.Need((long)image.Width * image.Height <= 16777216, "이미지 픽셀 한도 초과");
                return ((Bitmap)image).Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
            }
        }
    }
}
