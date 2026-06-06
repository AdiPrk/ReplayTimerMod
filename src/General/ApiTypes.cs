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
        public string SnapshotId;
        public string Game;
        public string SceneName;
        public string EntryFrom;
        public string ExitTo;
        public float TotalTime;
        public int FrameCount;
        public long CapturedAtUtcTicks;
        public string ReplayData;   // base64 RTM3 string (already encoded)
        public string ModVersion;

        // Retry state (managed by UploadWorker)
        public int RetryCount;
        public long RetryAfterTicks; // DateTime.UtcNow.Ticks when eligible
    }

    /// <summary>
    /// Parsed response from POST /api/v1/runs.
    /// </summary>
    internal sealed class UploadResponse
    {
        public string RunId;
        public int Rank;           // -1 if not returned
        public int TotalRunners;   // -1 if not returned
        public bool IsPB;
        public string DisplayName; // server-assigned name (first upload)

        public bool HasRank => Rank > 0 && TotalRunners > 0;
    }

    /// <summary>
    /// Parsed response from GET /api/v1/config.
    /// </summary>
    internal sealed class ConfigResponse
    {
        public string MinModVersion;
        public bool Maintenance;
        public string Announcement; // null if none
    }

    /// <summary>
    /// Rank info dispatched to the main thread after a successful upload.
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
}