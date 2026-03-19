using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace ReplayTimerMod
{
    internal static class Compress
    {
        internal static byte[] CompressData(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflaterOutputStream(ms, new Deflater(Deflater.DEFAULT_COMPRESSION)))
            {
                ds.Write(data, 0, data.Length);
                ds.Finish();
            }
            return ms.ToArray();
        }

        internal static byte[] DecompressData(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var inf = new InflaterInputStream(ms);
            using var output = new MemoryStream();
            var buf = new byte[4096];
            int n;
            while ((n = inf.Read(buf, 0, buf.Length)) > 0)
                output.Write(buf, 0, n);
            return output.ToArray();
        }
    }
}

