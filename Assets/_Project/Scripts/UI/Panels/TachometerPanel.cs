using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_tachometer</c>: the engine note as a needle dial.
    ///
    /// It is labelled "RPM (motor pitch)" for a reason. There is no gear or
    /// crankshaft in the recovered engine — the note is one straight ramp in
    /// speed, <c>speed * 0.01171875 + 0.25</c>, capped at 1.5 above speed 128 —
    /// so this plots that multiplier directly rather than dressing it up as a
    /// rev counter. The driver only recomputes every 64 ms, so the needle holds
    /// between refreshes exactly as the sound does.
    ///
    /// The gear read-out beside it belongs to the <see cref="KartGearbox"/>
    /// experiment and is only there in multi.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Tachometer Panel")]
    public sealed class TachometerPanel : HudWidget
    {
        public const float PanelWidth = 238f;
        public const float PanelHeight = 132f;
        public const float Margin = 18f;

        /// <summary>
        /// Clear of the wheel-load panel, which is itself clear of the
        /// speedometer: 18 + 94 + 8 + 118 + 8, the original's own stack.
        /// </summary>
        public const float BottomOffset = 246f;

        /// <summary>The dial's centre, measured down from the panel's top.</summary>
        public const float CentreFromTop = 96f;

        private PanelBox _panel;
        private TachometerDial _dial;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _pitch;
        private TextMeshProUGUI _volume;
        private TextMeshProUGUI _gear;
        private TextMeshProUGUI _top;

        protected override void Refresh()
        {
            if (Simulator == null) return;

            Build();

            float speed = Simulator.State != null ? Simulator.State.Speed : 0f;

            KartGearbox gearbox = Simulator.Gearbox;
            bool multi = gearbox != null && gearbox.Mode == KartGearMode.Multi;

            // The note the sound driver is actually holding — in multi the
            // gearbox has already overwritten it on the voice, so this is the
            // sawtooth rather than the ramp, and the dial and the engine agree
            // because they are reading the same number.
            float pitch = Simulator.MotorPitch;
            float volume = Simulator.MotorVolume;

            // The uncapped recovered ramp is only a reference for single: in
            // multi there is nothing for it to be a reference to, and the
            // original passes zero so the marker is not drawn.
            float ramp = multi
                ? 0f
                : speed * KartSoundConstants.MotorSlope + KartSoundConstants.MotorBase;

            _dial.SetReading(pitch, ramp);
            _pitch.SetText($"{pitch:F3}x");
            _volume.SetText($"vol {volume:F2}");

            if (multi)
            {
                _gear.color = HudPalette.TachometerGear;
                _gear.SetText($"GEAR {gearbox.Gear}/{KartGearbox.GearCount}");
                _top.SetText($"top {KartGearbox.Bands[KartGearbox.GearCount - 1].HighPitch:F2}");
            }
            else
            {
                _gear.color = HudPalette.TachometerDim;
                _gear.SetText("1 gear");
                _top.SetText($"cap {KartSoundConstants.MotorPitchCap:F1}");
            }
        }

        private void Build()
        {
            if (_panel != null) return;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-Margin, BottomOffset);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            _panel = gameObject.AddComponent<PanelBox>();
            _panel.color = HudPalette.PanelFill;
            _panel.BorderColor = HudPalette.TelemetryPanelBorder;
            _panel.BorderWidth = 1f;
            _panel.raycastTarget = false;

            // The dial is centred on the original's point rather than on the
            // panel: the labels take the top of it.
            var dialHolder = new GameObject("Dial", typeof(RectTransform));
            var dialRect = (RectTransform)dialHolder.transform;
            dialRect.SetParent(transform, worldPositionStays: false);
            dialRect.anchorMin = new Vector2(0.5f, 1f);
            dialRect.anchorMax = new Vector2(0.5f, 1f);
            dialRect.pivot = new Vector2(0.5f, 0.5f);
            dialRect.anchoredPosition = new Vector2(0f, -CentreFromTop);
            dialRect.sizeDelta = new Vector2(TachometerDial.Radius * 2f, TachometerDial.Radius * 2f);
            _dial = dialHolder.AddComponent<TachometerDial>();
            _dial.raycastTarget = false;

            _title = AddLabel("Title", 9f, 6f, 200f, HudPalette.TachometerTitle,
                              TextAlignmentOptions.TopLeft);
            _title.SetText("RPM (motor pitch)");

            _pitch = AddLabel("Pitch", 12f, 26f, 90f, HudPalette.TachometerNeedle,
                              TextAlignmentOptions.TopLeft);
            _volume = AddLabel("Volume", 12f, 44f, 90f, HudPalette.TachometerDim,
                               TextAlignmentOptions.TopLeft);

            _gear = AddLabel("Gear", PanelWidth - 84f, 26f, 74f, HudPalette.TachometerDim,
                             TextAlignmentOptions.TopRight);
            _top = AddLabel("Top", PanelWidth - 94f, 44f, 84f, HudPalette.TachometerDim,
                            TextAlignmentOptions.TopRight);
        }

        /// <summary>A label placed from the panel's top-left, in the original's pixels.</summary>
        private TextMeshProUGUI AddLabel(
            string name, float left, float top, float width, Color color,
            TextAlignmentOptions alignment)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(transform, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, 16f);

            var label = holder.AddComponent<TextMeshProUGUI>();

            var canvas = GetComponentInParent<Canvas>();
            var sample = canvas != null
                ? canvas.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)
                : null;
            if (sample != null) label.font = sample.font;

            label.fontSize = 12f;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }
    }
}
