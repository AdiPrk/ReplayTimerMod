using System.Collections.Generic;

namespace ReplayTimerMod
{
    // ── Leaderboard data types ─────────────────────────────────────────

    /// <summary>
    /// Parsed response from GET /leaderboard. Contains all routes for a room.
    /// </summary>
    public sealed class LeaderboardData
    {
        public List<RouteLeaderboard> Routes = new List<RouteLeaderboard>();
    }

    /// <summary>
    /// Leaderboard for a single route (entry → exit) within a room.
    /// </summary>
    public sealed class RouteLeaderboard
    {
        public string EntryFrom = "";
        public string ExitTo = "";
        public int TotalRunners;
        public int YourRank = -1;                         // -1 if you have no entry
        public List<LeaderboardEntry> Entries = new List<LeaderboardEntry>();
        public LeaderboardEntry? YourEntry;                // non-null if your rank > top N
    }

    /// <summary>
    /// A single entry (one runner's best time) in a route leaderboard.
    /// </summary>
    public sealed class LeaderboardEntry
    {
        public int Rank;
        public string RunnerName = "";
        public float TotalTime;
        public string RunId = "";
        public bool IsYou;
    }

    // ── Leaderboard cache ──────────────────────────────────────────────

    /// <summary>
    /// In-memory cache for leaderboard data, keyed by "game:scene".
    ///
    /// Change detection: every entry carries a content signature. Updates
    /// that don't change the data don't bump the version, so the UI can
    /// skip rebuilds for identical polling responses. This is what keeps
    /// hover states and scroll position alive while the 5-second poll and
    /// 60-second manifest refresh run in the background.
    /// </summary>
    public sealed class LeaderboardCache
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, LeaderboardData> _cache =
            new Dictionary<string, LeaderboardData>();
        private readonly Dictionary<string, int> _signatures =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> _versions =
            new Dictionary<string, int>();

        /// <summary>
        /// All scene names the server has leaderboard data for.
        /// Populated by UpdateFromManifest.
        /// </summary>
        private readonly HashSet<string> _serverScenes = new HashSet<string>();

        /// <summary>
        /// Bumped only when the server scene SET changes (rooms added or
        /// removed), not on every manifest refresh. The scene list uses
        /// this to skip pointless rebuilds.
        /// </summary>
        private int _serverScenesVersion;

        private bool _manifestLoaded;

        public bool ManifestLoaded
        {
            get { lock (_lock) { return _manifestLoaded; } }
        }

        public int ServerScenesVersion
        {
            get { lock (_lock) { return _serverScenesVersion; } }
        }

        public LeaderboardData? Get(string game, string scene)
        {
            string key = game + ":" + scene;
            lock (_lock)
            {
                return _cache.TryGetValue(key, out var data) ? data : null;
            }
        }

        /// <summary>
        /// Monotonic version for a room's data. Bumps only when the content
        /// actually changes. Returns 0 for rooms never cached.
        /// </summary>
        public int GetVersion(string game, string scene)
        {
            string key = game + ":" + scene;
            lock (_lock)
            {
                return _versions.TryGetValue(key, out var v) ? v : 0;
            }
        }

        /// <summary>
        /// Updates a single room's data. Returns true if the content
        /// actually changed (version bumped), false if identical.
        /// </summary>
        public bool Update(string game, string scene, LeaderboardData data)
        {
            string key = game + ":" + scene;
            int sig = ComputeSignature(data);

            lock (_lock)
            {
                _cache[key] = data;   // always keep freshest object

                if (_signatures.TryGetValue(key, out var oldSig) && oldSig == sig)
                    return false;     // identical content — no version bump

                _signatures[key] = sig;
                _versions[key] = (_versions.TryGetValue(key, out var v) ? v : 0) + 1;
                return true;
            }
        }

        /// <summary>
        /// Bulk-updates the cache from a parsed manifest response.
        /// Per-room versions only bump for rooms whose content changed.
        /// ServerScenesVersion only bumps if the scene set changed.
        /// Returns true if anything at all changed.
        /// </summary>
        public bool UpdateFromManifest(string game,
            Dictionary<string, LeaderboardData> manifest)
        {
            bool anyChanged = false;

            lock (_lock)
            {
                // Detect scene-set changes
                bool setChanged = _serverScenes.Count != manifest.Count;
                if (!setChanged)
                {
                    foreach (var scene in manifest.Keys)
                    {
                        if (!_serverScenes.Contains(scene)) { setChanged = true; break; }
                    }
                }

                if (setChanged)
                {
                    _serverScenes.Clear();
                    foreach (var scene in manifest.Keys)
                        _serverScenes.Add(scene);
                    _serverScenesVersion++;
                    anyChanged = true;
                }

                // Per-room content updates with signature gating
                foreach (var kvp in manifest)
                {
                    string key = game + ":" + kvp.Key;
                    int sig = ComputeSignature(kvp.Value);

                    _cache[key] = kvp.Value;

                    if (_signatures.TryGetValue(key, out var oldSig) && oldSig == sig)
                        continue;

                    _signatures[key] = sig;
                    _versions[key] = (_versions.TryGetValue(key, out var v) ? v : 0) + 1;
                    anyChanged = true;
                }

                _manifestLoaded = true;
            }

            return anyChanged;
        }

        /// <summary>
        /// Returns a snapshot of all scene names known to the server.
        /// Thread-safe copy — caller owns the returned set.
        /// </summary>
        public HashSet<string> GetServerScenes()
        {
            lock (_lock)
            {
                return new HashSet<string>(_serverScenes);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _signatures.Clear();
                _versions.Clear();
                _serverScenes.Clear();
                _serverScenesVersion++;
                _manifestLoaded = false;
            }
        }

        // ── Signature ──────────────────────────────────────────────────

        /// <summary>
        /// Cheap order-sensitive hash over everything the UI renders.
        /// Two responses with the same signature produce identical UI.
        /// </summary>
        private static int ComputeSignature(LeaderboardData data)
        {
            unchecked
            {
                int h = 17;
                foreach (var r in data.Routes)
                {
                    h = h * 31 + (r.EntryFrom != null ? r.EntryFrom.GetHashCode() : 0);
                    h = h * 31 + (r.ExitTo != null ? r.ExitTo.GetHashCode() : 0);
                    h = h * 31 + r.TotalRunners;
                    h = h * 31 + r.YourRank;

                    foreach (var e in r.Entries)
                    {
                        h = h * 31 + e.Rank;
                        h = h * 31 + (e.RunnerName != null ? e.RunnerName.GetHashCode() : 0);
                        h = h * 31 + e.TotalTime.GetHashCode();
                        h = h * 31 + (e.RunId != null ? e.RunId.GetHashCode() : 0);
                        h = h * 31 + (e.IsYou ? 1 : 0);
                    }

                    if (r.YourEntry != null)
                    {
                        h = h * 31 + r.YourEntry.Rank;
                        h = h * 31 + r.YourEntry.TotalTime.GetHashCode();
                        h = h * 31 + (r.YourEntry.RunId != null
                            ? r.YourEntry.RunId.GetHashCode() : 0);
                    }
                }
                return h;
            }
        }
    }
}