using System;
using System.IO;
using System.IO.Compression;

namespace ReplayTimerMod
{
    internal static class Compress
    {
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
                df.CopyTo(output);
            return output.ToArray();
        }
    }
}

