using OrangeCarrrrr.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// Root of the HUD. Binds every widget to the simulator and re-applies the
    /// recovered pixel layout.
    ///
    /// Every panel is laid out in the original's own pixels, measured from the
    /// window edges of its 1264 x 781 client rect. The canvas then scales that
    /// whole layout to whatever window it is actually in, so the proportions stay
    /// the original's instead of the HUD shrinking into the corner of a larger
    /// screen.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Simulator HUD")]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class SimulatorHud : MonoBehaviour
    {
        /// <summary>The original demo's client rect, which every panel is placed in.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1264f, 781f);

        [SerializeField] private SimulatorRoot _simulator;

        [Header("Widgets")]
        [SerializeField] private HudStatusLines _statusLines;
        [SerializeField] private TelemetryPanel _telemetry;
        [SerializeField] private WheelLoadPanel _wheelLoad;
        [SerializeField] private Speedometer _speedometer;
        [SerializeField] private WorldAxisGizmo _axisGizmo;
        [SerializeField] private CountdownDisplay _countdown;
        [SerializeField] private MinimapPanel _minimap;

        /// <summary>The <c>T</c> and <c>K</c> lists. Made on first play.</summary>
        [SerializeField] private SelectionMenu _selectionMenu;

        private void OnEnable()
        {
            EnsureSelectionMenu();
            BindAll();
        }

        /// <summary>
        /// Adds the selection menu to the canvas.
        ///
        /// Built here rather than authored into the prefab because its rows come
        /// from the track and kart catalogs — fourteen and twenty-six — and a
        /// prefab copy of those lists would only be something to keep in step.
        /// Only while playing, so opening the HUD prefab never adds objects to it.
        /// </summary>
        private void EnsureSelectionMenu()
        {
            if (_selectionMenu == null)
            {
                _selectionMenu = GetComponentInChildren<SelectionMenu>(includeInactive: true);
            }
            if (_selectionMenu != null || !Application.isPlaying) return;

            var holder = new GameObject("Selection menu", typeof(RectTransform));
            holder.transform.SetParent(transform, worldPositionStays: false);
            _selectionMenu = holder.AddComponent<SelectionMenu>();
            _selectionMenu.Close();
        }

        private void BindAll()
        {
            if (_simulator == null)
            {
#if UNITY_2023_1_OR_NEWER
                _simulator = FindFirstObjectByType<SimulatorRoot>();
#else
                _simulator = FindObjectOfType<SimulatorRoot>();
#endif
            }
            if (_simulator == null) return;

            if (_statusLines != null) _statusLines.Bind(_simulator);
            if (_telemetry != null) _telemetry.Bind(_simulator);
            if (_wheelLoad != null) _wheelLoad.Bind(_simulator);
            if (_speedometer != null) _speedometer.Bind(_simulator);
            if (_axisGizmo != null) _axisGizmo.Bind(_simulator);
            if (_countdown != null) _countdown.Bind(_simulator);
            if (_minimap != null) _minimap.Bind(_simulator);
            if (_selectionMenu != null) _selectionMenu.Bind(_simulator);
        }

        /// <summary>
        /// Re-applies every widget's geometry from the recovered constants. Called
        /// by the prefab builder, and available from the inspector so a layout
        /// nudged by hand can be put back.
        /// </summary>
        [ContextMenu("Apply recovered layout")]
        public void ApplyLayout()
        {
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            if (_statusLines != null) _statusLines.ApplyLayout();
            if (_telemetry != null) _telemetry.ApplyLayout();
            if (_wheelLoad != null) _wheelLoad.ApplyLayout();
            if (_speedometer != null) _speedometer.ApplyLayout();
            if (_axisGizmo != null) _axisGizmo.ApplyLayout();
            if (_countdown != null) _countdown.ApplyLayout();
            if (_minimap != null) _minimap.ApplyLayout();
        }

        public void Bind(SimulatorRoot simulator)
        {
            _simulator = simulator;
            BindAll();
        }
    }
}
