using System;
using System.IO;
using System.IO.Compression;

namespace ReplayTimerMod
{
    internal static class Compress
    {
        // Inflate ceiling — guards against a decompression bomb in shared replays.
        private const long MaxDecompressedBytes = 64L * 1024 * 1024;

       internal static byte[] CompressData(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var df = new DeflateStream(ms, CompressionLevel.Optimal))
                df.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        internal static byte[] DecompressData(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            using (var df = new DeflateStream(input, CompressionMode.Decompress))
            {
                var buf = new byte[8192];
                int n;
                while ((n = df.Read(buf, 0, buf.Length)) > 0)
                {
                    if (output.Length + n > MaxDecompressedBytes)
                        throw new InvalidDataException("Decompressed replay exceeds maximum allowed size");
                    output.Write(buf, 0, n);
                }
            }
            return output.ToArray();
        }
    }
}

