using System.Collections.Generic;

namespace ReplayTimerMod
{
    /// <summary>
    /// Queued payload for background upload. Captures everything needed
    /// to POST a run without holding a reference to the full ReplaySnapshot
    /// (which contains the decoded FrameData[] and should not be pinned
    /// longer than necessary).
    /// </summary>
    internal sealed class UploadPayload
    {
        public string SnapshotId = "";
        public string Game = "";
        public string SceneName = "";
        public string EntryFrom = "";
        public string ExitTo = "";
        public float TotalTime;
        public int FrameCount;
        public long CapturedAtUtcTicks;
        public string ReplayData = "";   // base64 RTM3 string (already encoded)
        public string ModVersion = "";
        public int ModifierMask;         // see ModifierMask (never Unknown here)

        // Retry state (managed by UploadWorker)
        public int RetryCount;
        public long RetryAfterTicks; // DateTime.UtcNow.Ticks when eligible
    }

    /// <summary>
    /// Parsed response from POST /runs.
    /// </summary>
    internal sealed class UploadResponse
    {
        public string RunId = "";
        public int Rank;           // -1 if not returned
        public int TotalRunners;   // -1 if not returned
        public bool IsPB;
        public string DisplayName = ""; // server-assigned name (first upload)

        public bool HasRank => Rank > 0 && TotalRunners > 0;
    }

    /// <summary>
    /// Parsed response from POST /share.
    /// </summary>
    internal sealed class ShareResponse
    {
        public string Code = "";
        public string? Url;   // present only when the server has SHARE_BASE_URL set
        public bool HasCode => !string.IsNullOrEmpty(Code);
    }

    /// <summary>
    /// Parsed response from GET /config (or the config portion of /init).
    /// </summary>
    internal sealed class ConfigResponse
    {
        public bool Maintenance;
        public string? Announcement; // null if none
    }

    /// <summary>
    /// Rank info dispatched after a successful upload.
    /// </summary>
    public sealed class RankInfo
    {
        public readonly string SceneName;
        public readonly string EntryFrom;
        public readonly string ExitTo;
        public readonly float TotalTime;
        public readonly int Rank;
        public readonly int TotalRunners;

        public RankInfo(string sceneName, string entryFrom, string exitTo,
            float totalTime, int rank, int totalRunners)
        {
            SceneName = sceneName;
            EntryFrom = entryFrom;
            ExitTo = exitTo;
            TotalTime = totalTime;
            Rank = rank;
            TotalRunners = totalRunners;
        }
    }

    // ── New types for the redesigned networking ─────────────────────────

    /// <summary>
    /// One scene in the scene index. Compact field names match the
    /// server's JSON (s, r, n) for minimal bandwidth.
    /// </summary>
    public sealed class SceneInfo
    {
        public string SceneName = "";
        public int RouteCount;
        public int RunnerCount;
    }

    /// <summary>
    /// Parsed response from GET /init (combined config + scene index).
    /// </summary>
    internal sealed class InitResponse
    {
        public ConfigResponse Config = new ConfigResponse();
        public int SceneIndexVersion;
        public List<SceneInfo> Scenes = new List<SceneInfo>();
    }

    /// <summary>
    /// Parsed response from GET /scenes. May be a "no change" response
    /// (only Version set, Changed=false) or a full update.
    /// </summary>
    internal sealed class SceneIndexResponse
    {
        public int Version;
        public bool Changed;
        public List<SceneInfo> Scenes = new List<SceneInfo>();
    }

    /// <summary>
    /// Parsed response from GET /leaderboard with version support.
    /// May be "no change" (only Version set, Changed=false) or full data.
    /// </summary>
    internal sealed class VersionedLeaderboardResponse
    {
        public int Version;
        public bool Changed;
        public LeaderboardData Data = new LeaderboardData();
    }
}