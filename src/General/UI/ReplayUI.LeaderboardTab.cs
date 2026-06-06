namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void BuildLeaderboardContent()
        {
            if (rightContent == null) return;

            if (selectedScene == null)
            {
                AddCenteredMessage(rightContent, "Select a room to view leaderboards.");
                return;
            }

            // Placeholder until backend is connected
            AddCenteredMessage(rightContent, "Leaderboards coming soon.");
        }
    }
}   