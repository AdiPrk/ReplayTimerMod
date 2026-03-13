using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ─────────────────────────────────────────────────────────────────────────
    // ReplayUI — pause-only two-column replay browser.
    //
    // Left column  : alphabetical list of scenes that have PBs.
    //                Click a scene to populate the right column.
    //
    // Right column : all recorded routes for the selected scene.
    //   Sub-header : scene name  |  [Paste]  [Clear all]
    //   Each row   : route  time  [Copy]  [X]
    //
    // [Paste]     reads GUIUtility.systemCopyBuffer, decodes it with
    //             ReplayShareEncoder, and imports the result unconditionally.
    // [Clear all] deletes every entry for the selected scene.
    // [Copy]      encodes the entry and writes it to the clipboard.
    // [X]         deletes that single entry.
    //
    // Visibility: canvas is disabled every frame when not paused.
    // ─────────────────────────────────────────────────────────────────────────
    public class ReplayUI
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayUI");

        // ── State ─────────────────────────────────────────────────────────────
        private bool isSetup = false;
        private bool expanded = false;
        private string? selectedScene = null;

        // ── Unity objects ─────────────────────────────────────────────────────
        private GameObject? canvasGO;
        private GameObject? tabGO;
        private GameObject? panelGO;

        private Transform? leftContent;
        private Transform? rightContent;
        private Text? rightHeader;    // scene name in right sub-header
        private Text? pasteStatus;   // brief feedback next to [Paste]

        // ── Pixel sizes ───────────────────────────────────────────────────────
        private int PW, PH;   // panel width/height
        private int LW, RW;   // left/right column widths
        private int RH;       // row height
        private int M;        // general margin
        private int TW, TH;   // tab width/height
        private int HDR;      // panel header height
        private int SUBHDR;   // right-column sub-header height

        // ═════════════════════════════════════════════════════════════════════
        // SETUP
        // ═════════════════════════════════════════════════════════════════════
        public void Setup()
        {
            UIStyle.LoadFonts();

            M = UIStyle.H(8);
            RH = UIStyle.H(26);
            TW = UIStyle.W(44);
            TH = UIStyle.H(28);
            HDR = UIStyle.H(34);
            SUBHDR = UIStyle.H(28);
            PW = UIStyle.W(680);
            PH = UIStyle.H(540);
            LW = UIStyle.W(200);
            RW = PW - LW - 1;

            // Build the canvas disabled — Tick() enables it only while paused
            canvasGO = new GameObject("ReplayModCanvas");
            Object.DontDestroyOnLoad(canvasGO);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.SetActive(false);   // <-- stays off until first pause

            BuildTab();
            BuildPanel();

            isSetup = true;
            Log.LogInfo("[ReplayUI] Setup complete");
        }

        // ═════════════════════════════════════════════════════════════════════
        // TICK — every frame, sets visibility from pause state
        // ═════════════════════════════════════════════════════════════════════
        public void Tick()
        {
            if (!isSetup) return;

            bool paused = IsPaused();
            canvasGO!.SetActive(paused);

            if (!paused)
            {
                expanded = false;   // collapse when unpausing
                return;
            }

            tabGO!.SetActive(true);
            panelGO!.SetActive(expanded);
        }

        private static bool IsPaused()
        {
            try
            {
                return GameManager.instance != null
                    && GameManager.instance.ui != null
                    && GameManager.instance.ui.uiState == UIState.PAUSED;
            }
            catch { return false; }
        }

        public void OnPBUpdated()
        {
            if (expanded && IsPaused())
            {
                RebuildLeft();
                if (selectedScene != null) RebuildRight(selectedScene);
            }
        }

        public void Hide() { /* kept for plugin compatibility */ }
        public void Toggle() => TogglePanel();

        // ═════════════════════════════════════════════════════════════════════
        // BUILD — tab button (bottom-left ≡)
        // ═════════════════════════════════════════════════════════════════════
        private void BuildTab()
        {
            tabGO = MakeGO("ReplayTab", canvasGO!.transform);
            Img(tabGO, UIStyle.Surface);
            Btn(tabGO, TogglePanel);

            var rt = tabGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M);
            rt.sizeDelta = new Vector2(TW, TH);

            MakeLbl(tabGO.transform, "≡", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // BUILD — full panel
        // ═════════════════════════════════════════════════════════════════════
        private void BuildPanel()
        {
            panelGO = MakeGO("ReplayPanel", canvasGO!.transform);
            Img(panelGO, UIStyle.Base);

            var rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M + TH + M);
            rt.sizeDelta = new Vector2(PW, PH);

            // ── Panel header ──────────────────────────────────────────────────
            var hdr = MakeGO("Header", panelGO.transform);
            Img(hdr, UIStyle.Surface);
            Rect(hdr, 0, 0, PW, HDR);

            MakeLbl(hdr.transform, "Replay Times", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleLeft, x: M, w: PW - HDR, h: HDR);

            var collBtn = MakeGO("Collapse", hdr.transform);
            Img(collBtn, UIStyle.Overlay);
            Btn(collBtn, TogglePanel);
            Rect(collBtn, PW - HDR, 0, HDR, HDR);
            MakeLbl(collBtn.transform, "—", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);

            HLine(panelGO.transform, 0, HDR, PW);

            // ── Body ──────────────────────────────────────────────────────────
            int bodyY = HDR + 1;
            int bodyH = PH - bodyY;

            leftContent = BuildScrollArea(panelGO.transform,
                "LeftScroll", 0, bodyY, LW, bodyH);

            VLine(panelGO.transform, LW, bodyY, bodyH);

            // Right sub-header: scene name | [Paste] [Clear all]
            BuildRightSubHeader(bodyY);

            HLine(panelGO.transform, LW + 1, bodyY + SUBHDR, RW);

            rightContent = BuildScrollArea(panelGO.transform,
                "RightScroll", LW + 1, bodyY + SUBHDR + 1,
                RW, bodyH - SUBHDR - 1);
        }

        private void BuildRightSubHeader(int bodyY)
        {
            var rhdr = MakeGO("RightSubHeader", panelGO!.transform);
            Img(rhdr, UIStyle.Surface);
            Rect(rhdr, LW + 1, bodyY, RW, SUBHDR);

            // [Clear all] — far right
            int clearW = UIStyle.W(70);
            int btnH = UIStyle.H(20);
            int btnY = (SUBHDR - btnH) / 2;

            var clearBtn = MakeGO("ClearAll", rhdr.transform);
            Img(clearBtn, UIStyle.Red with { a = 0.25f });
            Btn(clearBtn, OnClearAllClicked);
            Rect(clearBtn, RW - clearW - M, btnY, clearW, btnH);
            MakeLbl(clearBtn.transform, "Clear all", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            // [Paste] — left of [Clear all]
            int pasteW = UIStyle.W(52);
            var pasteBtn = MakeGO("Paste", rhdr.transform);
            Img(pasteBtn, UIStyle.Accent with { a = 0.25f });
            Btn(pasteBtn, OnPasteClicked);
            Rect(pasteBtn, RW - clearW - M - pasteW - M, btnY, pasteW, btnH);
            MakeLbl(pasteBtn.transform, "Paste", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // Paste status label (brief feedback, e.g. "✓ Imported" or "✗ Invalid")
            int statusX = RW - clearW - M - pasteW - M - UIStyle.W(130) - M;
            pasteStatus = MakeLbl(rhdr.transform, "", UIStyle.FontSizeSm - 2,
                UIStyle.Subtext, TextAnchor.MiddleRight,
                x: statusX, w: UIStyle.W(130), h: SUBHDR);

            // Scene name — fills the remaining left space
            int nameW = statusX - M;
            rightHeader = MakeLbl(rhdr.transform, "Select a room",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M, w: nameW, h: SUBHDR);
        }

        // ═════════════════════════════════════════════════════════════════════
        // SCROLL AREA BUILDER
        // ═════════════════════════════════════════════════════════════════════
        private static Transform BuildScrollArea(Transform parent, string name,
            float x, float y, float w, float h)
        {
            var sr = MakeGO(name, parent);
            Img(sr, Color.clear);
            Rect(sr, x, y, w, h);

            var vp = MakeGO("Viewport", sr.transform);
            vp.AddComponent<RectMask2D>();
            var vpRT = vp.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;

            var ct = MakeGO("Content", vp.transform);
            var ctRT = ct.GetComponent<RectTransform>();
            ctRT.anchorMin = new Vector2(0, 1);
            ctRT.anchorMax = new Vector2(1, 1);
            ctRT.pivot = new Vector2(0.5f, 1f);
            ctRT.offsetMin = ctRT.offsetMax = Vector2.zero;
            ctRT.sizeDelta = Vector2.zero;

            var csf = ct.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var vlg = ct.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1;

            var scroll = sr.AddComponent<ScrollRect>();
            scroll.content = ctRT;
            scroll.viewport = vpRT;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;

            return ct.transform;
        }

        // ═════════════════════════════════════════════════════════════════════
        // TOGGLE
        // ═════════════════════════════════════════════════════════════════════
        private void TogglePanel()
        {
            expanded = !expanded;
            panelGO!.SetActive(expanded);
            if (expanded) RebuildLeft();
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEFT COLUMN — scene list
        // ═════════════════════════════════════════════════════════════════════
        private void RebuildLeft()
        {
            if (leftContent == null) return;
            ClearContent(leftContent);

            var scenes = PBManager.AllPBs()
                .Select(p => p.Key.SceneName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            // If the previously selected scene no longer exists, deselect it
            if (selectedScene != null && !scenes.Contains(selectedScene))
            {
                selectedScene = null;
                ClearRight();
            }

            if (scenes.Count == 0)
            {
                AddMessageRow(leftContent, "No replays yet.");
            }
            else
            {
                foreach (var s in scenes)
                    AddSceneRow(leftContent, s);
            }

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
            RebuildLeft();           // refresh highlight
            RebuildRight(scene);
        }

        // ═════════════════════════════════════════════════════════════════════
        // RIGHT COLUMN — entries for selected scene
        // ═════════════════════════════════════════════════════════════════════
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
            {
                AddMessageRow(rightContent, "No entries.");
            }
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

        // ── Entry row: route  time  [Copy]  [X] ──────────────────────────────
        private void AddEntryRow(Transform parent, RoomKey key, float time, bool stripe)
        {
            int h = RH;

            var row = MakeGO("EntryRow", parent);
            Img(row, stripe ? UIStyle.Surface : Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = h;

            // Button sizes
            int xBtnW = UIStyle.W(22);
            int copyW = UIStyle.W(46);
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            // [X] delete button — far right
            var xBtn = MakeGO("Delete", row.transform);
            Img(xBtn, UIStyle.Red with { a = 0.20f });
            RoomKey dk = key;
            Btn(xBtn, () => DeleteEntry(dk));
            Rect(xBtn, RW - xBtnW - M, btnY, xBtnW, btnH);
            MakeLbl(xBtn.transform, "✕", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            // [Copy] button — left of [X]
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
            MakeLbl(row.transform, FormatTime(time), UIStyle.FontSizeSm, UIStyle.Gold,
                TextAnchor.MiddleRight, x: timeX, w: timeW, h: h);

            // Route label — fills remaining left space
            string entry = string.IsNullOrEmpty(key.EntryFromScene) ? "spawn" : key.EntryFromScene;
            MakeLbl(row.transform, $"{entry} → {key.ExitToScene}",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M, w: timeX - M * 2, h: h);
        }

        private static void AddMessageRow(Transform parent, string msg)
        {
            var row = MakeGO("MsgRow", parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(40);
            MakeLbl(row.transform, msg, UIStyle.FontSizeSm, UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ACTIONS
        // ═════════════════════════════════════════════════════════════════════

        // Copy a single entry's encoded replay to clipboard
        private void CopyReplay(RoomKey key)
        {
            var pb = PBManager.GetPB(key);
            if (pb == null) { Log.LogWarning($"[ReplayUI] No PB for {key}"); return; }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.Encode(pb);
            Log.LogInfo($"[ReplayUI] Copied {key}");
        }

        // Delete a single entry, then refresh
        private void DeleteEntry(RoomKey key)
        {
            PBManager.DeletePB(key);
            if (selectedScene != null)
                RebuildRight(selectedScene);
            RebuildLeft();
        }

        // Clear every entry for the selected scene, then refresh
        private void OnClearAllClicked()
        {
            if (selectedScene == null) return;
            PBManager.DeleteScene(selectedScene);
            selectedScene = null;
            ClearRight();
            RebuildLeft();
        }

        // Read clipboard, decode, import — then refresh
        private void OnPasteClicked()
        {
            string clip = GUIUtility.systemCopyBuffer ?? "";
            if (string.IsNullOrWhiteSpace(clip))
            {
                ShowPasteStatus("✕ Clipboard empty", UIStyle.Red);
                return;
            }

            RecordedRoom? room = null;
            try { room = ReplayShareEncoder.Decode(clip); }
            catch { /* decode logs its own error */ }

            if (room == null)
            {
                ShowPasteStatus("✕ Invalid data", UIStyle.Red);
                return;
            }

            PBManager.ImportPB(room);

            // Select the imported scene and refresh both columns
            selectedScene = room.Key.SceneName;
            RebuildLeft();
            RebuildRight(selectedScene);

            ShowPasteStatus($"✓ {room.Key.SceneName}", UIStyle.Gold);
            Log.LogInfo($"[ReplayUI] Pasted {room.Key}");
        }

        private void ShowPasteStatus(string msg, Color color)
        {
            if (pasteStatus == null) return;
            pasteStatus.text = msg;
            pasteStatus.color = color;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PRIMITIVE HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private static void ClearContent(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        private static void ForceLayout(Transform content)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                content.GetComponent<RectTransform>());
        }

        private static GameObject MakeGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void Img(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = c;
        }

        private static void Btn(GameObject go, UnityEngine.Events.UnityAction action)
        {
            var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static void Rect(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void HLine(Transform parent, float x, float y, float w)
        {
            var go = MakeGO("HLine", parent);
            Img(go, UIStyle.Overlay);
            Rect(go, x, y, w, 1);
        }

        private static void VLine(Transform parent, float x, float y, float h)
        {
            var go = MakeGO("VLine", parent);
            Img(go, UIStyle.Overlay);
            Rect(go, x, y, 1, h);
        }

        // Non-interactable text label — CanvasGroup prevents it eating mouse events.
        private static Text MakeLbl(Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor,
            float x = 0, float y = 0, float w = 0, float h = 0,
            bool fill = false)
        {
            var go = MakeGO("Lbl", parent);
            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var t = go.AddComponent<Text>();
            t.font = UIStyle.Arial ?? UIStyle.Trajan;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.text = text;

            var rt = go.GetComponent<RectTransform>();
            if (fill)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(x, -y);
                rt.sizeDelta = new Vector2(w, h);
            }

            return t;
        }

        private static string FormatTime(float t)
        {
            int ms = (int)(t * 100) % 100;
            int s = (int)t % 60;
            int min = (int)t / 60;
            return $"{min}:{s:00}.{ms:00}";
        }
    }
}