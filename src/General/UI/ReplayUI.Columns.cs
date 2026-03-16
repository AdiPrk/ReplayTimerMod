using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

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
        }

        private void ClearRight()
        {
            if (rightContent != null) ClearContent(rightContent);
            if (rightHeader != null) rightHeader.text = "Select a room";
            if (pasteStatus != null) pasteStatus.text = "";
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

            int timeW = UIStyle.W(64);
            int countW = UIStyle.W(58);
            int currentW = UIStyle.W(54);
            int currentX = RW - clearW - M - currentW - M;
            int countX = currentX - countW - M;
            int timeX = countX - timeW - M;

            MakeLbl(row.transform, "Current", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter,
                x: currentX, w: currentW, h: h);
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
            int xBtnW = UIStyle.W(22);
            int copyW = UIStyle.W(46);
            int statusW = UIStyle.W(54);
            int metaW = UIStyle.W(92);
            int timeW = UIStyle.W(60);
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            var row = MakeGO("SnapshotRow", parent);
            Img(row, index % 2 == 0 ? Color.clear : UIStyle.Surface with { a = 0.55f });
            Rect(row, 0, top, RW, h);

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

            int statusX = RW - xBtnW - M - copyW - M - statusW - M;
            int metaX = statusX - metaW - M;
            int timeX = metaX - timeW - M;

            bool isCurrent = snapshot.SnapshotId == route.Current.SnapshotId;
            MakeLbl(row.transform, isCurrent ? "Current" : "History",
                UIStyle.FontSizeSm - 2,
                isCurrent ? UIStyle.Accent : UIStyle.Subtext,
                TextAnchor.MiddleCenter,
                x: statusX, w: statusW, h: h);

            string meta = FormatSnapshotMeta(snapshot);
            MakeLbl(row.transform, meta, UIStyle.FontSizeSm - 3,
                UIStyle.Subtext, TextAnchor.MiddleRight,
                x: metaX, w: metaW, h: h);

            MakeLbl(row.transform, TimeUtil.Format(snapshot.TotalTime), UIStyle.FontSizeSm,
                UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeW, h: h);

            MakeLbl(row.transform, SnapshotLabel(snapshot, index),
                UIStyle.FontSizeSm - 1, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M * 2, w: timeX - M * 3, h: h);
        }

        private static string SnapshotLabel(ReplaySnapshot snapshot, int index)
        {
            if (snapshot.HasCapturedAt)
            {
                var captured = new DateTime(snapshot.CapturedAtUtcTicks, DateTimeKind.Utc)
                    .ToLocalTime();
                return $"PB #{index + 1} · {captured:yyyy-MM-dd HH:mm}";
            }

            return $"PB #{index + 1}";
        }

        private static string FormatSnapshotMeta(ReplaySnapshot snapshot)
        {
            if (!snapshot.HasCapturedAt) return "legacy";
            var captured = new DateTime(snapshot.CapturedAtUtcTicks, DateTimeKind.Utc)
                .ToLocalTime();
            return captured.ToString("MM-dd HH:mm");
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