using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_countdown</c>: 3, 2, 1 as the deadline approaches, then
    /// START!, centred over the view with a drop shadow four pixels down and
    /// right.
    ///
    /// The digit is <c>(remaining + 999) / 1000</c> — a ceiling — so "3" shows
    /// for the whole of the third second rather than flicking over early, and
    /// anything above 3 draws nothing at all. That is why the 7000 ms countdown
    /// is silent for its first four seconds.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Countdown Display")]
    public sealed class CountdownDisplay : HudWidget
    {
        public const float DigitFontSize = 96f;
        public const float StartFontSize = 64f;
        public const float BoxTopOffset = -130f;
        public const float BoxHeight = 200f;
        public const float ShadowOffset = 4f;

        /// <summary>How long START! stays up after GO.</summary>
        public const float StartNoticeSeconds = 1.5f;

        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private TextMeshProUGUI _shadow;
        [SerializeField] private HudFontSet _fonts;

        private static readonly Color DigitColor = new Color32(245, 248, 250, 255);
        private static readonly Color StartColor = new Color32(120, 255, 155, 255);
        private static readonly Color ShadowColor = new Color32(18, 22, 28, 255);

        private float _startNoticeSeconds;
        private bool _sawRelease;

        protected override void Refresh()
        {
            if (Simulator == null || _label == null) return;

            KartCountdown countdown = Simulator.Countdown;
            uint remainingMs = countdown.Armed && !countdown.Released
                ? (Simulator.RaceClockMs < countdown.DeadlineMs
                    ? countdown.DeadlineMs - Simulator.RaceClockMs
                    : 0u)
                : 0u;

            if (countdown.Released && !_sawRelease)
            {
                _sawRelease = true;
                _startNoticeSeconds = StartNoticeSeconds;
            }
            if (!countdown.Released) _sawRelease = false;

            if (_startNoticeSeconds > 0f && Application.isPlaying)
            {
                _startNoticeSeconds = Mathf.Max(0f, _startNoticeSeconds - Time.deltaTime);
            }

            string text = null;
            Color color = DigitColor;
            float size = DigitFontSize;

            if (remainingMs != 0u)
            {
                uint seconds = (remainingMs + 999u) / 1000u;
                if (seconds <= 3u)
                {
                    text = seconds.ToString();
                    color = DigitColor;
                    size = DigitFontSize;
                }
            }
            else if (_startNoticeSeconds > 0f)
            {
                text = "START!";
                color = StartColor;
                size = StartFontSize;
            }

            bool visible = text != null;
            _label.enabled = visible;
            if (_shadow != null) _shadow.enabled = visible;
            if (!visible) return;

            _label.SetText(text);
            _label.color = color;
            _label.fontSize = size;

            if (_shadow == null) return;
            _shadow.SetText(text);
            _shadow.color = ShadowColor;
            _shadow.fontSize = size;
        }

        /// <summary>Applies the recovered box geometry.</summary>
        public void ApplyLayout()
        {
            HudStatusLines.StretchToParent((RectTransform)transform);
            Place(_shadow, new Vector2(ShadowOffset, -ShadowOffset));
            Place(_label, Vector2.zero);
        }

        private void Place(TMP_Text text, Vector2 offset)
        {
            if (text == null) return;

            var rect = (RectTransform)text.transform;
            // Full width, and a 200-tall box starting 130 above the centre.
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(0f, BoxHeight);
            rect.anchoredPosition = new Vector2(offset.x, -BoxTopOffset + offset.y);

            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            if (_fonts != null) text.font = _fonts.UiHeavy;
        }
    }
}
