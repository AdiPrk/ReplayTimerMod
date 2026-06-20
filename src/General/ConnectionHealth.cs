namespace ReplayTimerMod
{
    /// <summary>
    /// Tracks the health of the server connection based on recent request
    /// outcomes. Used to modulate polling intervals: back off when the
    /// server is unreachable, resume immediately on recovery.
    /// </summary>
    internal sealed class ConnectionHealth
    {
        public enum State { Healthy, Degraded, Down }

        private const int DegradedThreshold = 3;
        private const int DownThreshold = 8;

        private int _consecutiveFailures;

        public State CurrentState { get; private set; } = State.Healthy;

        public void RecordSuccess()
        {
            _consecutiveFailures = 0;
            CurrentState = State.Healthy;
        }

        public void RecordFailure()
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= DownThreshold)
                CurrentState = State.Down;
            else if (_consecutiveFailures >= DegradedThreshold)
                CurrentState = State.Degraded;
        }

        /// <summary>
        /// Multiplier applied to all polling intervals.
        ///   Healthy  → 1.0x (normal)
        ///   Degraded → 2.0x (slow down)
        ///   Down     → 10.0x (near-suspended)
        /// </summary>
        public float PollMultiplier
        {
            get
            {
                if (CurrentState == State.Down) return 10f;
                if (CurrentState == State.Degraded) return 2f;
                return 1f;
            }
        }

        /// <summary>
        /// Whether non-upload requests should be attempted at all.
        /// When Down, only uploads (which are important) should proceed.
        /// </summary>
        public bool ShouldAttemptPolling => CurrentState != State.Down;

        public int ConsecutiveFailures => _consecutiveFailures;
    }
}