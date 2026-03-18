using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static ReplayTimerMod.UIStyle;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Left column - scene list ──────────────────────────────────────────

        private void RebuildLeft()
        {
            if (leftContent == null) return;
            ClearContent(leftContent);

            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            if (selectedScene != null && !scenes.Contains(selectedScene))
            {
                selectedScene = null;
                ClearRight();
            }

            if (scenes.Count == 0)
                AddMessageRow(leftContent, "No replays yet.");
            else
                foreach (var scene in scenes)
                    AddSceneRow(leftContent, scene);

            ForceLayout(leftContent);
        }

        private void AddSceneRow(Transform parent, string scene)
        {
            bool selected = scene == selectedScene;
            bool isCurrent = scene == RoomTracker.CurrentScene;

            Color bgColor = isCurrent
                ? UIStyle.Gold with { a = selected ? 0.28f : 0.14f }
                : (selected ? UIStyle.Overlay : Color.clear);

            Color textColor = isCurrent
                ? UIStyle.Gold
                : (selected ? UIStyle.Accent : UIStyle.Text);

            string label = isCurrent ? $"● {scene}" : scene;

            var row = MakeGO("SceneRow", parent);
            Img(row, bgColor);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RH;
            Btn(row, () => SelectScene(scene));
            MakeLbl(row.transform, label, UIStyle.FontSizeSm,
                textColor, TextAnchor.MiddleLeft, x: M, w: LW - M * 2, h: RH);
        }

        private void SelectScene(string scene)
        {
            selectedScene = scene;
            RebuildLeft();
            RebuildRight(scene);
        }

        // Scrolls to given scene
        private void ScrollToScene(string scene)
        {
            if (leftScrollRect == null || leftContent == null) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                leftContent.GetComponent<RectTransform>());

            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            int idx = scenes.IndexOf(scene);
            if (idx < 0) return;

            float contentHeight = leftContent.GetComponent<RectTransform>().rect.height;
            float viewportHeight = leftScrollRect.viewport.rect.height;
            if (contentHeight <= viewportHeight) return;

            // Position the row in the centre of the viewport.
            float rowTop = idx * (RH + 1f);
            float scrollOffset = rowTop - (viewportHeight - RH) / 2f;
            scrollOffset = Mathf.Clamp(scrollOffset, 0f, contentHeight - viewportHeight);

            // ScrollRect.verticalNormalizedPosition: 1 = top, 0 = bottom.
            leftScrollRect.verticalNormalizedPosition =
                1f - scrollOffset / (contentHeight - viewportHeight);
        }

        // ── Right column - entries for selected scene ─────────────────────────

        private void RebuildRight(string scene)
        {
            if (rightContent == null) return;
            if (rightHeader != null) rightHeader.text = scene;
            if (pasteStatus != null) pasteStatus.text = "";

            ClearContent(rightContent);

            var routes = PBManager.AllHistories()
                .Where(history => history.Key.SceneName == scene)
                .OrderBy(history => history.Key.EntryFromScene)
                .ThenBy(history => history.Key.ExitToScene)
                .ToList();

            if (routes.Count == 0)
                AddMessageRow(rightContent, "No entries.");
            else
            {
                bool stripe = false;
                foreach (var route in routes)
                {
                    AddRouteGroup(rightContent, route, stripe);
                    stripe = !stripe;
                }
            }

            ForceLayout(rightContent);
            RefreshSettingsBar();
        }

        private void ClearRight()
        {
            if (rightContent != null) ClearContent(rightContent);
            if (rightHeader != null) rightHeader.text = "Select a room";
            if (pasteStatus != null) pasteStatus.text = "";
            RefreshSettingsBar();
        }

        private void AddRouteGroup(Transform parent, RouteReplayHistory route, bool stripe)
        {
            var group = MakeGO("RouteGroup", parent);
            Img(group, stripe ? UIStyle.Surface : Color.clear);
            var le = group.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RouteGroupHeight(route.Count);

            AddRouteHeader(group.transform, route);

            for (int i = 0; i < route.Snapshots.Count; i++)
                AddSnapshotRow(group.transform, route, route.Snapshots[i], i);
        }

        private int RouteGroupHeight(int snapshotCount)
        {
            int headerHeight = RH + 2;
            int rowsHeight = snapshotCount * RH;
            return headerHeight + rowsHeight;
        }

        private void AddRouteHeader(Transform parent, RouteReplayHistory route)
        {
            int h = RH + 2;
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;
            int clearW = UIStyle.W(52);

            var row = MakeGO("RouteHeader", parent);
            Img(row, UIStyle.Overlay with { a = 0.65f });
            Rect(row, 0, 0, RW, h);

            var clearBtn = MakeGO("DeleteRoute", row.transform);
            Img(clearBtn, UIStyle.Red with { a = 0.18f });
            RoomKey key = route.Key;
            Btn(clearBtn, () => DeleteRoute(key));
            Rect(clearBtn, RW - clearW - M, btnY, clearW, btnH);
            MakeLbl(clearBtn.transform, "Clear", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            int timeW = UIStyle.W(72);
            int countW = UIStyle.W(46);
            int countX = RW - clearW - M - countW - M;
            int timeX = countX - timeW - M;

            MakeLbl(row.transform, $"x{route.Count}", UIStyle.FontSizeSm - 2,
                UIStyle.Subtext, TextAnchor.MiddleCenter,
                x: countX, w: countW, h: h);
            MakeLbl(row.transform, TimeUtil.Format(route.Current.TotalTime), UIStyle.FontSizeSm,
                UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeW, h: h);

            string from = string.IsNullOrEmpty(route.Key.EntryFromScene) ? "spawn" : route.Key.EntryFromScene;
            string label = $"{from} → {route.Key.ExitToScene}";
            MakeLbl(row.transform, label,
                UIStyle.FontSizeSm, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: timeX - M * 2, h: h);
        }

        private void AddSnapshotRow(Transform parent, RouteReplayHistory route,
            ReplaySnapshot snapshot, int index)
        {
            int h = RH;
            int top = RH + 2 + index * RH;
            int toggleW = UIStyle.W(22);
            int previewW = UIStyle.W(18);
            int xBtnW = UIStyle.W(22);
            int copyW = UIStyle.W(46);
            int metaW = UIStyle.W(88);
            int deltaW = UIStyle.W(72);
            int timeW = UIStyle.W(72);
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            bool playbackSelected = SelectionState?.IsPlaybackSelected(snapshot.SnapshotId) ?? false;
            bool editSelected = SelectedSnapshotId == snapshot.SnapshotId;
            Color baseColor = index % 2 == 0 ? Color.clear : UIStyle.Surface with { a = 0.55f };
            Color rowColor = editSelected
                ? UIStyle.Accent with { a = 0.18f }
                : (playbackSelected ? UIStyle.Gold with { a = 0.10f } : baseColor);

            var row = MakeGO("SnapshotRow", parent);
            Img(row, rowColor);
            Rect(row, 0, top, RW, h);
            RoomKey selectedKey = route.Key;
            string selectedSnapshotId = snapshot.SnapshotId;
            Btn(row, () => SelectSnapshotForEditing(selectedKey, selectedSnapshotId));

            if (editSelected)
            {
                var selectedBorder = MakeGO("SelectedBorder", row.transform);
                Img(selectedBorder, UIStyle.Accent with { a = 0.9f });
                Rect(selectedBorder, 0, 0, UIStyle.W(3), h);
            }

            var playbackBtn = MakeGO("PlaybackToggle", row.transform);
            Img(playbackBtn, playbackSelected
                ? UIStyle.Gold with { a = 0.28f }
                : UIStyle.Overlay with { a = 0.55f });
            RoomKey playbackKey = route.Key;
            string playbackSnapshotId = snapshot.SnapshotId;
            Btn(playbackBtn, () => ToggleSnapshotPlayback(playbackKey, playbackSnapshotId));
            Rect(playbackBtn, M / 2, btnY, toggleW, btnH);
            MakeLbl(playbackBtn.transform, playbackSelected ? "▶" : "",
                UIStyle.FontSizeSm - 2,
                playbackSelected ? UIStyle.Gold : UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);

            var preview = MakeGO("VisualPreview", row.transform);
            Img(preview, GetResolvedSnapshotColor(snapshot));
            Rect(preview, M / 2 + toggleW + M / 2, btnY + UIStyle.H(2), previewW, btnH - UIStyle.H(4));
            if (!snapshot.HasVisualOverride)
            {
                var inheritedMarker = MakeGO("InheritedMarker", preview.transform);
                Img(inheritedMarker, UIStyle.Text with { a = 0.32f });
                Rect(inheritedMarker,
                    UIStyle.W(5),
                    UIStyle.H(5),
                    previewW - UIStyle.W(10),
                    btnH - UIStyle.H(14));
                inheritedMarker.GetComponent<Graphic>().raycastTarget = false;
            }

            var xBtn = MakeGO("Delete", row.transform);
            Img(xBtn, UIStyle.Red with { a = 0.20f });
            RoomKey deleteKey = route.Key;
            string deleteSnapshotId = snapshot.SnapshotId;
            Btn(xBtn, () => DeleteSnapshot(deleteKey, deleteSnapshotId));
            Rect(xBtn, RW - xBtnW - M, btnY, xBtnW, btnH);
            MakeLbl(xBtn.transform, "✕", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            var copyBtn = MakeGO("Copy", row.transform);
            Img(copyBtn, UIStyle.Accent with { a = 0.22f });
            RoomKey copyKey = route.Key;
            string copySnapshotId = snapshot.SnapshotId;
            Btn(copyBtn, () => CopyReplay(copyKey, copySnapshotId));
            Rect(copyBtn, RW - xBtnW - M - copyW - M, btnY, copyW, btnH);
            MakeLbl(copyBtn.transform, "Copy", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            int metaX = RW - xBtnW - M - copyW - M - metaW - M;
            int deltaX = metaX - deltaW - M;
            int timeX = deltaX - timeW - M;
            int labelX = M / 2 + toggleW + M / 2 + previewW + M;

            string meta = FormatSnapshotMeta(snapshot);
            MakeLbl(row.transform, meta, UIStyle.FontSizeSm - 3,
                UIStyle.Subtext, TextAnchor.MiddleRight,
                x: metaX, w: metaW, h: h);

            bool isCurrent = snapshot.SnapshotId == route.Current.SnapshotId;
            string delta = FormatSnapshotDelta(route.Current.TotalTime, snapshot.TotalTime, isCurrent);
            MakeLbl(row.transform, delta, UIStyle.FontSizeSm - 1,
                UIStyle.Gold, TextAnchor.MiddleRight,
                x: deltaX, w: deltaW, h: h);

            MakeLbl(row.transform, TimeUtil.Format(snapshot.TotalTime), UIStyle.FontSizeSm,
                UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeW, h: h);

            MakeLbl(row.transform, SnapshotLabel(index),
                UIStyle.FontSizeSm,
                editSelected ? UIStyle.Text : UIStyle.Subtext,
                TextAnchor.MiddleLeft,
                x: labelX, w: timeX - labelX - M, h: h);
        }

        private static string SnapshotLabel(ReplaySnapshot snapshot, int index)
        {
            if (snapshot.HasCapturedAt)
            {
                var captured = new DateTime(snapshot.CapturedAtUtcTicks, DateTimeKind.Utc)
                    .ToLocalTime();
                return $"PB #{index + 1} · {captured:yyyy-MM-dd HH:mm}";
            }

            return SnapshotLabel(index);
        }

        private static string SnapshotLabel(int index)
        {
            return $"PB #{index + 1}";
        }

        private static string FormatSnapshotMeta(ReplaySnapshot snapshot)
        {
            if (!snapshot.HasCapturedAt) return "legacy";
            var captured = new DateTime(snapshot.CapturedAtUtcTicks, DateTimeKind.Utc)
                .ToLocalTime();
            return captured.ToString("MM-dd HH:mm");
        }

        private static string FormatSnapshotDelta(float routePbTime, float snapshotTime, bool isRoutePb)
        {
            if (isRoutePb) return string.Empty;

            float delta = Mathf.Max(0f, snapshotTime - routePbTime);
            int totalCentiseconds = Mathf.RoundToInt(delta * 100f);
            int minutes = totalCentiseconds / 6000;
            int seconds = (totalCentiseconds / 100) % 60;
            int centiseconds = totalCentiseconds % 100;

            return minutes > 0
                ? $"+{minutes}:{seconds:00}.{centiseconds:00}"
                : $"+{seconds:00}.{centiseconds:00}";
        }

        private static void AddMessageRow(Transform parent, string msg)
        {
            var row = MakeGO("MsgRow", parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(40);
            MakeLbl(row.transform, msg, UIStyle.FontSizeSm,
                UIStyle.Subtext, TextAnchor.MiddleCenter, fill: true);
        }
    }
}