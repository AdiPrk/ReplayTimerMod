using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ReplayTimerMod
{
    // Encodes and decodes RecordedRoom to/from a compact shareable string.
    //
    // Format: Base64( GZip( JSON( SerializedEntry ) ) )
    //
    // For a 30s room at 30fps that's ~900 frames → ~7KB JSON → ~2KB after
    // GZip → ~2.7KB base64 string. Easily fits in a chat message.
    public static class ReplayShareEncoder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ShareEncoder");

        public static string Encode(RecordedRoom room)
        {
            int n = room.FrameCount;

            var entry = new SerializedEntry
            {
                sceneName = room.Key.SceneName,
                entryGate = room.Key.EntryGate,
                exitToScene = room.Key.ExitToScene,
                totalTime = room.TotalTime,
                frames = new SerializedFrame
                {
                    x = new float[n],
                    y = new float[n],
                    facing = new byte[n]
                }
            };

            for (int i = 0; i < n; i++)
            {
                entry.frames.x[i] = room.Frames[i].x;
                entry.frames.y[i] = room.Frames[i].y;
                entry.frames.facing[i] = room.Frames[i].facingRight ? (byte)1 : (byte)0;
            }

            string json = JsonConvert.SerializeObject(entry);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] compressed = Compress(jsonBytes);
            return Convert.ToBase64String(compressed);
        }

        public static RecordedRoom? Decode(string encoded)
        {
            try
            {
                byte[] compressed = Convert.FromBase64String(encoded);
                byte[] jsonBytes = Decompress(compressed);
                string json = Encoding.UTF8.GetString(jsonBytes);

                var entry = JsonConvert.DeserializeObject<SerializedEntry>(json);
                if (entry == null) return null;

                int n = entry.frames.x?.Length ?? 0;
                var frames = new FrameData[n];
                for (int i = 0; i < n; i++)
                {
                    frames[i] = new FrameData
                    {
                        x = entry.frames.x[i],
                        y = entry.frames.y[i],
                        deltaTime = FrameRecorder.RECORD_INTERVAL,
                        facingRight = entry.frames.facing[i] != 0
                    };
                }

                return new RecordedRoom(
                    new RoomKey(entry.sceneName, entry.entryGate, entry.exitToScene),
                    entry.totalTime, frames);
            }
            catch (Exception e)
            {
                Log.LogError($"[ShareEncoder] Decode failed: {e.Message}");
                return null;
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionMode.Compress, true))
                gz.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
                gz.CopyTo(output);
            return output.ToArray();
        }
    }
}