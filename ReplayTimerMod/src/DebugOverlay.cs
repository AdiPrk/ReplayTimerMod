using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace ReplayTimerMod
{
    // Simple on-screen debug overlay, built with the same pattern as
    // TimerMod's TimerDisplay — plain Unity Canvas + Text, no DebugMod
    // canvas system. Lives until we replace it with the real UI.
    //
    // Shows:
    //   STATE   IDLE / RECORDING
    //   ROOM    sceneName [entryGate → ?]
    //   TIME    current LR time  /  PB time for this route
    //   FRAMES  captured frame count
    //   LAST    result of the most recent completed run
    public class DebugOverlay
    {
        private readonly GameObject canvas;
        private Text? stateText;
        private Text? roomText;
        private Text? timeText;
        private Text? framesText;
        private Text? lastText;

        private EvaluationResult? lastResult;

        public DebugOverlay()
        {
            canvas = new GameObject("ReplayModDebugCanvas");
            Object.DontDestroyOnLoad(canvas);

            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 100;

            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvas.AddComponent<GraphicRaycaster>();
        }

        // Called once after the game has fully booted (sceneCount >= 4)
        // so fonts are available.
        public void Setup()
        {
            var allFonts = Resources.FindObjectsOfTypeAll<Font>();
            Font? font = allFonts.FirstOrDefault(f => f.name == "TrajanPro-Regular");

            float x = 0.01f;
            float lineH = 0.033f;

            stateText = MakeText("StateText", font, x, 0.96f);
            roomText = MakeText("RoomText", font, x, 0.96f - lineH);
            timeText = MakeText("TimeText", font, x, 0.96f - lineH * 2);
            framesText = MakeText("FramesText", font, x, 0.96f - lineH * 3);
            lastText = MakeText("LastText", font, x, 0.96f - lineH * 4);
        }

        private Text MakeText(string name, Font? font, float xAnchor, float yAnchor)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(canvas.transform, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(xAnchor, yAnchor - 0.03f);
            rect.anchorMax = new Vector2(xAnchor + 0.4f, yAnchor);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var cg = obj.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var t = obj.AddComponent<Text>();
            t.font = font;
            t.fontSize = 18;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        // Call once per frame from LateUpdate.
        public void Tick()
        {
            if (stateText == null) return;

            if (RoomTracker.IsRecording)
            {
                stateText.color = new Color(0.4f, 1f, 0.4f);
                stateText.text = "● RECORDING";
            }
            else
            {
                stateText.color = new Color(0.7f, 0.7f, 0.7f);
                stateText.text = "○ IDLE";
            }

            roomText!.text = RoomTracker.IsRecording
                ? $"{RoomTracker.CurrentScene}  [{RoomTracker.EntryGateName} → ?]"
                : $"{RoomTracker.CurrentScene}";

            if (RoomTracker.IsRecording)
            {
                float cur = RoomTracker.CurrentRoomTime;
                var key = new RoomKey(RoomTracker.CurrentScene,
                                      RoomTracker.EntryGateName, "?");
                // We don't know the exit yet, so look up by scene+entry only —
                // show the best PB we have for any exit from this entry.
                float? pb = GetBestPBForEntry(RoomTracker.CurrentScene,
                                              RoomTracker.EntryGateName);

                timeText!.color = (pb.HasValue && cur > pb.Value)
                    ? new Color(1f, 0.5f, 0.5f)
                    : Color.white;

                timeText.text = pb.HasValue
                    ? $"{FormatTime(cur)}  /  PB {FormatTime(pb.Value)}"
                    : $"{FormatTime(cur)}  /  PB —";
            }
            else
            {
                timeText!.color = Color.white;
                timeText.text = "";
            }

            framesText!.text = RoomTracker.IsRecording
                ? $"frames: {ReplayTimerModPlugin.Instance.RecorderFrameCount}"
                : "";

            if (lastResult != null)
            {
                lastText!.text = FormatResult(lastResult);
                lastText.color = lastResult.Kind == ResultKind.NewPB
                    ? new Color(1f, 0.85f, 0.2f)
                    : lastResult.Kind == ResultKind.FirstRun
                        ? new Color(0.5f, 0.9f, 1f)
                        : new Color(1f, 0.5f, 0.5f);
            }
            else
            {
                lastText!.text = "";
            }
        }

        public void SetLastResult(EvaluationResult result)
        {
            lastResult = result;
        }

        public void ClearLastResult()
        {
            lastResult = null;
        }

        private static float? GetBestPBForEntry(string scene, string entryGate)
        {
            // Ask PBManager for all PBs and find the best time where
            // scene and entry gate match (exit is unknown mid-run).
            // This is a lightweight scan — PB counts are small.
            float? best = null;
            // We iterate keys manually since Dictionary has no LINQ-free filter.
            foreach (var pair in PBManager.AllPBs())
            {
                if (pair.Key.SceneName == scene && pair.Key.EntryGate == entryGate)
                {
                    if (!best.HasValue || pair.Value.TotalTime < best.Value)
                        best = pair.Value.TotalTime;
                }
            }
            return best;
        }

        private static string FormatResult(EvaluationResult r)
        {
            return r.Kind switch
            {
                ResultKind.FirstRun => $"FIRST  {FormatTime(r.NewTime)}",
                ResultKind.NewPB => $"PB!    {FormatTime(r.NewTime)}  (-{FormatTime(r.Delta!.Value)})",
                ResultKind.MissedPB => $"MISS   {FormatTime(r.NewTime)}  (+{FormatTime(r.Delta!.Value)})",
                _ => ""
            };
        }

        private static string FormatTime(float t)
        {
            int millis = (int)(t * 100) % 100;
            int seconds = (int)t % 60;
            int minutes = (int)t / 60;
            return $"{minutes}:{seconds:00}.{millis:00}";
        }
    }
}