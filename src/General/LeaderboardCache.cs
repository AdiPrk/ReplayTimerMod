using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        public int YourRank = -1;
        public List<LeaderboardEntry> Entries = new List<LeaderboardEntry>();
        public LeaderboardEntry? YourEntry;
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
    /// In-memory cache for leaderboard data.
    ///
    /// Two tiers of data:
    ///   1. Scene index (lightweight): which scenes have data + aggregate counts.
    ///      Populated from GET /init and GET /scenes.
    ///   2. Per-room leaderboards (full): entries, ranks, times.
    ///      Populated on demand from GET /leaderboard.
    ///
    /// Change detection: every entry carries a content signature. Updates
    /// that don't change the data don't bump the version, so the UI can
    /// skip rebuilds for identical polling responses.
    ///
    /// Key surface:
    ///   SceneIndexLoaded → true when the scene index has loaded
    ///   GetServerScenes() → set of scene names from scene index
    ///   ServerScenesVersion → bumped when scene set changes
    ///   Get/GetVersion → per-room leaderboard data
    /// </summary>
    public sealed class LeaderboardCache
    {
        // ── Scene index (from /init, /scenes) ──────────────────────────────

        private int _sceneIndexVersion;       // server's version stamp
        private bool _sceneIndexLoaded;
        private readonly HashSet<string> _serverScenes = new HashSet<string>();
        private readonly Dictionary<string, SceneInfo> _sceneInfos =
            new Dictionary<string, SceneInfo>();
        private int _serverScenesVersion;     // local monotonic, for UI rebuild gating

        // ── Per-room leaderboards (from /leaderboard) ──────────────────────

        private readonly Dictionary<string, LeaderboardData> _cache =
            new Dictionary<string, LeaderboardData>();
        private readonly Dictionary<string, int> _signatures =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> _versions =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> _roomServerVersions =
            new Dictionary<string, int>();
        private readonly Dictionary<string, float> _roomFetchedAt =
            new Dictionary<string, float>();

        // Per-room dictionaries are keyed by game + scene so multiple games
        // can share one cache without colliding.
        private static string Key(string game, string scene) => game + ":" + scene;

        // ── Scene index API ────────────────────────────────────────────────

        /// <summary>The server's scene-index version stamp.</summary>
        public int SceneIndexVersion => _sceneIndexVersion;

        /// <summary>
        /// True once the scene index has loaded. The UI checks this to know
        /// whether to show online indicators.
        /// </summary>
        public bool SceneIndexLoaded => _sceneIndexLoaded;

        /// <summary>
        /// Bumped only when the server scene SET changes (rooms added or
        /// removed), not on every scene index refresh.
        /// </summary>
        public int ServerScenesVersion => _serverScenesVersion;

        /// <summary>
        /// Copy of all scene names the server has leaderboard data for.
        /// </summary>
        public HashSet<string> GetServerScenes()
        {
            return new HashSet<string>(_serverScenes);
        }

        /// <summary>
        /// Returns aggregate info for a scene, or null if not in index.
        /// </summary>
        public SceneInfo? GetSceneInfo(string scene)
        {
            return _sceneInfos.TryGetValue(scene, out var info) ? info : null;
        }

        /// <summary>
        /// Updates the scene index from a /scenes or /init response.
        /// Returns true if the scene set changed.
        /// </summary>
        public bool UpdateSceneIndex(int serverVersion, List<SceneInfo> scenes)
        {
            _sceneIndexVersion = serverVersion;
            _sceneIndexLoaded = true;

            // Check if scene set changed.
            bool setChanged = _serverScenes.Count != scenes.Count;
            if (!setChanged)
            {
                foreach (var s in scenes)
                {
                    if (!_serverScenes.Contains(s.SceneName))
                    {
                        setChanged = true;
                        break;
                    }
                }
            }

            _serverScenes.Clear();
            _sceneInfos.Clear();
            foreach (var s in scenes)
            {
                _serverScenes.Add(s.SceneName);
                _sceneInfos[s.SceneName] = s;
            }

            if (setChanged)
                _serverScenesVersion++;

            return setChanged;
        }

        // ── Per-room leaderboard API ───────────────────────────────────────

        public LeaderboardData? Get(string game, string scene)
        {
            return _cache.TryGetValue(Key(game, scene), out var data) ? data : null;
        }

        /// <summary>
        /// Monotonic version for a room's data. Bumps only when content
        /// actually changes. Returns 0 for rooms never cached.
        /// </summary>
        public int GetVersion(string game, string scene)
        {
            return _versions.TryGetValue(Key(game, scene), out var v) ? v : 0;
        }

        /// <summary>
        /// The server-side version for this room. Sent in subsequent
        /// requests as the 'v' param for conditional responses.
        /// Returns 0 if never fetched.
        /// </summary>
        public int GetRoomServerVersion(string game, string scene)
        {
            return _roomServerVersions.TryGetValue(Key(game, scene), out var v) ? v : 0;
        }

        /// <summary>
        /// Whether cached data for this room is fresh enough to display
        /// without re-fetching.
        /// </summary>
        public bool IsRoomFresh(string game, string scene, float maxAgeSec)
        {
            if (!_roomFetchedAt.TryGetValue(Key(game, scene), out var fetchedAt))
                return false;
            return (Time.realtimeSinceStartup - fetchedAt) < maxAgeSec;
        }

        /// <summary>
        /// Whether we have any cached data for this room (fresh or stale).
        /// </summary>
        public bool HasRoomData(string game, string scene)
        {
            return _cache.ContainsKey(Key(game, scene));
        }

        /// <summary>
        /// Updates a room's leaderboard data from a server response.
        /// Returns true if the content actually changed (version bumped).
        /// </summary>
        public bool UpdateRoom(string game, string scene,
            int serverVersion, LeaderboardData data)
        {
            string key = Key(game, scene);
            int sig = ComputeSignature(data);

            _cache[key] = data;
            _roomServerVersions[key] = serverVersion;
            _roomFetchedAt[key] = Time.realtimeSinceStartup;

            if (_signatures.TryGetValue(key, out var oldSig) && oldSig == sig)
                return false;  // identical content — no version bump

            _signatures[key] = sig;
            _versions[key] = (_versions.TryGetValue(key, out var v) ? v : 0) + 1;
            return true;
        }

        // ── Optimistic upload update ───────────────────────────────────────

        /// <summary>
        /// Locally updates the cache after a successful upload, without any
        /// network request. Uses data from the upload payload and response
        /// to insert/update the player's entry in the cached leaderboard.
        ///
        /// Returns true if the cache was modified (and UI should rebuild).
        /// </summary>
        public bool ApplyOptimisticUpload(string game, string scene,
            string entryFrom, string exitTo, float totalTime,
            int rank, int totalRunners, string displayName)
        {
            string key = Key(game, scene);

            if (!_cache.TryGetValue(key, out var data))
            {
                // No cached data for this room. Create a stub so the player
                // can at least see their own entry if they open the tab.
                data = new LeaderboardData();
                _cache[key] = data;
            }

            // Find or create the route.
            RouteLeaderboard? route = null;
            foreach (var r in data.Routes)
            {
                if (r.EntryFrom == entryFrom && r.ExitTo == exitTo)
                {
                    route = r;
                    break;
                }
            }

            if (route == null)
            {
                route = new RouteLeaderboard
                {
                    EntryFrom = entryFrom,
                    ExitTo = exitTo
                };
                data.Routes.Add(route);
            }

            // Remove old "you" entry if present.
            route.Entries.RemoveAll(e => e.IsYou);
            if (route.YourEntry != null && route.YourEntry.IsYou)
                route.YourEntry = null;

            // Create new entry.
            var newEntry = new LeaderboardEntry
            {
                Rank = rank,
                RunnerName = string.IsNullOrEmpty(displayName) ? "You" : displayName,
                TotalTime = totalTime,
                RunId = "",  // not known until next server fetch
                IsYou = true
            };

            // Insert at correct position in sorted list.
            bool inserted = false;
            for (int i = 0; i < route.Entries.Count; i++)
            {
                if (totalTime < route.Entries[i].TotalTime)
                {
                    route.Entries.Insert(i, newEntry);
                    inserted = true;
                    break;
                }
            }
            if (!inserted)
                route.Entries.Add(newEntry);

            // Re-rank all visible entries.
            for (int i = 0; i < route.Entries.Count; i++)
                route.Entries[i].Rank = i + 1;

            route.TotalRunners = totalRunners;
            route.YourRank = rank;

            // If the new entry is outside the visible top (shouldn't happen
            // often with optimistic update), move it to YourEntry.
            const int TopN = 10;
            if (route.Entries.Count > TopN)
            {
                int youIdx = route.Entries.FindIndex(e => e.IsYou);
                if (youIdx >= TopN)
                {
                    route.YourEntry = route.Entries[youIdx];
                    route.Entries.RemoveAt(youIdx);
                }
            }

            // Bump local version to trigger UI rebuild.
            _signatures.Remove(key);  // force next signature check to differ
            _versions[key] = (_versions.TryGetValue(key, out var v) ? v : 0) + 1;
            _roomFetchedAt[key] = Time.realtimeSinceStartup;

            // Also ensure this scene is in the server scenes set.
            if (_serverScenes.Add(scene))
                _serverScenesVersion++;

            return true;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────

        public void Clear()
        {
            _cache.Clear();
            _signatures.Clear();
            _versions.Clear();
            _roomServerVersions.Clear();
            _roomFetchedAt.Clear();
            _serverScenes.Clear();
            _sceneInfos.Clear();
            _sceneIndexVersion = 0;
            _sceneIndexLoaded = false;
            _serverScenesVersion++;
        }

        /// <summary>
        /// Resets all cached server versions to 0, forcing the next
        /// leaderboard poll to fetch fresh data. Call this after a
        /// display name change so updated names appear immediately.
        /// </summary>
        public void InvalidateAllRoomVersions()
        {
            _roomServerVersions.Clear();
            _roomFetchedAt.Clear();
            _sceneIndexVersion = 0;
        }

        // ── Signature ──────────────────────────────────────────────────────

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