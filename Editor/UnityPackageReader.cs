using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WXGameMaker.TapAdn.Editor
{
    public static class UnityPackageReader
    {
        public static void Verify(byte[] data)
        {
            using (var md5 = MD5.Create())
            using (var sha = SHA256.Create())
                if (Hex(md5.ComputeHash(data)) != SdkLock.Md5 || Hex(sha.ComputeHash(data)) != SdkLock.Sha256)
                    throw new InvalidDataException("Official TapADN SDK checksum mismatch; installation was not changed.");
        }
        public static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();

        // Reads regular UnityPackage tar entries only; never extracts arbitrary archive paths.
        public static Dictionary<string, byte[]> Read(byte[] data)
        {
            var entries = new Dictionary<string, byte[]>();
            using (var compressed = new MemoryStream(data))
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            {
                var header = new byte[512];
                long total = 0;
                while (ReadBlock(gzip, header))
                {
                    if (header[0] == 0) break;
                    var name = Text(header, 0, 100);
                    if (name.StartsWith("/") || name.Contains("..") || name.Contains("\\"))
                        throw new InvalidDataException("Unsafe archive entry.");
                    var lengthText = Text(header, 124, 12).Trim();
                    var length = string.IsNullOrEmpty(lengthText) ? 0 : Convert.ToInt64(lengthText, 8);
                    if (length < 0 || length > 32 * 1024 * 1024 || (total += length) > 64 * 1024 * 1024)
                        throw new InvalidDataException("SDK archive exceeds extraction limits.");
                    var bytes = new byte[(int)length];
                    ReadExactly(gzip, bytes, bytes.Length);
                    var padding = (int)((512 - length % 512) % 512);
                    if (padding > 0) ReadExactly(gzip, new byte[padding], padding);
                    if (header[156] == 0 || header[156] == (byte)'0')
                    {
                        if (entries.ContainsKey(name)) throw new InvalidDataException("Duplicate archive entry.");
                        entries[name] = bytes;
                    }
                }
            }
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!entry.Key.EndsWith("/pathname", StringComparison.Ordinal)) continue;
                var assetPath = Encoding.UTF8.GetString(entry.Value).TrimEnd('\0', '\r', '\n');
                if (assetPath.Contains("..") || assetPath.StartsWith("/") || assetPath.Contains("\\"))
                    throw new InvalidDataException("Unsafe SDK asset pathname.");
                if (entries.TryGetValue(entry.Key.Substring(0, entry.Key.Length - 8) + "asset", out var bytes))
                    assets.Add(assetPath, bytes);
            }
            return assets;
        }
        private static string Text(byte[] data, int offset, int count) => Encoding.ASCII.GetString(data, offset, count).TrimEnd('\0', ' ');
        private static bool ReadBlock(Stream stream, byte[] buffer)
        {
            var first = stream.ReadByte();
            if (first < 0) return false;
            buffer[0] = (byte)first;
            var rest = new byte[buffer.Length - 1]; ReadExactly(stream, rest, rest.Length);
            Buffer.BlockCopy(rest, 0, buffer, 1, rest.Length); return true;
        }
        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            { int read = stream.Read(buffer, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        }
    }
}
