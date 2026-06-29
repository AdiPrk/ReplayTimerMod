using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ─────────────────────────────────────────────────────────────────────────
    // RoomTimerHUD – Per-room timer with PB comparison.
    //
    // Each room gets its own self-contained "card" (timer + delta + PB + rank).
    // At most two cards are alive at once:
    //
    //   * Base mode (GhostSettings.ChainRoomTimers == false):
    //       One card in the left slot. Entering a new room resets it and starts
    //       a fresh timer — so the display always reflects the current room and
    //       never freezes on a finished time.
    //
    //   * Chained mode (GhostSettings.ChainRoomTimers == true):
    //       When the next room starts, the just-finished card stays in the left
    //       slot and a new running card drops in to its right. After ROLL_DELAY
    //       seconds OR the next transition (whichever comes first) the left card
    //       slides off to the left and the right card slides into the left slot,
    //       freeing the right slot for the room after that. Rolls forever,
    //       always bounded to two cards.
    //
    // Savestate loads / deaths / cheat-cancels (OnRecordingDiscarded) clear all
    // cards and return to the "Ready" indicator.
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomTimerHUD
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTimerHUD");

        private const int MARGIN_X = 8;
        private const int MARGIN_Y = 8;

        // How long both cards stay side-by-side before the older one rolls off,
        // when the next room hasn't already triggered the roll by transitioning.
        private const float ROLL_DELAY = 2.5f;

        // ── Per-room card ────────────────────────────────────────────────────
        private enum CardState { Running, Finished }

        private sealed class TimerCard
        {
            public GameObject     go = null!;
            public RectTransform  rt = null!;
            public Text timer  = null!;
            public Text delta  = null!;
            public Text pbLbl  = null!;
            public Text pbTime = null!;
            public Text rank   = null!;
            public Text status = null!;   // small tag, e.g. "not saved"

            public CardState state;

            // True  → display the live ticking timer (reads CurrentRoomTime).
            // False → hold whatever finished content is shown (non-chaining
            //         keeps the previous result on screen while the next room
            //         is timed in the background).
            public bool liveDisplay;

            // Identity of the room this card represents (for rank matching).
            public string scene     = "";
            public string entryFrom = "";
            public string exitTo    = "";

            public float? entryPb;     // best (counting) PB at room entry
            public float  finishTime;  // frozen time once finished
            public float? deltaVal;    // finishTime - entryPb (null = first run)
            public bool   isNewPb;
            public bool   counts;      // does this run count toward the PB highlight
            public bool   notSaved;    // backtrack skipped from saving → show tag

            // Slide animation (anchored X). targetX is the slot it's moving to.
            public float currentX;
            public float targetX;
            public bool  slidingOut;   // true → destroy once fully off-screen left
        }

        // ── Card list / orchestration ────────────────────────────────────────
        // _cards[0] is the leftmost (oldest) card; the last element is the
        // newest. _live is the single Running card that reads CurrentRoomTime.
        private readonly List<TimerCard> _cards = new List<TimerCard>(2);
        private TimerCard? _live;
        private bool  _rollPending;   // older card is waiting to roll out
        private float _rollTimer;

        // ── Layout (computed once in BuildCanvas) ────────────────────────────
        private int   _cardW, _cardH;
        private int   _slot0X, _slot1X, _offLeftX;
        private int   _marginY;
        private float _slideSpeed;

        // Saved sizing so each card is built identically.
        private int _timerFontSz, _deltaFontSz, _pbFontSz;
        private int _timerRowH, _pbRowH, _rowGap;
        private int _timerW, _deltaW, _colGap, _pbLblW, _pbTimeW;

        // ── Persistent UI ────────────────────────────────────────────────────
        private GameObject? _canvasGO;
        private GameObject? _readyGO;
        private Transform?  _cardParent;

        private bool _setup = false;

        // ── Cancellation banner (unchanged behaviour) ────────────────────────
        private enum BannerState { Hidden, SlidingIn, Visible, SlidingOut }
        private BannerState _bannerState = BannerState.Hidden;
        private float _bannerHoldTimer = 0f;
        private float _bannerY = 0f;
        private int   _bannerWidth = 0;
        private int   _bannerHeight = 0;

        private const float BannerSlideSpeed  = 1400f;
        private const float BannerHoldSeconds = 4.6f;
        private const int   BANNER_GAP_X = 16;

        private GameObject?    _bannerGO;
        private RectTransform? _bannerRt;
        private Text?          _bannerText;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        public void Disarm()
        {
            ClearAllCards();
            Log.LogInfo("[RoomTimerHUD] Disabled");
        }

        public void Setup()
        {
            BuildCanvas();

            RoomTracker.OnRoomEnter          += HandleRoomEnter;
            RoomTracker.OnRoomExit           += HandleRoomExit;
            RoomTracker.OnRecordingDiscarded += HandleDiscarded;
            RoomTracker.OnRunCancelled        += HandleRunCancelled;

            _setup = true;
            Log.LogInfo("[RoomTimerHUD] Setup complete");
        }

        public void Teardown()
        {
            if (!_setup) return;

            RoomTracker.OnRoomEnter          -= HandleRoomEnter;
            RoomTracker.OnRoomExit           -= HandleRoomExit;
            RoomTracker.OnRecordingDiscarded -= HandleDiscarded;
            RoomTracker.OnRunCancelled        -= HandleRunCancelled;

            if (_canvasGO != null) Object.Destroy(_canvasGO);
            _cards.Clear();
            _live = null;
            _setup = false;
        }

        // ── Frame update ──────────────────────────────────────────────────────
        public void Tick(bool shouldTick)
        {
            if (!_setup || _canvasGO == null) return;

            bool enabled      = GhostSettings.TimerHudEnabled;
            bool shouldShow   = enabled && !GameUiState.IsPaused();
            bool bannerActive = _bannerState != BannerState.Hidden;

            // Disabled entirely: drop everything (banner may still finish).
            if (!enabled && _cards.Count > 0) ClearAllCards();

            if (!shouldShow && !bannerActive)
            {
                if (_canvasGO.activeSelf) _canvasGO.SetActive(false);
                return;
            }

            if (!_canvasGO.activeSelf) _canvasGO.SetActive(true);

            if (shouldShow)
            {
                // A mode change can leave a stale second card around.
                if (!GhostSettings.ChainRoomTimers && _cards.Count > 1)
                    ForceCollapseToOne();

                AdvanceRoll();
                AnimateCards();

                if (_live != null && _live.liveDisplay)
                    RefreshRunning(_live);

                // Ready indicator only when nothing is on screen.
                SetReadyVisible(_cards.Count == 0);
                SetCardsVisible(true);
            }
            else
            {
                // Paused: freeze the cards (hide), keep canvas alive for banner.
                SetReadyVisible(false);
                SetCardsVisible(false);
            }

            UpdateBanner();
        }

        // ── RoomTracker handlers ───────────────────────────────────────────────
        private void HandleRoomEnter(string sceneName, string entryFromScene)
        {
            if (!GhostSettings.TimerHudEnabled) return;

            if (GhostSettings.ChainRoomTimers)
                EnterChaining(sceneName, entryFromScene);
            else
                EnterNonChaining(sceneName, entryFromScene);
        }

        // Non-chaining: a single card. The most recently FINISHED room's result
        // stays on screen; the next room is timed in the background and only
        // replaces the display once it finishes. The live ticking timer is
        // shown only when nothing is being held (the first room after a reset),
        // matching the original pre-chaining behaviour.
        private void EnterNonChaining(string scene, string entry)
        {
            if (_cards.Count > 1) ForceCollapseToOne();

            TimerCard card = _cards.Count > 0 ? _cards[0] : CreateCard(_slot0X);
            PlaceInstant(card, _slot0X);
            card.slidingOut = false;

            if (card.state == CardState.Finished)
            {
                // Hold the previous result; just retarget what we're timing.
                card.scene       = scene;
                card.entryFrom   = entry;
                card.exitTo      = "";
                card.entryPb     = BestPBForEntry(scene, entry);
                card.liveDisplay = false;
            }
            else
            {
                InitRunning(card, scene, entry); // live display for the first room
            }
            _live = card;
        }

        // Chained: the just-finished card stays in the left slot while a new
        // running card drops into the right slot and rolls the old one off.
        private void EnterChaining(string scene, string entry)
        {
            // A previous roll still in flight: finish it instantly so the
            // right slot is free for the new room.
            if (_cards.Count >= 2)
                ForceCompleteRoll();

            if (_cards.Count == 0)
            {
                TimerCard first = CreateCard(_slot0X);
                InitRunning(first, scene, entry);
                _live = first;
                return;
            }

            TimerCard incoming = CreateCard(_slot1X);
            InitRunning(incoming, scene, entry);
            _live = incoming;

            _rollPending = true;
            _rollTimer   = ROLL_DELAY;
        }

        private void HandleRoomExit(string sceneName, string entryFromScene,
            string exitToScene, float lrTime)
        {
            if (_live == null) return;

            // Always freeze and show the time — including same-transition
            // backtracks. The backtrack options only affect the PB comparison
            // and the "not saved" tag, handled inside FinishCard.
            FinishCard(_live, exitToScene, lrTime);
            _live.liveDisplay = false;
            _live = null;

            // Chained mode: the live room ended, so push the older card out now
            // ("...or transition, whichever's first").
            if (_rollPending) TriggerRoll();
        }

        private void HandleDiscarded()
        {
            ClearAllCards();
        }

        // ── Roll mechanics ─────────────────────────────────────────────────────
        private void AdvanceRoll()
        {
            if (!_rollPending) return;
            _rollTimer -= Time.unscaledDeltaTime;
            if (_rollTimer <= 0f) TriggerRoll();
        }

        /// <summary>Begins sliding the oldest card off-screen and the newer card
        /// into the left slot. Removal happens in AnimateCards once off-screen.</summary>
        private void TriggerRoll()
        {
            _rollPending = false;
            if (_cards.Count < 2) return;

            TimerCard older = _cards[0];
            TimerCard newer = _cards[1];

            older.slidingOut = true;
            older.targetX    = _offLeftX;
            newer.targetX    = _slot0X;
        }

        /// <summary>Instantly completes a pending/in-flight roll: drops the oldest
        /// card and snaps the newer one into the left slot.</summary>
        private void ForceCompleteRoll()
        {
            _rollPending = false;
            while (_cards.Count > 1)
            {
                TimerCard older = _cards[0];
                _cards.RemoveAt(0);
                if (older.go != null) Object.Destroy(older.go);
                if (ReferenceEquals(_live, older)) _live = null;
            }
            if (_cards.Count == 1)
                PlaceInstant(_cards[0], _slot0X);
        }

        /// <summary>Keeps a single card (the live one if present, else the newest)
        /// in the left slot and discards the rest. Used in single-card mode.</summary>
        private void ForceCollapseToOne()
        {
            if (_cards.Count <= 1)
            {
                if (_cards.Count == 1) PlaceInstant(_cards[0], _slot0X);
                _rollPending = false;
                return;
            }

            TimerCard keep = _live ?? _cards[_cards.Count - 1];
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_cards[i], keep)) continue;
                if (_cards[i].go != null) Object.Destroy(_cards[i].go);
                if (ReferenceEquals(_live, _cards[i])) _live = null;
                _cards.RemoveAt(i);
            }
            PlaceInstant(keep, _slot0X);
            keep.slidingOut = false;
            _rollPending = false;
        }

        private void AnimateCards()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                TimerCard c = _cards[i];
                if (c.go == null) { _cards.RemoveAt(i); continue; }

                c.currentX = Mathf.MoveTowards(c.currentX, c.targetX, _slideSpeed * dt);
                c.rt.anchoredPosition = new Vector2(c.currentX, -_marginY);

                if (c.slidingOut && c.currentX <= _offLeftX + 1f)
                {
                    if (ReferenceEquals(_live, c)) _live = null;
                    Object.Destroy(c.go);
                    _cards.RemoveAt(i);
                }
            }
        }

        private void ClearAllCards()
        {
            foreach (var c in _cards)
                if (c.go != null) Object.Destroy(c.go);
            _cards.Clear();
            _live = null;
            _rollPending = false;
            _rollTimer = 0f;
        }

        // ── Card construction ──────────────────────────────────────────────────
        private TimerCard CreateCard(int slotX)
        {
            var go = new GameObject("TimerCard");
            go.transform.SetParent(_cardParent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(_cardW, _cardH);
            rt.anchoredPosition = new Vector2(slotX, -_marginY);

            int timerX = 0;
            int deltaX = timerX + _timerW + _colGap;

            var timer = MakeLbl(go.transform, "0:00.00",
                _timerFontSz, UIStyle.Text, TextAnchor.LowerLeft,
                x: timerX, y: 0, w: _timerW, h: _timerRowH);
            timer.alignByGeometry    = false;
            timer.horizontalOverflow = HorizontalWrapMode.Overflow;

            var delta = MakeLbl(go.transform, "",
                _deltaFontSz, UIStyle.Gold, TextAnchor.LowerLeft,
                x: deltaX, y: 0, w: _deltaW, h: _timerRowH);
            delta.alignByGeometry    = false;
            delta.horizontalOverflow = HorizontalWrapMode.Overflow;

            int pbRowTop = _timerRowH + _rowGap;
            int pbTimeX  = _pbLblW;

            var pbLbl = MakeLbl(go.transform, "PB",
                _pbFontSz, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: 0, y: pbRowTop, w: _pbLblW, h: _pbRowH);
            pbLbl.alignByGeometry = false;

            var pbTime = MakeLbl(go.transform, "--:--.--",
                _pbFontSz, UIStyle.Gold, TextAnchor.MiddleLeft,
                x: pbTimeX, y: pbRowTop, w: _pbTimeW, h: _pbRowH);
            pbTime.alignByGeometry = false;

            int rankRowTop = pbRowTop + _pbRowH + _rowGap;
            var rank = MakeLbl(go.transform, "",
                _pbFontSz, UIStyle.Accent, TextAnchor.MiddleLeft,
                x: 0, y: rankRowTop, w: _cardW, h: _pbRowH);
            rank.alignByGeometry = false;
            rank.gameObject.SetActive(false);

            // Status tag shares the rank row (the two are mutually exclusive:
            // a skipped backtrack is never uploaded, so it never gets a rank).
            var status = MakeLbl(go.transform, "",
                _pbFontSz, UIStyle.Red, TextAnchor.MiddleLeft,
                x: 0, y: rankRowTop, w: _cardW, h: _pbRowH);
            status.alignByGeometry = false;
            status.gameObject.SetActive(false);

            var card = new TimerCard
            {
                go = go, rt = rt,
                timer = timer, delta = delta,
                pbLbl = pbLbl, pbTime = pbTime, rank = rank, status = status,
                currentX = slotX, targetX = slotX,
            };
            _cards.Add(card);
            return card;
        }

        private void PlaceInstant(TimerCard card, int x)
        {
            card.currentX = x;
            card.targetX  = x;
            card.rt.anchoredPosition = new Vector2(x, -_marginY);
        }

        // ── Card state transitions ─────────────────────────────────────────────
        private void InitRunning(TimerCard card, string scene, string entryFrom)
        {
            card.state       = CardState.Running;
            card.liveDisplay = true;
            card.scene       = scene;
            card.entryFrom   = entryFrom;
            card.exitTo      = "";
            card.entryPb     = BestPBForEntry(scene, entryFrom);
            card.deltaVal    = null;
            card.isNewPb     = false;
            card.counts      = true;
            card.notSaved    = false;
            card.rank.gameObject.SetActive(false);
            SetCardStatus(card, null);
            RefreshRunning(card);
        }

        private void FinishCard(TimerCard card, string exitTo, float time)
        {
            card.state      = CardState.Finished;
            card.exitTo     = exitTo;
            card.finishTime = time;

            bool backtrack = IsBacktrack(card.entryFrom, exitTo);

            // A backtrack doesn't count toward the PB highlight if either option
            // excludes it: hidden from the comparison, or skipped from saving
            // (so it can never actually become the stored PB).
            card.counts = !(backtrack
                && (GhostSettings.SkipBacktrackTimer || GhostSettings.SkipBacktrackRuns));

            // Small tag when a backtrack was skipped from saving.
            card.notSaved = backtrack && GhostSettings.SkipBacktrackRuns;

            if (card.entryPb.HasValue)
            {
                card.deltaVal = time - card.entryPb.Value;
                card.isNewPb  = card.counts && card.deltaVal.Value < 0f;
            }
            else
            {
                // No counting PB to compare against. A counting first run is a
                // PB; an excluded backtrack is not.
                card.deltaVal = null;
                card.isNewPb  = card.counts;
            }

            RefreshFinished(card);
        }

        // ── Card visuals ───────────────────────────────────────────────────────
        private void RefreshRunning(TimerCard card)
        {
            card.timer.text  = TimeUtil.Format(RoomTracker.CurrentRoomTime);
            card.timer.color = UIStyle.Text;
            card.delta.text  = "";
            RefreshPbRow(card, card.entryPb, highlightGold: false);
        }

        private void RefreshFinished(TimerCard card)
        {
            card.timer.text  = TimeUtil.Format(card.finishTime);
            card.timer.color = card.isNewPb ? UIStyle.Gold : UIStyle.Text;

            if (!card.counts)
            {
                // Backtrack excluded from the comparison: show the time, but no
                // delta (comparing it to a forward-exit PB would be misleading).
                card.delta.text = "";
            }
            else if (card.deltaVal.HasValue)
            {
                float d = card.deltaVal.Value;
                if (card.isNewPb)
                {
                    card.delta.text  = TimeUtil.FormatDelta(d);
                    card.delta.color = UIStyle.Gold;
                }
                else if (Mathf.Abs(d) < 0.005f)
                {
                    card.delta.text  = "+-0.00";
                    card.delta.color = UIStyle.Subtext;
                }
                else
                {
                    card.delta.text  = TimeUtil.FormatDelta(d);
                    card.delta.color = UIStyle.Red;
                }
            }
            else
            {
                card.delta.text  = "1st";
                card.delta.color = UIStyle.Accent;
            }

            // PB row shows the best counting PB (backtracks excluded when the
            // hide option is on, via BestPBForEntry). A non-counting backtrack
            // never promotes itself to the PB, so it falls back to entryPb —
            // which is "--:--.--" when only backtracks exist.
            float? displayPb = card.isNewPb ? card.finishTime : card.entryPb;
            RefreshPbRow(card, displayPb, highlightGold: card.isNewPb);

            SetCardStatus(card, card.notSaved ? "not saved" : null);
        }

        private void RefreshPbRow(TimerCard card, float? pbTime, bool highlightGold)
        {
            if (pbTime.HasValue)
            {
                card.pbLbl.text  = "PB";
                card.pbLbl.color = highlightGold ? UIStyle.Gold : UIStyle.Subtext;
                card.pbTime.text  = TimeUtil.Format(pbTime.Value);
                card.pbTime.color = UIStyle.Gold;
            }
            else
            {
                Color dim = new Color(UIStyle.Subtext.r, UIStyle.Subtext.g,
                                      UIStyle.Subtext.b, 0.6f);
                card.pbLbl.text  = "PB";       card.pbLbl.color  = dim;
                card.pbTime.text = "--:--.--"; card.pbTime.color = dim;
            }
        }

        private void SetCardsVisible(bool v)
        {
            foreach (var c in _cards)
                if (c.go != null && c.go.activeSelf != v)
                    c.go.SetActive(v);
        }

        private void SetReadyVisible(bool v)
        {
            if (_readyGO != null && _readyGO.activeSelf != v)
                _readyGO.SetActive(v);
        }

        /// <summary>Shows or hides the small status tag on a card. Showing it
        /// hides the rank line (they share the same row and never co-occur).</summary>
        private void SetCardStatus(TimerCard card, string? text)
        {
            if (card.status == null) return;

            if (string.IsNullOrEmpty(text))
            {
                if (card.status.gameObject.activeSelf)
                    card.status.gameObject.SetActive(false);
                return;
            }

            if (card.rank != null) card.rank.gameObject.SetActive(false);
            card.status.text  = text;
            card.status.color = UIStyle.Red with { a = 0.9f };
            if (!card.status.gameObject.activeSelf)
                card.status.gameObject.SetActive(true);
        }

        // ── Rank ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Shows the global rank on whichever finished card matches the room the
        /// rank is for. Called from the main thread when the upload response
        /// arrives. With two cards on screen this targets the correct one.
        /// </summary>
        public void ShowRank(RankInfo rankInfo)
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                TimerCard c = _cards[i];
                if (c.state != CardState.Finished) continue;
                if (c.scene != rankInfo.SceneName) continue;
                if (c.entryFrom != rankInfo.EntryFrom) continue;
                if (!string.IsNullOrEmpty(c.exitTo) && c.exitTo != rankInfo.ExitTo) continue;

                if (c.status != null) c.status.gameObject.SetActive(false);
                c.rank.text  = $"#{rankInfo.Rank} / {rankInfo.TotalRunners}";
                c.rank.color = UIStyle.Accent;
                c.rank.gameObject.SetActive(true);
                return;
            }
        }

        // ── Cancellation banner ──────────────────────────────────────────────────
        private void HandleRunCancelled(string reason)
        {
            if (_bannerGO == null || _bannerRt == null || _bannerText == null) return;

            _bannerText.text = reason;
            _bannerY         = HiddenBannerY();
            _bannerHoldTimer = 0f;
            _bannerState     = BannerState.SlidingIn;

            _bannerRt.anchoredPosition = new Vector2(_bannerRt.anchoredPosition.x, _bannerY);
            _bannerGO.SetActive(true);

            Log.LogInfo($"[RoomTimerHUD] Run cancelled banner: {reason}");
        }

        private void UpdateBanner()
        {
            if (_bannerGO == null || _bannerRt == null) return;
            if (_bannerState == BannerState.Hidden) return;

            float dt      = Time.unscaledDeltaTime;
            float shownY  = ShownBannerY();
            float hiddenY = HiddenBannerY();

            switch (_bannerState)
            {
                case BannerState.SlidingIn:
                    _bannerY = Mathf.MoveTowards(_bannerY, shownY, BannerSlideSpeed * dt);
                    if (_bannerY <= shownY)
                    {
                        _bannerY         = shownY;
                        _bannerHoldTimer = 0f;
                        _bannerState     = BannerState.Visible;
                    }
                    break;

                case BannerState.Visible:
                    _bannerHoldTimer += dt;
                    if (_bannerHoldTimer >= BannerHoldSeconds)
                        _bannerState = BannerState.SlidingOut;
                    break;

                case BannerState.SlidingOut:
                    _bannerY = Mathf.MoveTowards(_bannerY, hiddenY, BannerSlideSpeed * dt);
                    if (_bannerY >= hiddenY)
                    {
                        _bannerY     = hiddenY;
                        _bannerState = BannerState.Hidden;
                        _bannerGO.SetActive(false);
                    }
                    break;
            }

            _bannerRt.anchoredPosition = new Vector2(_bannerRt.anchoredPosition.x, _bannerY);
        }

        private float ShownBannerY()  => -UIStyle.H(MARGIN_Y);
        private float HiddenBannerY() => _bannerHeight + UIStyle.H(20);

        // ── Canvas + persistent UI ────────────────────────────────────────────────
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

            _cardParent = _canvasGO.transform;

            ComputeLayout();
            BuildReadyIndicator(_canvasGO.transform);
            BuildBanner(_canvasGO.transform);
        }

        private void ComputeLayout()
        {
            _timerFontSz = UIStyle.H(18);
            _deltaFontSz = UIStyle.H(14);
            _pbFontSz    = UIStyle.H(12);

            _timerRowH = UIStyle.H(22);
            _pbRowH    = UIStyle.H(16);
            _rowGap    = UIStyle.H(0);

            _timerW  = UIStyle.W(60);
            _deltaW  = UIStyle.W(70);
            _colGap  = UIStyle.W(6);
            _pbLblW  = UIStyle.W(20);
            _pbTimeW = UIStyle.W(60);

            _cardW = _timerW + _colGap + _deltaW;
            _cardH = _timerRowH + _rowGap + _pbRowH + _rowGap + _pbRowH;

            int mX = UIStyle.W(MARGIN_X);
            _marginY = UIStyle.H(MARGIN_Y);

            int gapX = UIStyle.W(18);
            _slot0X = mX;
            _slot1X = _slot0X + _cardW + gapX;
            _offLeftX = -(_cardW + mX + UIStyle.W(40));

            // A slot-to-slot slide takes ~0.22s.
            _slideSpeed = (_slot1X - _slot0X) / 0.22f;

            _bannerHeight = _timerRowH;
        }

        private void BuildReadyIndicator(Transform canvasRoot)
        {
            int mX = UIStyle.W(MARGIN_X);

            _readyGO = new GameObject("ReadyIndicator");
            _readyGO.transform.SetParent(canvasRoot, false);

            var rt = _readyGO.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(mX, -_marginY);
            rt.sizeDelta        = new Vector2(UIStyle.W(82), UIStyle.H(22));

            var lbl = MakeLbl(_readyGO.transform, "Ready",
                UIStyle.H(14), UIStyle.Accent, TextAnchor.MiddleLeft,
                x: 0, y: 0, w: UIStyle.W(82), h: UIStyle.H(22));
            lbl.alignByGeometry = false;

            _readyGO.SetActive(false);
        }

        private void BuildBanner(Transform canvasRoot)
        {
            _bannerWidth  = UIStyle.W(220);
            _bannerHeight = _timerRowH;

            // To the right of where the second (right-slot) card sits, so it
            // never overlaps either card.
            int bannerX = _slot1X + _cardW + UIStyle.W(BANNER_GAP_X);

            _bannerGO = new GameObject("RunCancelledBanner");
            _bannerGO.transform.SetParent(canvasRoot, false);

            _bannerRt = _bannerGO.AddComponent<RectTransform>();
            _bannerRt.anchorMin = _bannerRt.anchorMax = new Vector2(0f, 1f);
            _bannerRt.pivot     = new Vector2(0f, 1f);
            _bannerRt.sizeDelta = new Vector2(_bannerWidth, _bannerHeight);
            _bannerRt.anchoredPosition = new Vector2(bannerX, HiddenBannerY());

            _bannerText = MakeLbl(_bannerGO.transform, "",
                UIStyle.H(13), UIStyle.Red, TextAnchor.LowerLeft,
                x: 0, y: 0, w: _bannerWidth, h: _bannerHeight);
            _bannerText.alignByGeometry    = false;
            _bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _bannerGO.SetActive(false);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
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
            t.font               = UIStyle.Arial;
            t.fontSize           = fontSize;
            t.color              = color;
            t.alignment          = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Truncate;
            t.text               = text;
            t.alignByGeometry    = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            outline.effectDistance = new Vector2(1, -1);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta        = new Vector2(w, h);

            return t;
        }

        private static bool IsBacktrack(string entryFrom, string exitTo)
            => !string.IsNullOrEmpty(entryFrom) && exitTo == entryFrom;

        private static float? BestPBForEntry(string sceneName, string entryFromScene)
        {
            bool hideBacktrack = GhostSettings.SkipBacktrackTimer;
            float? best = null;
            foreach (var kvp in PBManager.AllPBs())
            {
                var key = kvp.Key;
                if (key.SceneName != sceneName || key.EntryFromScene != entryFromScene)
                    continue;
                // When hiding backtracks, the best PB comes only from runs that
                // exit elsewhere. If every run is a backtrack, this returns null
                // and the card shows no PB.
                if (hideBacktrack && IsBacktrack(key.EntryFromScene, key.ExitToScene))
                    continue;
                float t = kvp.Value.TotalTime;
                if (!best.HasValue || t < best.Value) best = t;
            }
            return best;
        }

    }
}