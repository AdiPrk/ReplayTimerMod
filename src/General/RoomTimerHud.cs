using BepInEx.Logging;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ─────────────────────────────────────────────────────────────────────────
    // RoomTimerHUD – Savestate-driven room timer with PB comparison.
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomTimerHUD
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTimerHUD");

        private const int MARGIN_X = 8;
        private const int MARGIN_Y = 8; 

        private enum HudState { Hidden, Ready, Running, Finished }
        private HudState _state = HudState.Hidden;

        private float? _entryPbTime = null;
        private float  _finishTime  = 0f;
        private float? _delta       = null;
        private bool   _isNewPb     = false;

        private GameObject?  _canvasGO;
        private GameObject?  _readyGO;
        private GameObject?  _timerRootGO;

        private Text? _timerText;
        private Text? _deltaText;
        private Text? _pbLabelText;
        private Text? _pbTimeText;
        private Text? _rankText;

        private bool _setup = false;

        public bool IsRunning  => _state == HudState.Running;
        public bool IsFinished => _state == HudState.Finished;
        public bool IsReady    => _state == HudState.Ready;

        public void Disarm()
        {
            _state = HudState.Hidden;
            Log.LogInfo("[RoomTimerHUD] Disabled");
        }

        public void Setup()
        {
            BuildCanvas();

            RoomTracker.OnRoomEnter          += HandleRoomEnter;
            RoomTracker.OnRoomExit           += HandleRoomExit;
            RoomTracker.OnRecordingDiscarded += HandleDiscarded;

            _setup = true;
            Log.LogInfo("[RoomTimerHUD] Setup complete");
        }

        public void Teardown()
        {
            if (!_setup) return;

            RoomTracker.OnRoomEnter          -= HandleRoomEnter;
            RoomTracker.OnRoomExit           -= HandleRoomExit;
            RoomTracker.OnRecordingDiscarded -= HandleDiscarded;

            if (_canvasGO != null) Object.Destroy(_canvasGO);
            _setup = false;
        }

        public void Tick(bool shouldTick)
        {
            if (!_setup || _canvasGO == null) return;
            Object.DontDestroyOnLoad(_canvasGO);

            bool shouldShow = GhostSettings.TimerHudEnabled && !IsPaused();
            if (!shouldShow)
            {
                if (!GhostSettings.TimerHudEnabled && _state != HudState.Hidden)
                    _state = HudState.Hidden;

                if (_canvasGO.activeSelf) _canvasGO.SetActive(false);
                return;
            }

            if (_state == HudState.Hidden && GhostSettings.TimerHudEnabled)
            {
                _state = HudState.Ready;
            }

            if (!_canvasGO.activeSelf) _canvasGO.SetActive(true);

            switch (_state)
            {
                case HudState.Hidden:
                    SetReadyVisible(false);
                    SetTimerVisible(false);
                    break;

                case HudState.Ready:
                    SetReadyVisible(true);
                    SetTimerVisible(false);
                    break;

                case HudState.Running:
                    SetReadyVisible(false);
                    SetTimerVisible(true);
                    RefreshRunning();
                    break;

                case HudState.Finished:
                    SetReadyVisible(false);
                    SetTimerVisible(true);
                    break;
            }
        }

        private void HandleRoomEnter(string sceneName, string entryFromScene)
        {
            if (_rankText != null) _rankText.gameObject.SetActive(false);
            if (_state != HudState.Ready) return;

            _entryPbTime = BestPBForEntry(sceneName, entryFromScene);
            _delta       = null;
            _isNewPb     = false;
            _state       = HudState.Running;
            RefreshRunning();
        }

        private void HandleRoomExit(string sceneName, string entryFromScene,
            string exitToScene, float lrTime)
        {
            if (_state != HudState.Running) return;

            _finishTime = lrTime;

            if (_entryPbTime.HasValue)
            {
                _delta   = lrTime - _entryPbTime.Value;
                _isNewPb = _delta.Value < 0f;
            }
            else
            {
                _delta   = null;
                _isNewPb = true;
            }

            RefreshFinished();
            _state = HudState.Finished;
        }

        private void HandleDiscarded()
        {
            if (_rankText != null) _rankText.gameObject.SetActive(false);

            if (GhostSettings.TimerHudEnabled)
            {
                _state = HudState.Ready;
            }
        }

        private void RefreshRunning()
        {
            if (_timerText != null)
            {
                _timerText.text  = TimeUtil.Format(RoomTracker.CurrentRoomTime);
                _timerText.color = UIStyle.Text;
            }

            if (_deltaText != null) _deltaText.text = "";

            RefreshPbRow(_entryPbTime, highlightGold: false);
        }

        private void RefreshFinished()
        {
            if (_timerText != null)
            {
                _timerText.text  = TimeUtil.Format(_finishTime);
                _timerText.color = _isNewPb ? UIStyle.Gold : UIStyle.Text;
            }

            if (_deltaText != null)
            {
                if (_delta.HasValue)
                {
                    float d = _delta.Value;
                    if (_isNewPb)
                    {
                        _deltaText.text  = FormatDelta(d);
                        _deltaText.color = UIStyle.Gold;
                    }
                    else if (Mathf.Abs(d) < 0.005f)
                    {
                        _deltaText.text  = "+-0.00";
                        _deltaText.color = UIStyle.Subtext;
                    }
                    else
                    {
                        _deltaText.text  = FormatDelta(d);
                        _deltaText.color = UIStyle.Red;
                    }
                }
                else
                {
                    _deltaText.text  = "1st";
                    _deltaText.color = UIStyle.Accent;
                }
            }

            float? displayPb = _isNewPb ? _finishTime : _entryPbTime;
            RefreshPbRow(displayPb, highlightGold: _isNewPb);
        }

        private void RefreshPbRow(float? pbTime, bool highlightGold)
        {
            if (pbTime.HasValue)
            {
                if (_pbLabelText != null)
                {
                    _pbLabelText.text  = "PB";
                    _pbLabelText.color = highlightGold ? UIStyle.Gold : UIStyle.Subtext;
                }
                if (_pbTimeText != null)
                {
                    _pbTimeText.text  = TimeUtil.Format(pbTime.Value);
                    _pbTimeText.color = UIStyle.Gold;
                }
            }
            else
            {
                Color dim = new Color(UIStyle.Subtext.r, UIStyle.Subtext.g,
                                      UIStyle.Subtext.b, 0.6f); 
                if (_pbLabelText != null) { _pbLabelText.text = "PB";       _pbLabelText.color = dim; }
                if (_pbTimeText  != null) { _pbTimeText.text  = "--:--.--"; _pbTimeText.color  = dim; }
            }
        }

        private void BuildCanvas()
        {
            _canvasGO = new GameObject("RoomTimerHUD_Canvas");
            Object.DontDestroyOnLoad(_canvasGO);

            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32766;

            _canvasGO.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            _canvasGO.AddComponent<GraphicRaycaster>();
            _canvasGO.SetActive(false);

            BuildReadyIndicator(_canvasGO.transform);
            BuildTimerWidget(_canvasGO.transform);
        }

        private void BuildReadyIndicator(Transform canvasRoot)
        {
            int mX = UIStyle.W(MARGIN_X);
            int mY = UIStyle.H(MARGIN_Y);

            _readyGO = new GameObject("ReadyIndicator");
            _readyGO.transform.SetParent(canvasRoot, false);

            var rt = _readyGO.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(mX, -mY);
            rt.sizeDelta        = new Vector2(UIStyle.W(82), UIStyle.H(22));

            var lbl = MakeLbl(_readyGO.transform, "Ready",
                UIStyle.H(14), UIStyle.Accent, TextAnchor.MiddleLeft,
                x: 0, y: 0, w: UIStyle.W(82), h: UIStyle.H(22));
            lbl.alignByGeometry = false;

            _readyGO.SetActive(false);
        }

        private void BuildTimerWidget(Transform canvasRoot)
        {
            int timerFontSz = UIStyle.H(18); 
            int deltaFontSz = UIStyle.H(14); 
            int pbFontSz    = UIStyle.H(12);

            int timerRowH = UIStyle.H(22);
            int pbRowH    = UIStyle.H(16);
            int rowGap    = UIStyle.H(0);

            // Left-aligned grid definitions with adjusted width
            int timerW  = UIStyle.W(60); 
            int deltaW  = UIStyle.W(70);
            int colGap  = UIStyle.W(6);  
            int pbLblW  = UIStyle.W(20);
            int pbTimeW = UIStyle.W(60);

            int innerW = timerW + colGap + deltaW;
            int innerH = timerRowH + rowGap + pbRowH + rowGap + pbRowH;

            int mX = UIStyle.W(MARGIN_X);
            int mY = UIStyle.H(MARGIN_Y);

            _timerRootGO = new GameObject("TimerWidget");
            _timerRootGO.transform.SetParent(canvasRoot, false);

            var rootRt = _timerRootGO.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0f, 1f);
            rootRt.pivot     = new Vector2(0f, 1f);
            rootRt.anchoredPosition = new Vector2(mX, -mY);
            rootRt.sizeDelta        = new Vector2(innerW, innerH);
            _timerRootGO.SetActive(false);

            int timerRowTop = 0;
            int timerX      = 0;
            int deltaX      = timerX + timerW + colGap;

            _timerText = MakeLbl(_timerRootGO.transform, "0:00.00",
                timerFontSz, UIStyle.Text, TextAnchor.LowerLeft,
                x: timerX, y: timerRowTop, w: timerW, h: timerRowH);
            _timerText.alignByGeometry    = false;
            _timerText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _deltaText = MakeLbl(_timerRootGO.transform, "",
                deltaFontSz, UIStyle.Gold, TextAnchor.LowerLeft,
                x: deltaX, y: timerRowTop, w: deltaW, h: timerRowH);
            _deltaText.alignByGeometry    = false;
            _deltaText.horizontalOverflow = HorizontalWrapMode.Overflow;

            int pbRowTop = timerRowTop + timerRowH + rowGap;
            int pbLblX   = 0;
            int pbTimeX  = pbLblX + pbLblW;

            _pbLabelText = MakeLbl(_timerRootGO.transform, "PB",
                pbFontSz, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: pbLblX, y: pbRowTop, w: pbLblW, h: pbRowH);
            _pbLabelText.alignByGeometry = false;

            _pbTimeText = MakeLbl(_timerRootGO.transform, "--:--.--",
                pbFontSz, UIStyle.Gold, TextAnchor.MiddleLeft,
                x: pbTimeX, y: pbRowTop, w: pbTimeW, h: pbRowH);
            _pbTimeText.alignByGeometry = false;

            int rankRowTop = pbRowTop + pbRowH + rowGap;

            _rankText = MakeLbl(_timerRootGO.transform, "",
                pbFontSz, UIStyle.Accent, TextAnchor.MiddleLeft,
                x: 0, y: rankRowTop, w: innerW, h: pbRowH);
            _rankText.alignByGeometry = false;
            _rankText.gameObject.SetActive(false);
        }

        private static Text MakeLbl(Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor,
            int x, int y, int w, int h)
        {
            var go = new GameObject("Lbl");
            go.transform.SetParent(parent, false);

            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable   = false;
            cg.blocksRaycasts = false;

            var t = go.AddComponent<Text>();
            t.font              = UIStyle.Arial;
            t.fontSize          = fontSize;
            t.color             = color;
            t.alignment         = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow  = VerticalWrapMode.Truncate;
            t.text              = text;
            t.alignByGeometry   = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            outline.effectDistance = new Vector2(1, -1);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta        = new Vector2(w, h);

            return t;
        }

        private void SetReadyVisible(bool v)
        {
            if (_readyGO != null && _readyGO.activeSelf != v)
                _readyGO.SetActive(v);
        }

        private void SetTimerVisible(bool v)
        {
            if (_timerRootGO != null && _timerRootGO.activeSelf != v)
                _timerRootGO.SetActive(v);
        }

        private static float? BestPBForEntry(string sceneName, string entryFromScene)
        {
            float? best = null;
            foreach (var kvp in PBManager.AllPBs())
            {
                if (kvp.Key.SceneName != sceneName || kvp.Key.EntryFromScene != entryFromScene)
                    continue;
                float t = kvp.Value.TotalTime;
                if (!best.HasValue || t < best.Value) best = t;
            }
            return best;
        }

        private static string FormatDelta(float d)
        {
            string sign = d >= 0f ? "+" : "-";
            float  abs  = Mathf.Abs(d);
            int    cs   = Mathf.RoundToInt(abs * 100f);
            int    min  = cs / 6000;
            int    sec  = (cs / 100) % 60;
            int    rem  = cs % 100;
            return min > 0
                ? $"{sign}{min}:{sec:00}.{rem:00}"
                : $"{sign}{sec}.{rem:00}";
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

        /// <summary>
        /// Shows the global rank below the PB row. Called from the main thread
        /// when the upload response arrives with rank data.
        /// </summary>
        public void ShowRank(RankInfo rankInfo)
        {
            // Only show rank if we're in the Finished state and the rank
            // matches the room we just finished
            if (_state != HudState.Finished) return;
            if (_rankText == null) return;

            _rankText.text = $"#{rankInfo.Rank} / {rankInfo.TotalRunners}";
            _rankText.color = UIStyle.Accent;
            _rankText.gameObject.SetActive(true);
        }
    }
}