using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void RebuildSceneList()
        {
            if (_sceneListContent == null) return;

            // Preserve scroll position across rebuilds (scene-index refreshes,
            // PB updates, selection changes shouldn't yank the list around)
            float keepScroll = _sceneListScroll != null
                ? _sceneListScroll.verticalNormalizedPosition : 1f;

            ClearContentDetached(_sceneListContent);

            string filter = (_searchFilter ?? "").Trim().ToLowerInvariant();

            // Rooms where the player has recorded at least one run.
            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (!string.IsNullOrEmpty(filter))
                scenes = scenes.Where(s => s.ToLowerInvariant().Contains(filter)).ToList();

            if (_selectedScene != null && !scenes.Contains(_selectedScene)
                && string.IsNullOrEmpty(filter))
            {
                _selectedScene = null;
                UpdateRightSubHeader();
                if (_rightContent != null)
                {
                    ClearContentDetached(_rightContent);
                    AddCenteredMessage(_rightContent, "Select a room to view runs.");
                    ForceLayout(_rightContent);
                }
            }

            if (scenes.Count == 0)
            {
                string msg = !string.IsNullOrEmpty(filter)
                    ? "No rooms match filter."
                    : "No replays recorded yet.";
                AddCenteredMessage(_sceneListContent, msg);
            }
            else
            {
                foreach (string scene in scenes)
                    AddSceneRow(_sceneListContent, scene);
            }

            ForceLayout(_sceneListContent);

            if (_sceneListScroll != null)
                _sceneListScroll.verticalNormalizedPosition = Mathf.Clamp01(keepScroll);
        }

        private void AddSceneRow(Transform parent, string scene)
        {
            bool selected = scene == _selectedScene;
            bool current = scene == RoomTracker.CurrentScene;

            Color bgColor;
            Color textColor;

            if (current)
            {
                bgColor = UIStyle.Gold with { a = selected ? 0.28f : 0.12f };
                textColor = UIStyle.Gold;
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
            AddHoverEffect(row);

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RH;

            // Selection affordance: same accent edge bar as the Runs tab's
            // editing row, so "selected" looks identical everywhere.
            if (selected)
            {
                var bar = MakeGO("SelBar", row.transform);
                Img(bar, (current ? UIStyle.Gold : UIStyle.Accent) with { a = 0.9f });
                Rect(bar, 0, 0, UIStyle.W(3), RH);
                bar.GetComponent<Graphic>().raycastTarget = false;
            }

            int labelX = M;
            int labelW = LW - M * 2;

            // No glyph prefix - the label is always exactly the scene name
            // (ScrollToScene matches rows by label text).
            MakeLbl(row.transform, scene,
                UIStyle.FontSizeRow, textColor, TextAnchor.MiddleLeft,
                x: labelX, w: labelW, h: RH);
        }

        /// <summary>
        /// Scrolls the scene list so that the specified scene row is visible.
        /// Row labels are exactly the scene name, so a direct text match finds
        /// the child index.
        /// </summary>
        private void ScrollToScene(string scene)
        {
            if (_sceneListScroll == null || _sceneListContent == null) return;

            int totalChildren = _sceneListContent.childCount;
            if (totalChildren <= 0) return;

            int targetIndex = -1;
            for (int i = 0; i < totalChildren; i++)
            {
                var lbl = _sceneListContent.GetChild(i).GetComponentInChildren<Text>();
                if (lbl == null) continue;

                if (lbl.text == scene)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0) return;

            // verticalNormalizedPosition: 1 = top, 0 = bottom
            float viewportH = _sceneListScroll.viewport != null
                ? _sceneListScroll.viewport.rect.height : 0f;
            float contentH = ((RectTransform)_sceneListContent).rect.height;

            if (contentH <= viewportH) return; // all visible, no scrolling needed

            // Row pitch = row height + the VerticalLayoutGroup's 1px spacing
            // (see BuildLeftPane); RH alone drifts ~1px short per row.
            float targetY = targetIndex * (RH + 1);
            float maxScroll = contentH - viewportH;
            float normalized = 1f - Mathf.Clamp01(targetY / maxScroll);

            _sceneListScroll.verticalNormalizedPosition = normalized;
        }
    }
}