using TMPro;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The three faces the original HUD uses, gathered into one asset so every
    /// panel prefab references a single object instead of a font each.
    ///
    /// <c>draw_telemetry</c> asks GDI for Consolas at -12 and the wheel panel and
    /// speedometer ask for Segoe UI at -12, -18 and -58. The column alignment in
    /// the telemetry and status lines depends on the face being monospaced, so
    /// substituting a proportional font there would break the layout rather than
    /// just restyle it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "HudFontSet",
        menuName = "OrangeCarrrrr/HUD Font Set",
        order = 10)]
    public sealed class HudFontSet : ScriptableObject
    {
        [Tooltip("Consolas. Used by the status lines and the telemetry panel.")]
        [SerializeField] private TMP_FontAsset _mono;

        [Tooltip("Segoe UI Bold. Panel labels and the speedometer's unit.")]
        [SerializeField] private TMP_FontAsset _ui;

        [Tooltip("Segoe UI Black. The speedometer's three digits.")]
        [SerializeField] private TMP_FontAsset _uiHeavy;

        public TMP_FontAsset Mono => _mono;
        public TMP_FontAsset Ui => _ui != null ? _ui : _mono;
        public TMP_FontAsset UiHeavy => _uiHeavy != null ? _uiHeavy : Ui;

        public void Assign(TMP_FontAsset mono, TMP_FontAsset ui, TMP_FontAsset uiHeavy)
        {
            _mono = mono;
            _ui = ui;
            _uiHeavy = uiHeavy;
        }
    }
}
