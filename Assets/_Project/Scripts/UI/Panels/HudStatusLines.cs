using System.Collections.Generic;
using System.Text;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The top-left debug lines <c>draw_scene</c> writes straight onto the frame.
    ///
    /// The original prints six of them. Four survive into phase 1 unchanged; the
    /// two that are dropped are the render-mode toggles and the experimental
    /// gear/gauge/storage line, whose every field belongs to a feature that does
    /// not exist yet. The key-help line lists the keys that actually respond
    /// rather than the full set, so the HUD never advertises a dead key.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Status Lines")]
    public sealed class HudStatusLines : HudWidget
    {
        public const float LinePitch = 20f;
        public const float FirstLineY = 12f;
        public const float MarginX = 16f;

        [SerializeField] private RectTransform _lineRoot;
        [SerializeField] private HudFontSet _fonts;
        [SerializeField, Min(6f)] private float _fontSize = 13f;
        [SerializeField] private SimulatorKeys _keys;

        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private readonly StringBuilder _builder = new StringBuilder(384);

        /// <summary>How many lines the prefab needs to allocate.</summary>
        public const int LineCount = 5;

        protected override void OnEnable()
        {
            CollectLabels();
            base.OnEnable();
        }

        private void CollectLabels()
        {
            _labels.Clear();
            if (_lineRoot == null) return;
            for (int i = 0; i < _lineRoot.childCount; ++i)
            {
                var label = _lineRoot.GetChild(i).GetComponent<TextMeshProUGUI>();
                if (label != null) _labels.Add(label);
            }
        }

        protected override void Refresh()
        {
            if (Simulator == null) return;
            if (_labels.Count == 0) CollectLabels();
            if (_labels.Count < LineCount) return;

            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            WriteStatus(kart);
            WriteKeys();
            WriteScene(kart);
            WriteDrift(kart);
            WriteScreenshotNotice();
        }

        private void WriteStatus(KartSimulationState kart)
        {
            float forwardSpeed = kart.ForwardSpeed;
            float lateralSpeed = kart.LateralSpeed;

            _builder.Clear();
            _builder.AppendFormat(
                "FPS {0,5:F1} | speed {1:F2} m/s | slip {2:F1} deg | vf {3:F1} vs {4:F1} | AUTO {5}",
                Simulator.FramesPerSecond,
                kart.Speed,
                KartUnits.SlipAngleDegrees(forwardSpeed, lateralSpeed),
                forwardSpeed,
                lateralSpeed,
                kart.Drift.SlipDetected ? "ON" : "off");

            _labels[0].SetText(_builder);
            _labels[0].color = HudPalette.StatusText;
        }

        private void WriteKeys()
        {
            _builder.Clear();
            _builder.Append(
                "Arrows: drive/instant  Shift/W: drift  Ctrl/D: boost  " +
                "C: camera  B: bounds  F: drag trigger  ");
            _builder.AppendFormat(
                "L: colour {0} [{1}]", Simulator.KartColourIndex, Simulator.KartColourName);
            _builder.AppendFormat("  K: kart  T: track  F1: fps [{0}]", Simulator.FrameRateCapName);
            _builder.Append("  S: screenshot  R: reset");

            _labels[1].SetText(_builder);
            _labels[1].color = HudPalette.StatusText;
        }

        private void WriteScene(KartSimulationState kart)
        {
            TrackSpecAsset track = Simulator.Track;
            KartSpecAsset kartSpec = Simulator.Kart;

            _builder.Clear();
            _builder.Append(Simulator.ViewMode == SimulatorViewMode.TopDown ? "TOP-DOWN" : "CHASE");
            if (track != null)
            {
                _builder.AppendFormat(
                    " | {0} ({1}) {2:F1} x {3:F1} | scene {4}",
                    track.DisplayName, track.AssetName, track.Width, track.Length,
                    track.HasScene ? "KTRK+collision" : "bounds");
            }
            if (kartSpec != null)
            {
                _builder.AppendFormat(
                    " | kart {0} {1:F3} x {2:F3}", kartSpec.AssetName, kartSpec.Width, kartSpec.Length);
            }
            _builder.AppendFormat(" | h {0:F2}", kart.Position.Z);

            _labels[2].SetText(_builder);
            _labels[2].color = kart.Drift.SlipDetected
                ? HudPalette.StatusDrift
                : HudPalette.StatusDim;
        }

        private void WriteDrift(KartSimulationState kart)
        {
            bool drifting = kart.Drift.TriggerActive || kart.Drift.InputActive ||
                            kart.Drift.SlipDetected;
            bool boosting = KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost);

            _builder.Clear();
            _builder.AppendFormat(
                "DRIFT {0} | ITEM {1} {2:F2}s | INSTANT READY {3:F2}s | INSTANT {4} | drag x{5:F2} | skids {6}",
                drifting ? "ON" : "off",
                kart.TimedBoost.Active ? "ON" : "off",
                kart.TimedBoost.RemainingMs * 0.001f,
                kart.InstantBoost.OpportunityTimer,
                kart.InstantBoost.Active ? "ON" : "off",
                kart.GroundedDragScale,
                Simulator.SkidMarkSegments);

            _labels[3].SetText(_builder);
            _labels[3].color = boosting
                ? HudPalette.StatusBoost
                : (drifting ? HudPalette.StatusDrift : HudPalette.StatusDim);
        }

        private void WriteScreenshotNotice()
        {
            bool visible = _keys != null && _keys.ScreenshotNoticeSeconds > 0f;
            _labels[4].enabled = visible;
            if (!visible) return;

            _builder.Clear();
            _builder.Append("SAVED ").Append(_keys.LastScreenshotName);
            _labels[4].SetText(_builder);
            _labels[4].color = HudPalette.ScreenshotNotice;
        }

        /// <summary>Places the lines at the original's 20 px pitch from (16, 12).</summary>
        public void ApplyLayout()
        {
            // Both containers fill the canvas so a line's offset is measured from
            // the window's top-left corner, which is where the C code measures it.
            StretchToParent((RectTransform)transform);
            if (_lineRoot == null) return;
            StretchToParent(_lineRoot);

            for (int i = 0; i < _lineRoot.childCount; ++i)
            {
                var rect = (RectTransform)_lineRoot.GetChild(i);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(MarginX, -(FirstLineY + i * LinePitch));
                rect.sizeDelta = new Vector2(1100f, LinePitch);

                var label = rect.GetComponent<TextMeshProUGUI>();
                if (label == null) continue;
                label.fontSize = _fontSize;
                if (_fonts != null && _fonts.Mono != null) label.font = _fonts.Mono;
            }
        }

        internal static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
