using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void RebuildSceneList()
        {
            if (sceneListContent == null) return;

            // Preserve scroll position across rebuilds (manifest refreshes,
            // PB updates, selection changes shouldn't yank the list around)
            float keepScroll = sceneListScroll != null
                ? sceneListScroll.verticalNormalizedPosition : 1f;

            ClearContentDetached(sceneListContent);

            string filter = (searchFilter ?? "").Trim().ToLowerInvariant();

            // ── Merge local + server rooms ──────────────────────────────────
            //
            // Local: rooms where the player has recorded at least one run.
            // Server: all rooms known to the leaderboard manifest.
            // The union ensures rooms you haven't visited but others have
            // still appear in the scene list.

            var localScenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .ToList();

            var localSet = new System.Collections.Generic.HashSet<string>(localScenes);

            var serverScenes = _leaderboardCache.GetServerScenes();

            // Record what server-scene version this build reflects, so
            // HandleManifestReady can skip rebuilds when nothing changed.
            _renderedServerScenesVersion = _leaderboardCache.ServerScenesVersion;

            // Union, sorted alphabetically
            var scenes = localSet
                .Union(serverScenes)
                .OrderBy(s => s)
                .ToList();

            if (!string.IsNullOrEmpty(filter))
                scenes = scenes.Where(s => s.ToLowerInvariant().Contains(filter)).ToList();

            if (selectedScene != null && !scenes.Contains(selectedScene)
                && string.IsNullOrEmpty(filter))
            {
                selectedScene = null;
                UpdateRightSubHeader();
                if (rightContent != null)
                {
                    ClearContentDetached(rightContent);
                    AddCenteredMessage(rightContent, "Select a room to view runs.");
                    ForceLayout(rightContent);
                }
            }

            if (scenes.Count == 0)
            {
                string msg;
                if (!string.IsNullOrEmpty(filter))
                    msg = "No rooms match filter.";
                else if (GhostSettings.OnlineEnabled && !_leaderboardCache.ManifestLoaded
                    && _networkClient != null
                    && _networkClient.CurrentManifestStatus
                        == NetworkClient.ManifestStatus.Failed)
                    msg = "No replays yet.\nRoom sync failed \u2014 retrying...";
                else if (GhostSettings.OnlineEnabled && !_leaderboardCache.ManifestLoaded)
                    msg = "No replays yet.\nSyncing rooms...";
                else
                    msg = "No replays recorded yet.";
                AddCenteredMessage(sceneListContent, msg);
            }
            else
            {
                foreach (string scene in scenes)
                {
                    bool isServerOnly = !localSet.Contains(scene);
                    AddSceneRow(sceneListContent, scene, isServerOnly);
                }
            }

            ForceLayout(sceneListContent);

            if (sceneListScroll != null)
                sceneListScroll.verticalNormalizedPosition = Mathf.Clamp01(keepScroll);
        }

        private void AddSceneRow(Transform parent, string scene,
            bool isServerOnly = false)
        {
            bool selected = scene == selectedScene;
            bool current = scene == RoomTracker.CurrentScene;

            Color bgColor;
            Color textColor;

            if (current)
            {
                bgColor = UIStyle.Gold with { a = selected ? 0.28f : 0.12f };
                textColor = UIStyle.Gold;
            }
            else if (isServerOnly)
            {
                // Server-only rooms: subtler colors to distinguish from local
                bgColor = selected
                    ? UIStyle.Accent with { a = 0.18f }
                    : Color.clear;
                textColor = selected ? UIStyle.Accent : UIStyle.Overlay;
            }
            else
            {
                bgColor = selected
                    ? UIStyle.Overlay with { a = 0.6f }
                    : Color.clear;
                textColor = selected ? UIStyle.Text : UIStyle.Subtext;
            }

            var row = MakeGO("SceneRow", parent);
            Img(row, bgColor);
            Btn(row, () => SelectScene(scene));

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RH;

            int labelX = M;
            int labelW = LW - M * 2;

            // Server-only indicator: small circle prefix
            string displayName = isServerOnly
                ? "\u25CB " + scene   // ○ prefix for server-only rooms
                : scene;

            MakeLbl(row.transform, displayName,
                UIStyle.FontSizeSm - 1, textColor, TextAnchor.MiddleLeft,
                x: labelX, w: labelW, h: RH);
        }

        /// <summary>
        /// Scrolls the scene list so that the specified scene row is visible.
        /// Uses a text-match against row labels to find the correct child index.
        /// </summary>
        private void ScrollToScene(string scene)
        {
            if (sceneListScroll == null || sceneListContent == null) return;

            int totalChildren = sceneListContent.childCount;
            if (totalChildren <= 0) return;

            // Find the target row index by matching label text
            int targetIndex = -1;
            for (int i = 0; i < totalChildren; i++)
            {
                var lbl = sceneListContent.GetChild(i).GetComponentInChildren<Text>();
                if (lbl == null) continue;

                string text = lbl.text;
                // Strip server-only prefix if present
                if (text.StartsWith("\u25CB "))
                    text = text.Substring(2);

                if (text == scene)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0) return;

            // Calculate normalized scroll position (1 = top, 0 = bottom)
            float viewportH = sceneListScroll.viewport != null
                ? sceneListScroll.viewport.rect.height : 0f;
            float contentH = ((RectTransform)sceneListContent).rect.height;

            if (contentH <= viewportH) return; // all visible, no scrolling needed

            float targetY = targetIndex * RH;
            float maxScroll = contentH - viewportH;
            float normalized = 1f - Mathf.Clamp01(targetY / maxScroll);

            sceneListScroll.verticalNormalizedPosition = normalized;
        }
    }
}