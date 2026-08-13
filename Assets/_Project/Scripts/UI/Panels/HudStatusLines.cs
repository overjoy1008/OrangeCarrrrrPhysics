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
    /// The keys are split across three lines the way the original splits them:
    /// driving, view toggles, and the experimental layers in their own purple.
    /// Each line lists only the keys that actually respond, so the HUD never
    /// advertises a dead one — which is why the render-mode toggles and the
    /// gearbox are still absent.
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
        public const int LineCount = 8;

        /// <summary>
        /// The last line that is written every frame. Only the course line and
        /// the notice line after it come and go.
        /// </summary>
        private const int LastAlwaysVisibleLine = 5;

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
            ApplyLayout();
        }

        protected override void Refresh()
        {
            if (Simulator == null) return;

            // Re-collected whenever the row count has moved under us, not only
            // when it is empty. Rows are added to the prefab when LineCount grows,
            // and a panel that had already collected the old set would otherwise
            // fail the check below for the rest of the session and stop writing
            // every line — including the ones that were there all along.
            if (_lineRoot != null && _labels.Count != _lineRoot.childCount) CollectLabels();
            if (_labels.Count < LineCount) return;

            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            // The always-written lines are switched on explicitly rather than
            // left as whatever was serialised. An earlier layout had the course
            // line at a lower index and hid it when a track had no course; under
            // ExecuteAlways that ran in edit mode and the disabled state was saved
            // into every scene as a prefab override, so the line stayed dark long
            // after it had become something else.
            for (int line = 0; line <= LastAlwaysVisibleLine; ++line) _labels[line].enabled = true;

            WriteStatus(kart);
            WriteKeys();
            WriteScene(kart);
            WriteDrift(kart);
            WriteCourse();
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

        /// <summary>
        /// The driving keys, then the view toggles, then the experimental layers,
        /// on three lines.
        ///
        /// The original splits the first two and says why: "there are enough of
        /// them now that keeping them with the driving keys ran the line off the
        /// window." It splits the third for a different reason, and that one is
        /// about meaning rather than width — "the inferred layers, kept on their
        /// own line so they read as what they are rather than as part of the
        /// recovered demo" — which is why it is also the only line in a colour of
        /// its own.
        /// </summary>
        private void WriteKeys()
        {
            _builder.Clear();
            _builder.Append(
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                "Arrows: grip drive  Shift/W: drift  Cmd/D: booster  ");
#else
                "Arrows: grip drive  Shift/W: drift  Ctrl/D: booster  ");
#endif
            _builder.Append("P: parameters  K: karts  T: tracks  ");
            _builder.Append("F: drag trigger  R: respawn");

            _labels[1].SetText(_builder);
            _labels[1].color = HudPalette.StatusText;

            _builder.Clear();
            _builder.AppendFormat(
                "C: color [{0} {1}]", Simulator.KartColourIndex, Simulator.KartColourName);
            _builder.AppendFormat("  I: skid [{0}]", Simulator.SkidStyleName);
            _builder.AppendFormat(
                "  F1: race mode [{0}]",
                Simulator.RaceMode == KartRaceMode.Race ? "race" : "free");
            _builder.Append("  F2: screenshot");

            // Written out rather than showing the state: both of these are things
            // you can see on screen, so the label says what the key does instead
            // of repeating what is already in front of you.
            _builder.Append("  F3: show/hide checkpoints");
            _builder.Append("  F4: show/hide kart bounds");

            _builder.Append("  F5: camera");
            _builder.AppendFormat("  F6: fps [{0}]", Simulator.FrameRateCapName);

            _labels[2].SetText(_builder);
            _labels[2].color = HudPalette.StatusText;

            WriteExperimental();
        }

        /// <summary>
        /// The inferred layers: the gauge's charging model, the short booster off
        /// a drift exit, the storage cap, and what starts and stops an item boost.
        /// None of these is recovered behaviour — they are hypotheses the
        /// simulator can be run against — and the purple is what says so at a
        /// glance.
        ///
        /// They are the only things on the number row, so which keys change a
        /// hypothesis and which only change the view is readable from the keyboard
        /// rather than from this line.
        ///
        /// Every bracket carries live state. The short booster's is two values —
        /// the model and how many charges are banked under it — because with the
        /// stored model an empty bank is the reason a press does nothing, and a
        /// label naming only the model would leave that off screen. How many
        /// boosters are in storage is the gauge panel's job, not this line's.
        /// </summary>
        private void WriteExperimental()
        {
            KartGauge gauge = Simulator.Gauge;

            _builder.Clear();
            _builder.Append("(experimental)");
            _builder.AppendFormat(
                " E: gear [{0}]",
                Simulator.Gearbox.Mode == KartGearMode.Multi ? "multi" : "single");
            _builder.AppendFormat(" 1: gauge [{0}]", KartGauge.ModelName(gauge.Model));
            _builder.AppendFormat(
                " 2: short booster [{0}, {1}]",
                Simulator.StoredInstantBoost ? "stored" : "window",
                Simulator.StoredInstantBoostCount);
            _builder.AppendFormat(
                " 3: storage [{0}]", gauge.UnlimitedBoosters ? "unlimited" : "capped");
            _builder.AppendFormat(
                " 4: starter [{0}]", Simulator.NoDelayBoost ? "no delay" : "one press");
            _builder.AppendFormat(
                " 5: stopper [{0}]", Simulator.ReverseInputEndsBoost ? "reverse" : "release");

            _labels[3].SetText(_builder);
            _labels[3].color = HudPalette.StatusExperimental;
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
                // The engine set sits with the kart rather than up on the key
                // line: it is no longer something a key does, it is one more thing
                // the chosen kart brings with it.
                _builder.AppendFormat(
                    " | kart {0} {1:F3} x {2:F3} | engine {3}",
                    kartSpec.AssetName, kartSpec.Width, kartSpec.Length,
                    Simulator.EngineSoundPreset);
            }
            _builder.AppendFormat(" | h {0:F2}", kart.Position.Z);

            _labels[4].SetText(_builder);
            _labels[4].color = kart.Drift.SlipDetected
                ? HudPalette.StatusDrift
                : HudPalette.StatusDim;
        }

        private void WriteDrift(KartSimulationState kart)
        {
            bool drifting = KartGauge.DriftVisualActive(kart);
            bool boosting = KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost);

            // The original's fields, in its order. The gauge's own readout is not
            // among them: it belongs to the telemetry panel, where the original
            // puts it, and the stored count is on the experimental line.
            _builder.Clear();
            _builder.AppendFormat(
                "DRIFT {0} | ITEM {1} {2:F2}s | INSTANT READY {3:F2}s | INSTANT {4} | " +
                "drag x{5:F2} | skids {6} [{7}]",
                drifting ? "ON" : "off",
                kart.TimedBoost.Active ? "ON" : "off",
                kart.TimedBoost.RemainingMs * 0.001f,
                kart.InstantBoost.OpportunityTimer,
                kart.InstantBoost.Active ? "ON" : "off",
                kart.GroundedDragScale,
                Simulator.SkidMarkSegments,
                Simulator.SkidStyleName);

            _labels[5].SetText(_builder);
            _labels[5].color = boosting
                ? HudPalette.StatusBoost
                : (drifting ? HudPalette.StatusDrift : HudPalette.StatusDim);
        }

        /// <summary>
        /// The original's own progress record, field for field: the node the kart
        /// is in, how far into it, the accumulated start-gate crossings that gate
        /// the lap counter, and the wrong-way flag.
        ///
        /// <c>advance</c> is printed because it is the mechanism rather than a
        /// derived number — a lap that refuses to count is always a lap where this
        /// stopped moving, and the line says so directly.
        /// </summary>
        private void WriteCourse()
        {
            bool ready = Simulator.CourseReady;
            _labels[6].enabled = ready;
            if (!ready) return;

            KartCourseProgress progress = Simulator.Progress;

            KartRaceFlow race = Simulator.Race;

            _builder.Clear();
            _builder.AppendFormat(
                "LAP {0}/{1} | node {2}/{3} {4:F0}m | advance {5}",
                progress.Lap,
                Simulator.LapCount,
                progress.NodeId,
                Simulator.Course.NodeCount,
                progress.NodeDistance,
                progress.Advance);

            if (progress.BestLapMs != 0)
            {
                _builder.AppendFormat(" | best {0:F2}s", progress.BestLapMs * 0.001f);
            }
            if (progress.WrongWay) _builder.Append(" | WRONG WAY");

            // F2's setting, and what the race is doing under it.
            _builder.AppendFormat(
                " | {0}", race.Mode == KartRaceMode.Race ? "RACE" : "FREE");
            if (race.Finished)
            {
                _builder.AppendFormat(" | FINISHED {0:F2}s", race.FinishTimeMs * 0.001f);
            }

            _labels[6].SetText(_builder);
            _labels[6].color = progress.WrongWay ? HudPalette.StatusWrongWay : HudPalette.StatusDim;
        }

        /// <summary>
        /// The last line carries whichever notice is live. The respawn wins: it
        /// says the kart is being put back on the course, which matters more than
        /// a filename.
        /// </summary>
        private void WriteScreenshotNotice()
        {
            if (Simulator.RespawnNoticeSeconds > 0f)
            {
                _labels[7].enabled = true;
                _builder.Clear();
                _builder.Append(Simulator.CourseReady
                    ? "RESPAWNING ONTO THE COURSE"
                    : "FELL THROUGH THE TRACK - RESPAWNED");
                _labels[7].SetText(_builder);
                _labels[7].color = HudPalette.StatusWrongWay;
                return;
            }

            bool visible = _keys != null && _keys.ScreenshotNoticeSeconds > 0f;
            _labels[7].enabled = visible;
            if (!visible) return;

            _builder.Clear();
            _builder.Append("SAVED ").Append(_keys.LastScreenshotName);
            _labels[7].SetText(_builder);
            _labels[7].color = HudPalette.ScreenshotNotice;
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
