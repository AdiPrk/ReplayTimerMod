using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void BuildRunsContent(string scene)
        {
            if (rightContent == null) return;

            var routes = PBManager.AllHistories()
                .Where(h => h.Key.SceneName == scene)
                .OrderBy(h => h.Key.EntryFromScene)
                .ThenBy(h => h.Key.ExitToScene)
                .ToList();

            if (routes.Count == 0)
            {
                AddCenteredMessage(rightContent, "No entries for this room.");
                return;
            }

            bool stripe = false;
            foreach (var route in routes)
            {
                AddRouteGroup(rightContent, route, stripe);
                stripe = !stripe;
            }
        }

        private void AddRouteGroup(Transform parent, RouteReplayHistory route, bool stripe)
        {
            int headerH = RH + 2;
            int totalH = headerH + route.Count * RH;

            var group = MakeGO("RouteGroup", parent);
            Img(group, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            var le = group.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = totalH;

            AddRouteHeader(group.transform, route, headerH);

            for (int i = 0; i < route.Snapshots.Count; i++)
                AddSnapshotRow(group.transform, route, route.Snapshots[i], i, headerH);
        }

        private void AddRouteHeader(Transform parent, RouteReplayHistory route, int h)
        {
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;
            int clearW = UIStyle.W(48);

            var row = MakeGO("RouteHeader", parent);
            Img(row, UIStyle.Overlay with { a = 0.45f });
            Rect(row, 0, 0, RW, h);

            RoomKey key = route.Key;
            MakeButton(row.transform, "ClearRoute", "Clear",
                UIStyle.FontSizeSm - 2, UIStyle.Red, UIStyle.Red with { a = 0.18f },
                RW - clearW - M, btnY, clearW, btnH,
                () => DeleteRoute(key));

            string from = string.IsNullOrEmpty(route.Key.EntryFromScene)
                ? "spawn" : route.Key.EntryFromScene;
            MakeLbl(row.transform, from + " > " + route.Key.ExitToScene,
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: RW - clearW - M * 3, h: h);
        }

        private void AddSnapshotRow(Transform parent, RouteReplayHistory route,
            ReplaySnapshot snapshot, int index, int headerH)
        {
            int h = RH;
            int top = headerH + index * RH;

            bool playbackOn = SelectionState?.IsPlaybackSelected(snapshot.SnapshotId) ?? false;
            bool editing = SelectedSnapshotId == snapshot.SnapshotId;
            bool isCurrent = snapshot.SnapshotId == route.Current.SnapshotId;
            bool pendingDelete = deleteConfirmId == snapshot.SnapshotId;

            Color rowBg = editing
                ? UIStyle.Accent with { a = 0.15f }
                : (playbackOn
                    ? UIStyle.Gold with { a = 0.08f }
                    : (index % 2 == 1 ? UIStyle.Surface with { a = 0.35f } : Color.clear));

            var row = MakeGO("Snap", parent);
            Img(row, rowBg);
            Rect(row, 0, top, RW, h);

            RoomKey rowKey = route.Key;
            string rowSnapshotId = snapshot.SnapshotId;
            Btn(row, () => SelectSnapshotForEditing(rowKey, rowSnapshotId));

            int x = M / 2;
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;
            int sp = UIStyle.W(6);

            if (editing)
            {
                var bar = MakeGO("EditBar", row.transform);
                Img(bar, UIStyle.Accent with { a = 0.9f });
                Rect(bar, 0, 0, UIStyle.W(3), h);
            }

            // Playback toggle
            int toggleW = UIStyle.W(22);
            var playBtn = MakeGO("Play", row.transform);
            Img(playBtn, playbackOn
                ? UIStyle.Gold with { a = 0.25f }
                : UIStyle.Overlay with { a = 0.4f });
            string playId = snapshot.SnapshotId;
            RoomKey playKey = route.Key;
            Btn(playBtn, () => ToggleSnapshotPlayback(playKey, playId));
            Rect(playBtn, x, btnY, toggleW, btnH);
            MakeLbl(playBtn.transform, playbackOn ? "ON" : "",
                UIStyle.FontSizeSm - 3,
                playbackOn ? UIStyle.Gold : UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);
            x += toggleW + M / 2;

            // Color swatch
            int swatchW = UIStyle.W(14);
            int swatchH = UIStyle.H(14);
            var swatch = MakeGO("Color", row.transform);
            Img(swatch, GetResolvedSnapshotColor(snapshot));
            Rect(swatch, x, (h - swatchH) / 2, swatchW, swatchH);

            if (!snapshot.HasVisualOverride)
            {
                var inner = MakeGO("InheritMark", swatch.transform);
                Img(inner, UIStyle.Text with { a = 0.3f });
                Rect(inner, UIStyle.W(3), UIStyle.H(3),
                    swatchW - UIStyle.W(6), swatchH - UIStyle.H(6));
                inner.GetComponent<Graphic>().raycastTarget = false;
            }
            x += swatchW + M;

            // --- Right-aligned elements (positioned from right edge) ---

            // Delete button (two-click confirm)
            int delW = UIStyle.W(22);
            string delId = snapshot.SnapshotId;
            RoomKey delKey = route.Key;
            Color delBg = pendingDelete ? UIStyle.Red with { a = 0.7f } : UIStyle.Red with { a = 0.15f };
            Color delFg = pendingDelete ? UIStyle.Text : UIStyle.Red;
            MakeButton(row.transform, "Del", pendingDelete ? "!" : "X",
                UIStyle.FontSizeSm - 2, delFg, delBg,
                RW - delW - M, btnY, delW, btnH,
                () => OnSnapshotDeleteClicked(delKey, delId));

            // Copy button
            int copyW = UIStyle.W(40);
            int copyX = RW - delW - M - copyW - sp;
            string copyId = snapshot.SnapshotId;
            RoomKey copyKey = route.Key;
            MakeButton(row.transform, "Copy", "Copy",
                UIStyle.FontSizeSm - 2, UIStyle.Accent, UIStyle.Accent with { a = 0.15f },
                copyX, btnY, copyW, btnH,
                () => CopyReplay(copyKey, copyId));

            // Time
            int timeW = UIStyle.W(50);
            int timeX = copyX - timeW - sp * 2;
            MakeLbl(row.transform, TimeUtil.Format(snapshot.TotalTime),
                UIStyle.FontSizeSm, UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeW, h: h);

            // Delta (only for non-PB)
            string delta = FormatSnapshotDelta(route.Current.TotalTime, snapshot.TotalTime, isCurrent);
            int deltaW = UIStyle.W(46);
            int deltaX = timeX - deltaW - sp;
            if (!string.IsNullOrEmpty(delta))
            {
                MakeLbl(row.transform, delta, UIStyle.FontSizeSm - 2,
                    UIStyle.Subtext, TextAnchor.MiddleRight,
                    x: deltaX, w: deltaW, h: h);
            }

            // Label
            int labelEnd = string.IsNullOrEmpty(delta) ? timeX : deltaX;
            int labelW = labelEnd - x - sp;
            Color labelColor = editing ? UIStyle.Text : UIStyle.Subtext;
            MakeLbl(row.transform, "#" + (index + 1),
                UIStyle.FontSizeSm - 1, labelColor, TextAnchor.MiddleLeft,
                x: x, w: labelW, h: h);
        }

        private void OnSnapshotDeleteClicked(RoomKey key, string snapshotId)
        {
            if (deleteConfirmId == snapshotId)
            {
                deleteConfirmId = null;
                DeleteSnapshot(key, snapshotId);
            }
            else
            {
                deleteConfirmId = snapshotId;
                if (selectedScene != null && activeTab == TabKind.Runs)
                    RebuildRightContent();
            }
        }

        private static string FormatSnapshotDelta(float pbTime, float snapTime, bool isPb)
        {
            if (isPb) return "";

            float delta = Mathf.Max(0f, snapTime - pbTime);
            int cs = Mathf.RoundToInt(delta * 100f);
            int min = cs / 6000;
            int sec = (cs / 100) % 60;
            int rem = cs % 100;

            return min > 0
                ? $"+{min}:{sec:00}.{rem:00}"
                : $"+{sec:00}.{rem:00}";
        }
    }
}