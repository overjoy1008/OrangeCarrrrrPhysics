using System.Collections.Generic;
using OrangeCarrrrr.Core;
using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// <c>kart_demo_draw_gauge</c>: the drift gauge along the bottom of the
    /// window, with the stored boosters beside it.
    ///
    /// A filled bar of the gauge's charge, the charging model's name written
    /// across it, and to its right either one box per booster slot or, when the
    /// storage cap is off, a plain count. Every measurement here is the
    /// original's: 420 x 20 at 20 from the bottom, a 2 px inset on the fill, and
    /// 26-wide slots 6 apart.
    ///
    /// The gauge itself is a simulator-side layer rather than recovered code —
    /// see <see cref="KartGauge"/> — so this panel is drawing a bench readout,
    /// not a 2004 screen.
    ///
    /// It builds its own children: the number of slots comes from the kart's
    /// <c>max_boosters</c> rather than from the layout, so there is nothing to
    /// author in advance.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Gauge Panel")]
    public sealed class GaugePanel : HudWidget
    {
        public const float BarWidth = 420f;
        public const float BarHeight = 20f;
        public const float Margin = 20f;

        /// <summary>The fill's inset inside the bar, on every side.</summary>
        public const float FillInset = 2f;

        public const float SlotWidth = 26f;
        public const float SlotGap = 6f;

        /// <summary>The gap between the bar and the first slot.</summary>
        public const float SlotOffset = 10f;

        private PanelBox _bar;
        private PanelBox _fill;
        private RectTransform _fillRect;
        private TextMeshProUGUI _model;
        private TextMeshProUGUI _count;
        private readonly List<PanelBox> _slots = new List<PanelBox>();

        private float _shownRatio = -1f;

        protected override void Refresh()
        {
            if (Simulator == null) return;

            KartGauge gauge = Simulator.Gauge;
            if (gauge == null) return;

            Build();

            float ratio = gauge.Ratio;
            if (!Mathf.Approximately(ratio, _shownRatio))
            {
                _shownRatio = ratio;
                _fillRect.sizeDelta = new Vector2(
                    (BarWidth - FillInset * 2f) * ratio, -FillInset * 2f);
                _fill.enabled = ratio > 0f;
            }

            _model.SetText(KartGauge.ModelName(gauge.Model));

            uint max = Simulator.Kart != null ? Simulator.Kart.ToSpec().MaxBoosters : 0u;
            ShowBoosters(gauge, max);
        }

        /// <summary>
        /// The slots, or the count that replaces them when the cap is off. The
        /// original prints "BOOSTERS xN" there rather than drawing an unbounded
        /// row of boxes.
        /// </summary>
        private void ShowBoosters(KartGauge gauge, uint max)
        {
            bool unlimited = gauge.UnlimitedBoosters;

            _count.enabled = unlimited;
            if (unlimited) _count.SetText($"BOOSTERS x{gauge.Boosters}");

            EnsureSlots(unlimited ? 0 : (int)max);
            for (int slot = 0; slot < _slots.Count; ++slot)
            {
                _slots[slot].color = slot < gauge.Boosters
                    ? HudPalette.GaugeSlotFull
                    : HudPalette.GaugeSlotEmpty;
            }
        }

        private void EnsureSlots(int wanted)
        {
            for (int slot = _slots.Count; slot < wanted; ++slot)
            {
                PanelBox box = AddBox(
                    $"Slot{slot:00}", HudPalette.GaugeSlotEmpty, HudPalette.GaugeEdge, 1f);

                var rect = (RectTransform)box.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(
                    BarWidth * 0.5f + SlotOffset + slot * (SlotWidth + SlotGap), Margin);
                rect.sizeDelta = new Vector2(SlotWidth, BarHeight);

                _slots.Add(box);
            }

            for (int slot = 0; slot < _slots.Count; ++slot)
            {
                _slots[slot].enabled = slot < wanted;
            }
        }

        private void Build()
        {
            if (_bar != null) return;

            var self = (RectTransform)transform;
            self.anchorMin = Vector2.zero;
            self.anchorMax = Vector2.one;
            self.offsetMin = Vector2.zero;
            self.offsetMax = Vector2.zero;

            _bar = AddBox("Bar", HudPalette.GaugeBack, HudPalette.GaugeEdge, 1f);
            var barRect = (RectTransform)_bar.transform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, Margin);
            barRect.sizeDelta = new Vector2(BarWidth, BarHeight);

            // Inside the bar and pinned to its left edge, so growing it to the
            // right is a width change and nothing else.
            _fill = AddBox("Fill", HudPalette.GaugeFill, Color.clear, 0f);
            _fillRect = (RectTransform)_fill.transform;
            _fillRect.SetParent(barRect, worldPositionStays: false);
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = new Vector2(FillInset, 0f);
            _fillRect.sizeDelta = new Vector2(0f, -FillInset * 2f);

            _model = AddLabel("Model", HudPalette.GaugeLabel, TextAlignmentOptions.Center);
            var modelRect = (RectTransform)_model.transform;
            modelRect.SetParent(barRect, worldPositionStays: false);
            modelRect.anchorMin = Vector2.zero;
            modelRect.anchorMax = Vector2.one;
            modelRect.offsetMin = Vector2.zero;
            modelRect.offsetMax = Vector2.zero;

            _count = AddLabel("Count", HudPalette.GaugeCount, TextAlignmentOptions.MidlineLeft);
            var countRect = (RectTransform)_count.transform;
            countRect.anchorMin = new Vector2(0.5f, 0f);
            countRect.anchorMax = new Vector2(0.5f, 0f);
            countRect.pivot = new Vector2(0f, 0f);
            countRect.anchoredPosition = new Vector2(BarWidth * 0.5f + SlotOffset, Margin);
            countRect.sizeDelta = new Vector2(200f, BarHeight);
        }

        private PanelBox AddBox(string name, Color fill, Color border, float borderWidth)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);

            var box = holder.AddComponent<PanelBox>();
            box.color = fill;
            box.BorderColor = border;
            box.BorderWidth = borderWidth;
            box.raycastTarget = false;
            return box;
        }

        private TextMeshProUGUI AddLabel(string name, Color color, TextAlignmentOptions alignment)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);

            var label = holder.AddComponent<TextMeshProUGUI>();

            // The HUD's own face, taken from a label already on the canvas so the
            // Korean fallback and the look come along with it.
            var sample = GetComponentInParent<Canvas>() != null
                ? GetComponentInParent<Canvas>().GetComponentInChildren<TextMeshProUGUI>(true)
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
