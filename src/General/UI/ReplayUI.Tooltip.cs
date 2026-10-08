using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Hover tooltip ──────────────────────────────────────────────────
        //
        // A single shared tooltip panel on the mod canvas. Elements opt in
        // via AttachTooltip; after a short hover delay the tooltip appears
        // near the cursor (clamped to the screen) and follows it until the
        // pointer leaves.

        private const float TooltipDelaySec = 0.45f;

        private GameObject? _tooltipGO;
        private Text? _tooltipLbl;

        /// <summary>Pointer-hover tracker for tooltip-enabled elements.
        /// Rebuilds destroy them freely; OnDisable clears the static hover
        /// state so a destroyed element can never leave a stuck tooltip.</summary>
        internal sealed class TooltipTrigger : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler
        {
            public string text = "";

            public static TooltipTrigger? Hovered;
            public static float HoverStartTime;

            public void OnPointerEnter(PointerEventData eventData)
            {
                Hovered = this;
                HoverStartTime = Time.unscaledTime;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (Hovered == this) Hovered = null;
            }

            private void OnDisable()
            {
                if (Hovered == this) Hovered = null;
            }
        }

        /// <summary>Makes hovering the given element show a plain-text
        /// tooltip.</summary>
        private static void AttachTooltip(GameObject go, string text)
        {
            var trigger = go.AddComponent<TooltipTrigger>();
            trigger.text = text;
        }

        /// <summary>Called every frame from Tick. Shows/positions/hides the
        /// shared tooltip based on the current hover state.</summary>
        private void TickTooltip()
        {
            var hovered = TooltipTrigger.Hovered;
            bool show = _expanded
                && hovered != null
                && !string.IsNullOrEmpty(hovered.text)
                && Time.unscaledTime - TooltipTrigger.HoverStartTime >= TooltipDelaySec;

            if (!show)
            {
                if (_tooltipGO != null && _tooltipGO.activeSelf)
                    _tooltipGO.SetActive(false);
                return;
            }

            EnsureTooltip();
            if (_tooltipGO == null || _tooltipLbl == null) return;

            if (!_tooltipGO.activeSelf) _tooltipGO.SetActive(true);

            int pad = UIStyle.W(8);
            int width = UIStyle.W(220);

            if (_tooltipLbl.text != hovered!.text)
                _tooltipLbl.text = hovered.text;

            // Size the label to the fixed width, then the panel to the
            // label's preferred (wrapped) height.
            var lblRt = _tooltipLbl.GetComponent<RectTransform>();
            lblRt.sizeDelta = new Vector2(width - pad * 2, lblRt.sizeDelta.y);
            float textH = Mathf.Max(UIStyle.H(16), _tooltipLbl.preferredHeight);
            lblRt.sizeDelta = new Vector2(width - pad * 2, textH);
            lblRt.anchoredPosition = new Vector2(pad, -pad);

            float height = textH + pad * 2;
            var rt = _tooltipGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            // Position near the cursor, clamped on-screen. The canvas uses
            // ConstantPixelSize, so canvas units == screen pixels.
            Vector2 mouse = Input.mousePosition;
            float x = Mathf.Min(mouse.x + UIStyle.W(16), Screen.width - width - 4);
            float yTop = Mathf.Min(mouse.y + height + UIStyle.H(12), Screen.height - 4);
            rt.anchoredPosition = new Vector2(Mathf.Max(4, x), Mathf.Max(height + 4, yTop));
        }

        private void EnsureTooltip()
        {
            if (_tooltipGO != null) return;
            if (_canvasGO == null) return;

            _tooltipGO = MakeGO("Tooltip", _canvasGO.transform);

            // 1px frame: outer image is the border color, inner inset image
            // is the panel background.
            var borderImg = _tooltipGO.AddComponent<Image>();
            borderImg.color = UIStyle.Overlay with { a = 0.9f };
            borderImg.raycastTarget = false;

            var rt = _tooltipGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero; // bottom-left anchored
            rt.pivot = new Vector2(0f, 1f);             // position = top-left corner

            var inner = MakeGO("Inner", _tooltipGO.transform);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = UIStyle.Base with { a = 0.97f };
            innerImg.raycastTarget = false;
            var innerRt = inner.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(1, 1);
            innerRt.offsetMax = new Vector2(-1, -1);

            _tooltipLbl = MakeLbl(_tooltipGO.transform, "",
                UIStyle.FontSizeRow, UIStyle.Text, TextAnchor.UpperLeft,
                x: UIStyle.W(8), y: UIStyle.H(8), w: UIStyle.W(204), h: UIStyle.H(16));
            // MakeLbl defaults to no-wrap/truncate; the tooltip needs wrapped
            // multi-line text and a measurable preferredHeight.
            _tooltipLbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipLbl.verticalOverflow = VerticalWrapMode.Overflow;

            _tooltipGO.SetActive(false);
        }
    }
}
