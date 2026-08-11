using System;
using System.Collections.Generic;
using System.Text;
using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The bottom-left read-out from <c>draw_telemetry</c>: the whole rigid-body
    /// state, one fixed-width row per quantity.
    ///
    /// The C version writes sixteen rows from one macro. Here each row is an entry
    /// in <see cref="Rows"/> that knows how to format itself and what colour it
    /// takes, so a row belonging to a feature that is out of scope is simply
    /// absent from the list rather than printing zeros. Jump, the drift gauge and
    /// the instant-boost store are the three still missing; adding one back is one
    /// entry.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Telemetry Panel")]
    public sealed class TelemetryPanel : HudWidget
    {
        public const float LineHeight = 15f;
        public const float PanelWidth = 396f;
        public const float Margin = 16f;
        public const float TextInsetX = 9f;
        public const float TextInsetY = 7f;
        public const float PanelPadding = 14f;

        /// <summary>One formatted row of the panel.</summary>
        public readonly struct Row
        {
            public readonly Action<StringBuilder, KartSimulationState> Format;
            public readonly Func<KartSimulationState, Color> Colour;

            public Row(
                Action<StringBuilder, KartSimulationState> format,
                Func<KartSimulationState, Color> colour)
            {
                Format = format;
                Colour = colour;
            }
        }

        [SerializeField] private PanelBox _panel;
        [SerializeField] private RectTransform _rowRoot;
        [SerializeField] private HudFontSet _fonts;
        [SerializeField, Min(6f)] private float _fontSize = 12f;

        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private readonly StringBuilder _builder = new StringBuilder(160);

        private Row[] Rows => _rows ??= BuildRows();

        private Row[] _rows;

        private Row[] BuildRows() => new[]
        {
            new Row(FormatPhase, ColourPhase),
            new Row(FormatPosition, _ => HudPalette.TelemetryNeutral),
            new Row(FormatVelocity, _ => HudPalette.TelemetryVelocity),
            new Row(FormatAcceleration, _ => HudPalette.TelemetryAcceleration),
            new Row(FormatOmega, _ => HudPalette.TelemetryNeutral),
            new Row(FormatAxis, _ => HudPalette.TelemetryNeutral),
            new Row(FormatGround, ColourGround),
            new Row(FormatSteer, _ => HudPalette.TelemetryNeutral),
            new Row(FormatDrift, _ => HudPalette.TelemetryNeutral),
            new Row(FormatBoost, ColourBoost),
            new Row(FormatForces, _ => HudPalette.TelemetryNeutral),
            new Row(FormatDrag, _ => HudPalette.TelemetryNeutral),

            // Between DRAG and STEP, where the original has it. This row reads the
            // gauge rather than the kart, and the gauge belongs to the simulator —
            // hence the closures, which is also why BuildRows is an instance
            // method and the rows are cached per panel.
            new Row((text, _) => FormatGauge(text), _ => ColourGauge()),

            new Row(FormatStep, ColourStep),
        };

        public int RowCount => Rows.Length;

        /// <summary>Panel height for the current row count, the way the C code sizes it.</summary>
        public float PanelHeight => RowCount * LineHeight + PanelPadding;

        protected override void OnEnable()
        {
            CollectLabels();
            base.OnEnable();
        }

        /// <summary>
        /// Gathers the row labels, cloning the first one until there is one per
        /// entry in <see cref="Rows"/>. Adding a row to the table is then the only
        /// edit needed — the prefab keeps up on its own instead of having to be
        /// rebuilt whenever the recovered read-out grows.
        /// </summary>
        private void CollectLabels()
        {
            _labels.Clear();
            if (_rowRoot == null) return;

            for (int i = 0; i < _rowRoot.childCount; ++i)
            {
                var label = _rowRoot.GetChild(i).GetComponent<TextMeshProUGUI>();
                if (label != null) _labels.Add(label);
            }

            // Laid out every time, so a row that appeared since the last pass is
            // placed at its own index instead of sitting on top of another. This
            // is what a stale row saved into a scene used to collide over.
            ApplyLayout();

            if (_labels.Count == 0 || _labels.Count >= Rows.Length) return;

            // Only while playing. Under ExecuteAlways this used to clone in edit
            // mode too, and the clone was then saved into whatever scene was open
            // as a prefab-instance addition — which is how fourteen scenes ended
            // up carrying a row the prefab now has of its own.
            if (!Application.isPlaying) return;

            TextMeshProUGUI template = _labels[0];
            while (_labels.Count < Rows.Length)
            {
                TextMeshProUGUI clone = Instantiate(template, _rowRoot);
                clone.name = $"Row{_labels.Count:00}";
                _labels.Add(clone);
            }
            ApplyLayout();
        }

        protected override void Refresh()
        {
            if (Simulator == null) return;
            if (_rowRoot != null && _labels.Count != _rowRoot.childCount) CollectLabels();

            KartSimulationState kart = Simulator.State;
            if (kart == null) return;

            int count = Mathf.Min(_labels.Count, Rows.Length);
            for (int i = 0; i < count; ++i)
            {
                _builder.Clear();
                Rows[i].Format(_builder, kart);
                _labels[i].SetText(_builder);
                _labels[i].color = Rows[i].Colour(kart);
            }
        }

        /// <summary>
        /// Applies the panel geometry and font from the recovered constants. Used
        /// by the prefab builder and by <c>OnValidate</c> so the layout cannot
        /// drift away from the C source by hand-editing.
        /// </summary>
        public void ApplyLayout()
        {
            // The widget root fills the canvas; the panel is placed inside it at
            // the margin the C code measures from the window's bottom-left.
            HudStatusLines.StretchToParent((RectTransform)transform);

            if (_panel != null)
            {
                var rect = (RectTransform)_panel.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(Margin, Margin);
                rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
                _panel.color = HudPalette.TelemetryPanelFill;
                _panel.BorderColor = HudPalette.TelemetryPanelBorder;
                _panel.BorderWidth = 1f;
            }

            if (_rowRoot == null) return;
            for (int i = 0; i < _rowRoot.childCount; ++i)
            {
                var rect = (RectTransform)_rowRoot.GetChild(i);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(TextInsetX, -(TextInsetY + i * LineHeight));
                rect.sizeDelta = new Vector2(PanelWidth - TextInsetX * 2f, LineHeight);

                var label = rect.GetComponent<TextMeshProUGUI>();
                if (label == null) continue;
                label.fontSize = _fontSize;
                if (_fonts != null && _fonts.Mono != null) label.font = _fonts.Mono;
            }
        }

        // ---- row formatters, one per TELEMETRY_LINE in draw_telemetry ----

        private static void FormatPhase(StringBuilder text, KartSimulationState kart)
        {
            float slip = KartUnits.SlipAngleDegrees(kart.ForwardSpeed, kart.LateralSpeed);
            text.AppendFormat("PHASE   {0,-8} beta {1,5:F1} deg  (auto slip at 50)",
                kart.Drift.PhaseName, slip);
        }

        private static Color ColourPhase(KartSimulationState kart)
        {
            if (kart.Drift.TriggerActive) return HudPalette.TelemetryDriftTrigger;
            if (kart.Drift.SlipDetected) return HudPalette.TelemetryAlert;
            if (kart.Drift.InputActive) return HudPalette.TelemetryDriftArmed;
            return HudPalette.TelemetryGood;
        }

        private static void FormatPosition(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("POS     {0,8:F2} {1,8:F2} {2,8:F2}",
                kart.Position.X, kart.Position.Y, kart.Position.Z);

        private static void FormatVelocity(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("VEL     {0,8:F2} {1,8:F2} {2,8:F2}  |v| {3,6:F2}",
                kart.LinearVelocity.X, kart.LinearVelocity.Y, kart.LinearVelocity.Z, kart.Speed);

        private static void FormatAcceleration(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("ACC     {0,8:F2} {1,8:F2} {2,8:F2}  |a| {3,6:F2}",
                kart.Acceleration.X, kart.Acceleration.Y, kart.Acceleration.Z,
                kart.Acceleration.Magnitude);

        private static void FormatOmega(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("OMEGA   {0,8:F3} {1,8:F3} {2,8:F3}  yaw {3,6:F1} d/s",
                kart.AngularVelocity.X, kart.AngularVelocity.Y, kart.AngularVelocity.Z,
                kart.AngularVelocity.Z * KartUnits.DegreesPerRadian);

        private static void FormatAxis(StringBuilder text, KartSimulationState kart)
        {
            kart.GetBodyAxes(out _, out _, out KartVec3 up);
            text.AppendFormat("AXIS    vf {0,6:F2}  vs {1,6:F2}  up_z {2,5:F3}",
                kart.ForwardSpeed, kart.LateralSpeed, up.Z);
        }

        private static void FormatGround(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("GROUND  {0,-3} contacts {1}  (loads in the wheel panel)",
                kart.Grounded ? "yes" : "AIR", kart.LastStep.WheelContacts);

        private static Color ColourGround(KartSimulationState kart)
            => kart.Grounded ? HudPalette.TelemetryGood : HudPalette.TelemetryAlert;

        private void FormatSteer(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("STEER   in {0,5:F2}  applied {1,6:F2} deg  hyst {2,6:F2}",
                Simulator != null ? Simulator.Controls.SteeringInput : 0f,
                kart.PreviousSteerAngleRad * KartUnits.DegreesPerRadian,
                kart.Config.MaxSteerAngleDeg);

        /// <summary>
        /// The item boost's remaining milliseconds, whether the instant boost is
        /// running, and how much of its opportunity window is left.
        /// </summary>
        private static void FormatBoost(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("BOOST   item {0,5:F2}s  inst {1,-3} {2,5:F2}s  opp {3,5:F2}s",
                kart.TimedBoost.RemainingMs * 0.001f,
                kart.InstantBoost.Active ? "ON" : "off",
                kart.InstantBoost.ActiveTimer,
                kart.InstantBoost.OpportunityTimer);

        private static Color ColourBoost(KartSimulationState kart)
            => KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost)
                ? HudPalette.StatusBoost
                : HudPalette.TelemetryNeutral;

        private static void FormatDrift(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("DRIFT   linger {0,5:F2}  trigger {1,5:F2}  entry {2}",
                kart.Drift.LingerTimer, kart.Drift.TriggerTimer,
                kart.Drift.EntryWasForward ? "fwd" : "-");

        private static void FormatForces(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("FORCES  fwd {0,6:F0}  brake {1,6:F0}  Gf {2,4:F2}  Gr {3,4:F2}",
                kart.Config.ForwardAccelForce, kart.Config.GripBrakeForce,
                kart.Config.FrontGripFactor, kart.Config.RearGripFactor);

        private static void FormatDrag(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("DRAG    scale x{0,4:F2}  air {1,5:F2}  ground {2,5:F3}  m {3,5:F1}",
                kart.GroundedDragScale, kart.Config.AirFriction,
                kart.Config.DragFactor, kart.Config.Mass);

        /// <summary>
        /// The drift gauge's own readout: which hypothesis is charging it, how
        /// full it is, how fast it is filling, the suspension model's contact
        /// weight, and the stored boosters against the kart's cap.
        /// </summary>
        private void FormatGauge(StringBuilder text)
        {
            KartGauge gauge = Simulator != null ? Simulator.Gauge : null;
            if (gauge == null) { text.Append("GAUGE   -"); return; }

            uint max = Simulator.Kart != null ? Simulator.Kart.ToSpec().MaxBoosters : 0u;
            text.AppendFormat(
                "GAUGE   {0,-17} {1,5:F1}%  rate {2,6:F2}  W {3,4:F2}  store {4} {5}/{6}",
                KartGauge.ModelName(gauge.Model),
                gauge.Ratio * 100f,
                gauge.Rate,
                gauge.ContactWeight,
                gauge.UnlimitedBoosters ? "unlimited" : "capped",
                gauge.Boosters,
                max);
        }

        /// <summary>Lit while it is actually charging, the way the original lights it.</summary>
        private Color ColourGauge()
            => Simulator != null && Simulator.Gauge != null && Simulator.Gauge.Rate > 0f
                ? HudPalette.GaugeFill
                : HudPalette.TelemetryNeutral;

        private static void FormatStep(StringBuilder text, KartSimulationState kart)
            => text.AppendFormat("STEP    sub {0,2}  wheels {1}  body {2}  wall {3,4:F1}  gnd {4,4:F1}",
                kart.LastStep.Substeps, kart.LastStep.WheelContacts, kart.LastStep.BodyContacts,
                kart.LastStep.WallImpactSpeed, kart.LastStep.GroundImpactSpeed);

        private static Color ColourStep(KartSimulationState kart)
            => kart.LastStep.BodyContacts != 0u
                ? HudPalette.TelemetryAlert
                : HudPalette.TelemetryStepIdle;
    }
}
