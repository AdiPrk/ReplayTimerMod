using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void BuildRunsContent(string scene)
        {
            if (_rightContent == null) return;

            var routes = PBManager.AllHistories()
                .Where(h => h.Key.SceneName == scene)
                .OrderBy(h => h.Key.EntryFromScene)
                .ThenBy(h => h.Key.ExitToScene)
                .ToList();

            if (routes.Count == 0)
            {
                AddCenteredMessage(_rightContent, "No entries for this room.");
                return;
            }

            // Sized to the widest time in the room before any row is built.
            int timeColW = TimeColumnWidth(
                routes.SelectMany(r => r.Snapshots).Select(s => s.TotalTime));

            bool stripe = false;
            foreach (var route in routes)
            {
                AddRouteGroup(_rightContent, route, stripe, timeColW);
                stripe = !stripe;
            }
        }

        /// <summary>
        /// Lightweight rebuild of just the Runs content area:
        /// detached clear, scroll preserved.
        /// </summary>
        private void RebuildRunsContentOnly()
        {
            if (_rightContent == null || _selectedScene == null) return;

            var scroll = RightScroll;
            float keepScroll = scroll != null ? ScrollOffsetFromTop(scroll) : 0f;

            ClearContentDetached(_rightContent);
            BuildRunsContent(_selectedScene);
            ForceLayout(_rightContent);

            if (scroll != null)
                RestoreScrollOffsetFromTop(scroll, keepScroll);
        }

        private void AddRouteGroup(Transform parent, RouteReplayHistory route,
            bool stripe, int timeColW)
        {
            int headerH = RH + 2;
            int totalH = headerH + route.Snapshots.Count * RH;

            var group = MakeGO("RouteGroup", parent);
            Img(group, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            var le = group.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = totalH;

            AddRouteHeader(group.transform, route, headerH);

            for (int i = 0; i < route.Snapshots.Count; i++)
                AddSnapshotRow(group.transform, route, route.Snapshots[i], i, headerH,
                    timeColW);
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
            bool clearPending = _routeClearConfirmKey == RouteConfirmKey(key);
            MakeButton(row.transform, "ClearRoute", clearPending ? "Sure?" : "Clear",
                UIStyle.FontSizeBtn,
                clearPending ? UIStyle.Text : UIStyle.Red,
                clearPending ? UIStyle.Red with { a = 0.55f } : UIStyle.BtnBg(UIStyle.Red),
                RW - clearW - M, btnY, clearW, btnH,
                () => OnRouteClearClicked(key));

            int labelRight = clearW + M;
#if SILKSONG_BUILD
            // Warp button — shown for any route whose entry transition is
            // known. Lands the player in the previous room at a door leading
            // into the run room. Placed left of Clear.
            if (QuickWarp.CanWarp(key))
            {
                int warpW = UIStyle.W(44);
                int sp = UIStyle.Gap;
                RoomKey warpKey = key;
                MakeButton(row.transform, "WarpRoute", "Warp",
                    UIStyle.FontSizeBtn, UIStyle.Green, UIStyle.BtnBg(UIStyle.Green),
                    RW - clearW - M - warpW - sp, btnY, warpW, btnH,
                    () => OnRouteWarpClicked(warpKey));
                labelRight += warpW + sp;
            }
#endif

            string from = string.IsNullOrEmpty(route.Key.EntryFromScene)
                ? "spawn" : route.Key.EntryFromScene;
            MakeLbl(row.transform, from + " to " + route.Key.ExitToScene,
                UIStyle.FontSizeRow, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: RW - labelRight - M * 2, h: h);
        }

        /// <summary>Stable string identity for a route's two-click Clear
        /// confirm (RoomKey itself isn't used as the pending marker so the
        /// field can be a simple nullable string like _deleteConfirmId).</summary>
        private static string RouteConfirmKey(RoomKey key) =>
            key.SceneName + "|" + key.EntryFromScene + "|" + key.ExitToScene;

        private void OnRouteClearClicked(RoomKey key)
        {
            if (_routeClearConfirmKey == RouteConfirmKey(key))
            {
                _routeClearConfirmKey = null;
                DeleteRoute(key);
            }
            else
            {
                _routeClearConfirmKey = RouteConfirmKey(key);
                RebuildRunsContentOnly();
            }
        }

        private void AddSnapshotRow(Transform parent, RouteReplayHistory route,
            ReplaySnapshot snapshot, int index, int headerH, int timeColW)
        {
            int h = RH;
            int top = headerH + index * RH;

            bool playbackOn = SelectionState?.IsPlaybackSelected(snapshot.SnapshotId) ?? false;
            bool pendingDelete = _deleteConfirmId == snapshot.SnapshotId;

            Color rowBg = playbackOn
                ? UIStyle.Gold with { a = 0.08f }
                : (index % 2 == 1 ? UIStyle.Surface with { a = 0.35f } : Color.clear);

            var row = MakeGO("Snap", parent);
            Img(row, rowBg);
            Rect(row, 0, top, RW, h);

            RoomKey rowKey = route.Key;
            string rowSnapshotId = snapshot.SnapshotId;
            Btn(row, () => ToggleSnapshotPlayback(rowKey, rowSnapshotId));
            AddHoverEffect(row);

            int x = M;
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            // Ghost-active affordance: the run currently playing as the
            // ghost gets a gold caret (right-pointing triangle) tucked
            // into the row's left margin, plus the gold row tint and gold
            // label. The caret lives entirely inside the margin so it
            // costs no layout space; inactive rows are untouched.
            if (playbackOn)
            {
                int markH = UIStyle.H(10);
                var mark = MakeGO("Playing", row.transform);
                var markImg = mark.AddComponent<RawImage>();
                markImg.texture = PlayMarkerTexture();
                markImg.color = UIStyle.Gold;
                markImg.raycastTarget = false;
                Rect(mark, 0, (h - markH) / 2, M - 1, markH);
            }

            // Ghost color swatch — a bordered chip that opens the color
            // picker for this run. Solid, bright-bordered fill = custom
            // color; dimmed fill = following the global color.
            int swatchS = UIStyle.H(16);
            var swatch = MakeGO("Color", row.transform);
            Img(swatch, snapshot.HasVisualOverride
                ? UIStyle.Text with { a = 0.75f }
                : UIStyle.Overlay with { a = 0.9f });
            Rect(swatch, x, (h - swatchS) / 2, swatchS, swatchS);

            Color resolved = GetResolvedSnapshotColor(snapshot);
            var swatchFill = MakeGO("Fill", swatch.transform);
            Img(swatchFill, new Color(resolved.r, resolved.g, resolved.b, 1f));
            Rect(swatchFill, 1, 1, swatchS - 2, swatchS - 2);
            swatchFill.GetComponent<Graphic>().raycastTarget = false;

            string swId = snapshot.SnapshotId;
            RoomKey swKey = route.Key;
            Btn(swatch, () => OpenSnapshotColorPicker(swatch, swKey, swId));
            AddButtonHover(swatch);
            AttachTooltip(swatch, snapshot.HasVisualOverride
                ? ColorHex(resolved)
                : ColorHex(resolved) + " (global)");
            x += swatchS + M;

            // --- Right side: geometry comes from RowRightCluster.

            var cluster = RowRightCluster.Begin(RW);

            // Delete button (two-click confirm). Fixed width so the row
            // doesn't shift when the label flips to the confirm state.
            int delW = UIStyle.W(44);
            string delId = snapshot.SnapshotId;
            RoomKey delKey = route.Key;
            Color delBg = pendingDelete
                ? UIStyle.Red with { a = 0.55f }
                : UIStyle.BtnBg(UIStyle.Red);
            Color delFg = pendingDelete ? UIStyle.Text : UIStyle.Red;
            MakeButton(row.transform, "Del", pendingDelete ? "Sure?" : "Del",
                UIStyle.FontSizeBtn, delFg, delBg,
                cluster.AddButton(delW), btnY, delW, btnH,
                () => OnSnapshotDeleteClicked(delKey, delId));

            int copyW = UIStyle.W(40);
            string copyId = snapshot.SnapshotId;
            RoomKey copyKey = route.Key;
            MakeButton(row.transform, "Copy", "Copy",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                cluster.AddButton(copyW), btnY, copyW, btnH,
                () => CopyReplay(copyKey, copyId));

            // Camera-follow button, leftmost of the button cluster; one run
            // at a time can hold the follow slot, and following also enables
            // the ghost.
            int camW = btnH;
            int camX = cluster.AddButton(camW);
            bool followOn =
                SelectionState?.CameraFollowSnapshotId == snapshot.SnapshotId;

            var camBtn = MakeGO("CamFollow", row.transform);
            Img(camBtn, followOn
                ? UIStyle.BtnBgStrong(UIStyle.Gold)
                : UIStyle.Overlay);
            Rect(camBtn, camX, btnY, camW, btnH);

            var camIcon = MakeGO("Icon", camBtn.transform);
            var camImg = camIcon.AddComponent<RawImage>();
            camImg.texture = CameraMarkerTexture();
            camImg.color = followOn ? UIStyle.Gold : UIStyle.Subtext;
            camImg.raycastTarget = false;
            int camIconS = UIStyle.H(14);
            Rect(camIcon, (camW - camIconS) / 2, (btnH - camIconS) / 2,
                camIconS, camIconS);

            string camId = snapshot.SnapshotId;
            RoomKey camKey = route.Key;
            Btn(camBtn, () => OnCameraFollowClicked(camKey, camId));
            AddButtonHover(camBtn);
            AttachTooltip(camBtn, followOn
                ? "Camera following this run"
                : "Follow with camera. For full ghost visibility, use \"Deactivate Visual Masks\" in debug mod.");

            int timeX = cluster.AddTime(timeColW);
            MakeLbl(row.transform, TimeUtil.Format(snapshot.TotalTime),
                UIStyle.FontSizeSm, UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeColW, h: h);

            // Label — "#N"
            int labelW = cluster.LabelEnd - x;
            Color labelColor = playbackOn ? UIStyle.Gold : UIStyle.Subtext;

            string labelText = "#" + (index + 1);

            MakeLbl(row.transform, labelText,
                UIStyle.FontSizeRow, labelColor, TextAnchor.MiddleLeft,
                x: x, w: labelW, h: h);
        }

        /// <summary>"#RRGGBB" for a color (alpha ignored).</summary>
        private static string ColorHex(Color c) =>
            "#" + Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f).ToString("X2")
                + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f).ToString("X2")
                + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f).ToString("X2");

        private void OnSnapshotDeleteClicked(RoomKey key, string snapshotId)
        {
            if (_deleteConfirmId == snapshotId)
            {
                _deleteConfirmId = null;
                DeleteSnapshot(key, snapshotId);
            }
            else
            {
                // Arming the confirm only restyles this row - the lightweight
                // rebuild, same as the route-clear confirm above.
                _deleteConfirmId = snapshotId;
                if (_selectedScene != null && _activeTab == TabKind.Runs)
                    RebuildRunsContentOnly();
            }
        }
    }
}