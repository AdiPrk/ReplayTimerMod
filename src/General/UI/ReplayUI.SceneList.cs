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
            ClearContent(sceneListContent);

            string filter = (searchFilter ?? "").Trim().ToLowerInvariant();
            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
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
                    ClearContent(rightContent);
                    AddCenteredMessage(rightContent, "Select a room to view runs.");
                    ForceLayout(rightContent);
                }
            }

            if (scenes.Count == 0)
            {
                string msg = string.IsNullOrEmpty(filter)
                    ? "No replays recorded yet."
                    : "No rooms match filter.";
                AddCenteredMessage(sceneListContent, msg);
            }
            else
            {
                foreach (string scene in scenes)
                    AddSceneRow(sceneListContent, scene);
            }

            if (sceneCountLbl != null)
            {
                int totalScenes = PBManager.AllPBs()
                    .Select(p => p.Key.SceneName)
                    .Distinct()
                    .Count();
                sceneCountLbl.text = totalScenes + " rooms";
            }

            ForceLayout(sceneListContent);
        }

        private void AddSceneRow(Transform parent, string scene)
        {
            bool selected = scene == selectedScene;
            bool current = scene == RoomTracker.CurrentScene;

            Color bgColor = current
                ? UIStyle.Gold with { a = selected ? 0.28f : 0.12f }
                : (selected ? UIStyle.Overlay with { a = 0.6f } : Color.clear);

            Color textColor = current
                ? UIStyle.Gold
                : (selected ? UIStyle.Accent : UIStyle.Text);

            var row = MakeGO("SceneRow", parent);
            Img(row, bgColor);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RH;

            string sceneName = scene;
            Btn(row, () => SelectScene(sceneName));

            int x = M;

            if (current)
            {
                var dot = MakeGO("Dot", row.transform);
                Img(dot, UIStyle.Gold);
                Rect(dot, x, (RH - UIStyle.H(5)) / 2, UIStyle.H(5), UIStyle.H(5));
                x += UIStyle.H(5) + UIStyle.W(4);
            }

            MakeLbl(row.transform, scene, UIStyle.FontSizeSm - 1,
                textColor, TextAnchor.MiddleLeft,
                x: x, w: LW - x - M, h: RH);
        }

        private void ScrollToScene(string scene)
        {
            if (sceneListScroll == null || sceneListContent == null) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                sceneListContent.GetComponent<RectTransform>());

            string filter = (searchFilter ?? "").Trim().ToLowerInvariant();
            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            if (!string.IsNullOrEmpty(filter))
                scenes = scenes.Where(s => s.ToLowerInvariant().Contains(filter)).ToList();

            int idx = scenes.IndexOf(scene);
            if (idx < 0) return;

            float contentH = sceneListContent.GetComponent<RectTransform>().rect.height;
            float viewportH = sceneListScroll.viewport.rect.height;
            if (contentH <= viewportH) return;

            float rowTop = idx * (RH + 1f);
            float scrollOffset = rowTop - (viewportH - RH) / 2f;
            scrollOffset = Mathf.Clamp(scrollOffset, 0f, contentH - viewportH);

            sceneListScroll.verticalNormalizedPosition =
                1f - scrollOffset / (contentH - viewportH);
        }
    }
}