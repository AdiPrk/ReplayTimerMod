using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    internal static class ReeseImages
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReeseImages");

        private const string ResourcePrefix = "Reese.";
        private const float WorldHeight = 2f;

        private static List<Sprite>? _sprites;

        public static Sprite? Random()
        {
            if (_sprites == null)
                _sprites = Load();
            return _sprites.Count == 0 ? null : _sprites[UnityEngine.Random.Range(0, _sprites.Count)];
        }

        private static List<Sprite> Load()
        {
            var sprites = new List<Sprite>();
            var asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix))
                    continue;
                try
                {
                    var tex = new Texture2D(2, 2) { filterMode = FilterMode.Trilinear };
                    if (!tex.LoadImage(ReadAll(asm.GetManifestResourceStream(name)), true))
                    {
                        Object.Destroy(tex);
                        continue;
                    }
                    sprites.Add(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), tex.height / WorldHeight));
                }
                catch (System.Exception ex)
                {
                    Log.LogWarning($"[ReeseImages] Failed to load {name}: {ex.Message}");
                }
            }
            Log.LogInfo($"[ReeseImages] Loaded {sprites.Count} image(s)");
            return sprites;
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (stream)
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                return ms.ToArray();
            }
        }
    }
}
