using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace ReplayTimerMod
{
    internal static class Compress
    {
        // Inflate ceiling — guards against a decompression bomb in shared replays.
        private const long MaxDecompressedBytes = 64L * 1024 * 1024;

        internal static byte[] CompressData(byte[] data)
        {
            using var ms = new MemoryStream();
            // BEST_COMPRESSION to match the Silksong build's
            // CompressionLevel.Optimal — identical runs should compress
            // comparably on every platform.
            using (var ds = new DeflaterOutputStream(ms, new Deflater(Deflater.BEST_COMPRESSION, true)))
            {
                ds.Write(data, 0, data.Length);
                ds.Finish();
            }
            return ms.ToArray();
        }

        internal static byte[] DecompressData(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var inf = new InflaterInputStream(ms, new Inflater(true));
            using var output = new MemoryStream();
            var buf = new byte[8192];
            int n;
            while ((n = inf.Read(buf, 0, buf.Length)) > 0)
            {
                // IOException, NOT InvalidDataException: the latter is missing
                // from HK 1.2.2.1's old Mono, and merely referencing it makes
                // the JIT throw a TypeLoadException the first time this method
                // runs - every replay decode "fails" and all saved PBs load as
                // corrupt on that platform.
                if (output.Length + n > MaxDecompressedBytes)
                    throw new IOException("Decompressed replay exceeds maximum allowed size");
                output.Write(buf, 0, n);
            }
            return output.ToArray();
        }
    }
}

