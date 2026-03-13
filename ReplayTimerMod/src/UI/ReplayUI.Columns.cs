using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Left column — scene list ──────────────────────────────────────────

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
                foreach (var s in scenes)
                    AddSceneRow(leftContent, s);

            ForceLayout(leftContent);
        }

        private void AddSceneRow(Transform parent, string scene)
        {
            bool selected = scene == selectedScene;
            var row = MakeGO("SceneRow", parent);
            Img(row, selected ? UIStyle.Overlay : Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RH;
            Btn(row, () => SelectScene(scene));
            MakeLbl(row.transform, scene, UIStyle.FontSizeSm,
                selected ? UIStyle.Accent : UIStyle.Text,
                TextAnchor.MiddleLeft, x: M, fill: true);
        }

        private void SelectScene(string scene)
        {
            selectedScene = scene;
            RebuildLeft();
            RebuildRight(scene);
        }

        // ── Right column — entries for selected scene ─────────────────────────

        private void RebuildRight(string scene)
        {
            if (rightContent == null) return;
            if (rightHeader != null) rightHeader.text = scene;
            if (pasteStatus != null) pasteStatus.text = "";

            ClearContent(rightContent);

            var entries = PBManager.AllPBs()
                .Where(p => p.Key.SceneName == scene)
                .OrderBy(p => p.Key.EntryFromScene)
                .ThenBy(p => p.Key.ExitToScene)
                .ToList();

            if (entries.Count == 0)
                AddMessageRow(rightContent, "No entries.");
            else
            {
                bool stripe = false;
                foreach (var kvp in entries)
                {
                    AddEntryRow(rightContent, kvp.Key, kvp.Value.TotalTime, stripe);
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

        // ── Entry row: route  time  [Copy]  [✕] ──────────────────────────────

        private void AddEntryRow(Transform parent, RoomKey key, float time, bool stripe)
        {
            int h = RH;
            int xBtnW = UIStyle.W(22);
            int copyW = UIStyle.W(46);
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            var row = MakeGO("EntryRow", parent);
            Img(row, stripe ? UIStyle.Surface : Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = h;

            // [✕] delete — far right
            var xBtn = MakeGO("Delete", row.transform);
            Img(xBtn, UIStyle.Red with { a = 0.20f });
            RoomKey dk = key;
            Btn(xBtn, () => DeleteEntry(dk));
            Rect(xBtn, RW - xBtnW - M, btnY, xBtnW, btnH);
            MakeLbl(xBtn.transform, "✕", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            // [Copy] — left of [✕]
            var copyBtn = MakeGO("Copy", row.transform);
            Img(copyBtn, UIStyle.Accent with { a = 0.22f });
            RoomKey ck = key;
            Btn(copyBtn, () => CopyReplay(ck));
            Rect(copyBtn, RW - xBtnW - M - copyW - M, btnY, copyW, btnH);
            MakeLbl(copyBtn.transform, "Copy", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // Time — gold, left of [Copy]
            int timeW = UIStyle.W(60);
            int timeX = RW - xBtnW - M - copyW - M - timeW - M;
            MakeLbl(row.transform, TimeUtil.Format(time), UIStyle.FontSizeSm,
                UIStyle.Gold, TextAnchor.MiddleRight,
                x: timeX, w: timeW, h: h);

            // Route label — fills remaining left space
            string from = string.IsNullOrEmpty(key.EntryFromScene) ? "spawn" : key.EntryFromScene;
            MakeLbl(row.transform, $"{from} → {key.ExitToScene}",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M, w: timeX - M * 2, h: h);
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