using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The race's own notices, all in the countdown's lettering: the lap call as
    /// the closing laps start, and the result — WINNER or FINISH — from the moment
    /// the line is crossed, with the times joining it three seconds later.
    ///
    /// The demo draws each of these with its own artwork — <c>final lap.png</c>,
    /// <c>goalin/finish.png</c>, <c>ranking_1.png</c> — and this port draws them as
    /// text instead, in <see cref="HudFontSet.UiHeavy"/> at the countdown's own
    /// size, offset and drop shadow, so the whole race reads in one face. The
    /// original images stay in the project, unused.
    ///
    /// <b>What is recovered.</b> The three-second delay before the times is the
    /// original's, as is the lap call and its sound — the lap branch
    /// plays <c>etc</c>'s <c>ufo_lab</c> and runs <c>finallap</c> when the lap
    /// counter reaches the course's lap count. The wording of an earlier lap
    /// (<c>2ND LAP</c>) is the port's; the demo ships art for the final lap only.
    /// So is the fade — the original's actions carry scale and alpha curves this
    /// port has not recovered — and so is showing the placing at the crossing:
    /// the original runs its <c>goalin</c> action there and only names the result
    /// with the <c>winner</c> or <c>raceover</c> one afterwards.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Race Banner Display")]
    public sealed class RaceBannerDisplay : HudWidget
    {
        /// <summary>How long a timed notice stays up.</summary>
        public const float NoticeSeconds = 2.5f;

        public const float FadeSeconds = 0.25f;

        /// <summary>The countdown's START! size, so the two match.</summary>
        public const float NoticeFontSize = 64f;

        public const float ShadowOffset = 4f;

        public const float ResultFontSize = 40f;
        public const float ResultBoxHeight = 120f;

        [SerializeField] private TextMeshProUGUI _noticeLabel;
        [SerializeField] private TextMeshProUGUI _noticeShadow;
        [SerializeField] private TextMeshProUGUI _resultLabel;
        [SerializeField] private HudFontSet _fonts;

        private float _noticeSeconds;
        private bool _noticeHold;
        private Color _noticeFace = Color.white;

        private uint _shownLap;
        private bool _finishShown;

        private static readonly Color ResultColor = new Color32(245, 248, 250, 255);
        private static readonly Color FinalLapColor = new Color32(255, 196, 96, 255);
        private static readonly Color WinnerColor = new Color32(120, 255, 155, 255);
        private static readonly Color PlainColor = new Color32(245, 248, 250, 255);
        private static readonly Color ShadowColor = new Color32(18, 22, 28, 255);

        protected override void OnEnable()
        {
            Resolve();
            Build();
            base.OnEnable();
        }

        private void Resolve()
        {
#if UNITY_EDITOR
            if (_fonts == null)
            {
                _fonts = UnityEditor.AssetDatabase.LoadAssetAtPath<HudFontSet>(
                    "Assets/_Project/Data/UI/HudFontSet.asset");
            }
#endif
        }

        /// <summary>
        /// Creates the children when the panel was dropped in without them. The
        /// HUD's other panels are authored by their builders; this one is small
        /// enough to build itself.
        /// </summary>
        private void Build()
        {
            // The shadow is built first so it draws behind, four pixels down and
            // right — the offset the countdown uses.
            if (_noticeShadow == null) _noticeShadow = BuildNotice("Notice shadow", ShadowOffset);
            if (_noticeLabel == null) _noticeLabel = BuildNotice("Notice", 0f);

            if (_resultLabel == null)
            {
                var holder = new GameObject("Result", typeof(RectTransform));
                holder.transform.SetParent(transform, worldPositionStays: false);

                _resultLabel = holder.AddComponent<TextMeshProUGUI>();
                // Hung off the top of its own box so it starts immediately below
                // the headline's, which is what keeps the two apart whatever the
                // headline says.
                _resultLabel.alignment = TextAlignmentOptions.Top;
                _resultLabel.fontSize = ResultFontSize;
                _resultLabel.color = ResultColor;
                _resultLabel.raycastTarget = false;
                if (_fonts != null && _fonts.Ui != null) _resultLabel.font = _fonts.Ui;

                var rect = (RectTransform)holder.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.sizeDelta = new Vector2(0f, ResultBoxHeight);
                rect.anchoredPosition = new Vector2(
                    0f, -CountdownDisplay.BoxTopOffset - CountdownDisplay.BoxHeight);
            }

            _noticeLabel.enabled = false;
            _noticeShadow.enabled = false;
            _resultLabel.enabled = false;
        }

        /// <summary>
        /// One notice label, placed and faced like the countdown's so the two read
        /// as the same lettering.
        /// </summary>
        private TextMeshProUGUI BuildNotice(string name, float offset)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);

            var label = holder.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.fontSize = NoticeFontSize;
            label.raycastTarget = false;
            if (_fonts != null) label.font = _fonts.UiHeavy;

            var rect = (RectTransform)holder.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, CountdownDisplay.BoxHeight);
            rect.anchoredPosition = new Vector2(offset, -CountdownDisplay.BoxTopOffset - offset);

            return label;
        }

        protected override void Refresh()
        {
            if (Simulator == null || _noticeLabel == null) return;

            KartRaceFlow race = Simulator.Race;
            KartCourseProgress progress = Simulator.Progress;

            if (Application.isPlaying && _noticeSeconds > 0f)
            {
                _noticeSeconds = Mathf.Max(0f, _noticeSeconds - Time.deltaTime);
            }

            // The lap call, raised once on the crossing that starts a lap.
            uint lap = progress.Lap;
            if (lap != _shownLap)
            {
                _shownLap = lap;
                if (race.Phase == KartRacePhase.Running) ShowLap(lap, Simulator.LapCount);
            }

            // The crossing. The headline is the result itself rather than a
            // FINISH that gets replaced three seconds later: a kart that came
            // second reads FINISH once and keeps it, and a winner reads WINNER
            // from the moment it crosses.
            if (race.Finished && !_finishShown)
            {
                _finishShown = true;

                bool won = race.FinishPlace <= 1u;
                ShowNotice(won ? "WINNER" : "FINISH", won ? WinnerColor : PlainColor, hold: true);
            }
            if (!race.Finished)
            {
                _finishShown = false;
                _noticeHold = false;
                _resultLabel.enabled = false;
            }

            // The times join the headline three seconds later, under it.
            if (race.ResultsVisible)
            {
                _resultLabel.enabled = true;
                _resultLabel.text = progress.BestLapMs != 0u
                    ? $"TIME {Format(race.FinishTimeMs)}\nBEST LAP {Format(progress.BestLapMs)}"
                    : $"TIME {Format(race.FinishTimeMs)}";
            }

            Fade();
        }

        /// <summary>
        /// The lap wording.
        ///
        /// The last lap is the original's <c>finallap</c>; the ones before it are
        /// the port's. On a two-lap course there are none — lap 2 is already the
        /// final one and reads that way, which is why the test is a comparison
        /// against the course's own lap count rather than a fixed number.
        /// </summary>
        private void ShowLap(uint lap, uint lapCount)
        {
            if (lap < 2u) return;

            bool final = lapCount != 0u && lap >= lapCount;
            ShowNotice(
                final ? "FINAL LAP" : $"{lap}{Ordinal(lap)} LAP",
                final ? FinalLapColor : PlainColor,
                hold: false);
        }

        /// <summary>
        /// Puts a word up in the countdown's lettering. A held notice stays at full
        /// alpha — the result does that — and an unheld one fades in and times out.
        ///
        /// The text is only pushed when it actually changes, so the result asking
        /// for the same word every frame does not re-lay the mesh.
        /// </summary>
        private void ShowNotice(string text, Color face, bool hold)
        {
            if (_noticeLabel == null) return;

            if (!string.Equals(_noticeLabel.text, text, System.StringComparison.Ordinal))
            {
                _noticeLabel.SetText(text);
                if (_noticeShadow != null) _noticeShadow.SetText(text);
            }

            _noticeFace = face;
            _noticeHold = hold;
            _noticeSeconds = NoticeSeconds;
        }

        /// <summary>English ordinals.</summary>
        private static string Ordinal(uint value)
        {
            uint tens = value % 100u;
            if (tens >= 11u && tens <= 13u) return "TH";

            switch (value % 10u)
            {
                case 1u: return "ST";
                case 2u: return "ND";
                case 3u: return "RD";
                default: return "TH";
            }
        }

        private void Fade()
        {
            bool visible = _noticeHold || _noticeSeconds > 0f;
            _noticeLabel.enabled = visible;
            if (_noticeShadow != null) _noticeShadow.enabled = visible;
            if (!visible) return;

            // The same fade-in either way — the expression saturates past it, so a
            // held notice simply stays at full alpha once its timer runs out.
            float alpha = Mathf.Clamp01((NoticeSeconds - _noticeSeconds) / FadeSeconds);

            _noticeLabel.color = new Color(_noticeFace.r, _noticeFace.g, _noticeFace.b, alpha);
            if (_noticeShadow != null)
            {
                _noticeShadow.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, alpha);
            }
        }

        private static string Format(uint milliseconds)
        {
            uint totalSeconds = milliseconds / 1000u;
            return $"{totalSeconds / 60u}:{totalSeconds % 60u:00}.{milliseconds % 1000u:000}";
        }
    }
}
