using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The race's own banners, drawn with the original's artwork rather than with
    /// text: FINAL LAP as the last lap starts, FINISH as the line is crossed, and
    /// the result panel three seconds later.
    ///
    /// The images are the demo's, extracted from <c>stage_drivinggame.rho</c> —
    /// <c>finallap/final lap.png</c>, <c>goalin/finish.png</c>, <c>winner/ranking_1.png</c>.
    /// The original plays each of them through an action of its own with a scale
    /// and alpha curve; those curves are not recovered, so these fade in and hold
    /// instead. That is the one part of this panel that is the port's invention,
    /// and it is only timing.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Race Banner Display")]
    public sealed class RaceBannerDisplay : HudWidget
    {
        /// <summary>How long FINAL LAP and FINISH stay up.</summary>
        public const float BannerSeconds = 2.5f;

        public const float FadeSeconds = 0.25f;
        public const float BannerWidth = 512f;
        public const float BannerHeight = 128f;

        [SerializeField] private Texture2D _finalLap;
        [SerializeField] private Texture2D _finish;
        [SerializeField] private Texture2D _winner;

        [SerializeField] private RawImage _banner;
        [SerializeField] private TextMeshProUGUI _resultLabel;
        [SerializeField] private HudFontSet _fonts;

        private float _bannerSeconds;
        private uint _shownLap;
        private bool _finishShown;

        private static readonly Color ResultColor = new Color32(245, 248, 250, 255);

        protected override void OnEnable()
        {
            Resolve();
            Build();
            base.OnEnable();
        }

        private void Resolve()
        {
#if UNITY_EDITOR
            const string directory = "Assets/_Project/Art/UI/Race";
            if (_finalLap == null)
            {
                _finalLap = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{directory}/final_lap.png");
            }
            if (_finish == null)
            {
                _finish = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{directory}/finish.png");
            }
            if (_winner == null)
            {
                _winner = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{directory}/winner.png");
            }
            if (_fonts == null)
            {
                _fonts = UnityEditor.AssetDatabase.LoadAssetAtPath<HudFontSet>(
                    "Assets/_Project/Data/UI/HudFontSet.asset");
            }
#endif
        }

        /// <summary>
        /// Creates the two children when the panel was dropped in without them.
        /// The HUD's other panels are authored by their builders; this one is small
        /// enough to build itself.
        /// </summary>
        private void Build()
        {
            if (_banner == null)
            {
                var holder = new GameObject("Banner", typeof(RectTransform));
                holder.transform.SetParent(transform, worldPositionStays: false);
                _banner = holder.AddComponent<RawImage>();
                _banner.raycastTarget = false;

                var rect = (RectTransform)holder.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -90f);
                rect.sizeDelta = new Vector2(BannerWidth, BannerHeight);
            }

            if (_resultLabel == null)
            {
                var holder = new GameObject("Result", typeof(RectTransform));
                holder.transform.SetParent(transform, worldPositionStays: false);
                _resultLabel = holder.AddComponent<TextMeshProUGUI>();
                _resultLabel.alignment = TextAlignmentOptions.Center;
                _resultLabel.fontSize = 40f;
                _resultLabel.color = ResultColor;
                _resultLabel.raycastTarget = false;
                if (_fonts != null && _fonts.Ui != null) _resultLabel.font = _fonts.Ui;

                var rect = (RectTransform)holder.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -230f);
                rect.sizeDelta = new Vector2(720f, 140f);
            }

            _banner.enabled = false;
            _resultLabel.enabled = false;
        }

        protected override void Refresh()
        {
            if (Simulator == null || _banner == null) return;

            KartRaceFlow race = Simulator.Race;
            KartCourseProgress progress = Simulator.Progress;

            if (_bannerSeconds > 0f) _bannerSeconds = Mathf.Max(0f, _bannerSeconds - Time.deltaTime);

            // FINAL LAP: raised once, on the crossing that starts the last lap.
            uint lap = progress.Lap;
            if (lap != _shownLap)
            {
                _shownLap = lap;
                if (Simulator.LapCount != 0u && lap == Simulator.LapCount &&
                    race.Phase == KartRacePhase.Running)
                {
                    Show(_finalLap);
                }
            }

            if (race.Finished && !_finishShown)
            {
                _finishShown = true;
                Show(_finish);
            }
            if (!race.Finished)
            {
                _finishShown = false;
                _resultLabel.enabled = false;
            }

            // The result panel replaces the banner three seconds after the finish.
            if (race.ResultsVisible)
            {
                _bannerSeconds = 0f;
                if (_winner != null)
                {
                    _banner.texture = _winner;
                    _banner.enabled = true;
                    _banner.color = Color.white;
                }

                _resultLabel.enabled = true;
                _resultLabel.text = progress.BestLapMs != 0u
                    ? $"TIME {Format(race.FinishTimeMs)}\nBEST LAP {Format(progress.BestLapMs)}"
                    : $"TIME {Format(race.FinishTimeMs)}";
                return;
            }

            if (_bannerSeconds <= 0f)
            {
                _banner.enabled = false;
                return;
            }

            float alpha = Mathf.Clamp01((BannerSeconds - _bannerSeconds) / FadeSeconds);
            _banner.color = new Color(1f, 1f, 1f, alpha);
        }

        private void Show(Texture2D texture)
        {
            if (texture == null) return;

            _banner.texture = texture;
            _banner.enabled = true;
            _banner.color = new Color(1f, 1f, 1f, 0f);
            _bannerSeconds = BannerSeconds;
        }

        private static string Format(uint milliseconds)
        {
            uint totalSeconds = milliseconds / 1000u;
            return $"{totalSeconds / 60u}:{totalSeconds % 60u:00}.{milliseconds % 1000u:000}";
        }
    }
}
