using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// Every colour the original HUD draws with, transcribed from the RGB()
    /// literals in <c>kart_win32.c</c>, <c>kart_demo_win32_ui.h</c> and
    /// <c>kart_axis_gizmo_win32.h</c>.
    ///
    /// These are UI colours written by a GDI rasterizer that did no colour
    /// management, so they are used as raw sRGB byte values here too.
    /// </summary>
    public static class HudPalette
    {
        // draw_scene: the status lines.
        public static readonly Color StatusText = new Color32(235, 240, 245, 255);
        public static readonly Color StatusDim = new Color32(180, 195, 205, 255);
        public static readonly Color StatusDrift = new Color32(255, 185, 55, 255);
        public static readonly Color StatusBoost = new Color32(75, 225, 255, 255);
        public static readonly Color StatusExperimental = new Color32(190, 165, 240, 255);
        public static readonly Color ScreenshotNotice = new Color32(140, 235, 255, 255);

        /// <summary>The wrong-way flag and the respawn notice, which share a colour.</summary>
        public static readonly Color StatusWrongWay = new Color32(255, 120, 120, 255);

        // kart_demo_draw_tachometer.
        public static readonly Color TachometerTitle = new Color32(180, 205, 215, 255);
        public static readonly Color TachometerNeedle = new Color32(255, 210, 90, 255);
        public static readonly Color TachometerDim = new Color32(150, 165, 180, 255);
        public static readonly Color TachometerGear = new Color32(120, 215, 245, 255);

        // kart_demo_draw_gauge: the drift gauge along the bottom.
        public static readonly Color GaugeBack = new Color32(14, 18, 24, 255);
        public static readonly Color GaugeEdge = new Color32(86, 98, 108, 255);
        public static readonly Color GaugeFill = new Color32(255, 210, 90, 255);
        public static readonly Color GaugeSlotFull = new Color32(255, 140, 40, 255);
        public static readonly Color GaugeSlotEmpty = new Color32(30, 38, 46, 255);
        public static readonly Color GaugeCount = new Color32(255, 170, 70, 255);
        public static readonly Color GaugeLabel = new Color32(200, 212, 220, 255);

        // draw_telemetry.
        public static readonly Color TelemetryPanelFill = new Color32(15, 19, 26, 255);
        public static readonly Color TelemetryPanelBorder = new Color32(54, 64, 73, 255);
        public static readonly Color TelemetryNeutral = new Color32(200, 212, 220, 255);
        public static readonly Color TelemetryVelocity = new Color32(120, 215, 245, 255);
        public static readonly Color TelemetryAcceleration = new Color32(255, 175, 110, 255);
        public static readonly Color TelemetryGood = new Color32(150, 205, 165, 255);
        public static readonly Color TelemetryAlert = new Color32(255, 140, 120, 255);
        public static readonly Color TelemetryDriftTrigger = new Color32(255, 210, 90, 255);
        public static readonly Color TelemetryDriftArmed = new Color32(255, 185, 55, 255);
        public static readonly Color TelemetryStepIdle = new Color32(150, 165, 180, 255);

        // kart_demo_draw_wheel_load.
        public static readonly Color PanelFill = new Color32(12, 16, 22, 255);
        public static readonly Color PanelBorder = new Color32(54, 64, 73, 255);
        public static readonly Color WheelPanelLabel = new Color32(180, 205, 215, 255);
        public static readonly Color WheelBodyFill = new Color32(38, 46, 55, 255);
        public static readonly Color WheelBodyBorder = new Color32(96, 110, 122, 255);
        public static readonly Color WheelIdleFill = new Color32(70, 46, 50, 255);
        public static readonly Color WheelLoadedText = new Color32(235, 245, 240, 255);
        public static readonly Color WheelIdleText = new Color32(190, 130, 135, 255);

        // kart_demo_draw_speedometer.
        public static readonly Color SpeedometerBorder = new Color32(110, 130, 145, 255);
        public static readonly Color SpeedometerDigits = new Color32(245, 248, 250, 255);
        public static readonly Color SpeedometerBoostDigits = new Color32(90, 235, 255, 255);
        public static readonly Color SpeedometerUnit = new Color32(175, 195, 205, 255);

        // kart_axis_gizmo_win32.h.
        public static readonly Color AxisX = new Color32(255, 96, 96, 255);
        public static readonly Color AxisY = new Color32(120, 235, 120, 255);
        public static readonly Color AxisZ = new Color32(120, 170, 255, 255);
        public static readonly Color AxisLabel = new Color32(150, 150, 160, 255);

        /// <summary>
        /// The wheel-load fill ramp: <c>shade = 60 + 170 * clamp(compression)</c>,
        /// laid out as <c>RGB(shade / 3, shade, shade / 2)</c>.
        /// </summary>
        public static Color WheelLoadFill(float compression)
        {
            float clamped = Mathf.Clamp01(compression);
            int shade = (int)(60f + 170f * clamped);
            return new Color32((byte)(shade / 3), (byte)shade, (byte)(shade / 2), 255);
        }
    }
}
