using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The view-side keys from the original's HUD help line. Driving input lives
    /// in <see cref="SimulatorDriverInput"/>.
    ///
    ///   C  switch between the chase camera and the top-down projection
    ///   B  show or hide the AABB bounds
    ///   N  show or hide the course's checkpoint gates
    ///   U  cycle the engine sound preset
    ///   E  engine note: one ramp, or four gear bands
    ///   G  cycle the drift gauge's charging model
    ///   H  booster storage capped by the kart, or unlimited
    ///   P  the live parameter editor
    ///   Q  instant boost: opportunity window, or a stored charge
    ///   M  boost cutoff: throttle release, or reverse press
    ///   F  emulate the ground-drag trigger enter/leave (x4 / x0.25)
    ///   L  cycle the kart's paint through colortable.xml
    ///   T  open the track list (in the HUD's SelectionMenu, not here)
    ///   K  open the kart list (likewise)
    ///   S  save a screenshot
    ///   R  respawn onto the checkpoint the kart is at, half a second later
    ///
    /// F1 caps the frame rate and F2 switches between the original's race rule and
    /// the bench. Neither is in the original: the port has to be frame-rate
    /// independent, and being able to pin 60 or 40 on demand is how that gets
    /// checked rather than assumed; and a bench that ends after three laps is not
    /// a bench.
    /// </summary>
    [RequireComponent(typeof(SimulatorRoot))]
    public sealed class SimulatorKeys : MonoBehaviour
    {
        [SerializeField] private string _screenshotDirectory = "Screenshots";

        private SimulatorRoot _simulator;

        /// <summary>File name of the most recent screenshot, for the HUD notice.</summary>
        public string LastScreenshotName { get; private set; }

        /// <summary>Seconds left on the "SAVED ..." notice.</summary>
        public float ScreenshotNoticeSeconds { get; private set; }

        private void Awake() => _simulator = GetComponent<SimulatorRoot>();

        private void Update()
        {
            if (ScreenshotNoticeSeconds > 0f)
            {
                ScreenshotNoticeSeconds = Mathf.Max(0f, ScreenshotNoticeSeconds - Time.deltaTime);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _simulator == null) return;

            // T and K belong to the selection menu, which lives with the rest of
            // the HUD. While it is up it owns the keyboard outright — it moves on
            // the same arrows the kart steers with — so nothing here fires.
            if (_simulator.MenuOpen) return;

            if (keyboard.cKey.wasPressedThisFrame) _simulator.ToggleViewMode();
            if (keyboard.bKey.wasPressedThisFrame) _simulator.ShowBounds = !_simulator.ShowBounds;
            if (keyboard.rKey.wasPressedThisFrame) _simulator.RequestRespawn();
            if (keyboard.nKey.wasPressedThisFrame) _simulator.ShowCheckpoints = !_simulator.ShowCheckpoints;
            if (keyboard.fKey.wasPressedThisFrame) _simulator.ToggleDragTrigger();
            if (keyboard.lKey.wasPressedThisFrame) _simulator.NextKartColour();
            if (keyboard.eKey.wasPressedThisFrame) _simulator.ToggleGearMode();
            if (keyboard.gKey.wasPressedThisFrame) _simulator.NextGaugeModel();
            if (keyboard.uKey.wasPressedThisFrame) _simulator.NextEngineSound();
            if (keyboard.pKey.wasPressedThisFrame) Parameters();
            if (keyboard.qKey.wasPressedThisFrame) _simulator.ToggleStoredInstantBoost();
            if (keyboard.mKey.wasPressedThisFrame) _simulator.ToggleBoostCutoffModel();
            if (keyboard.hKey.wasPressedThisFrame) _simulator.ToggleUnlimitedBoosters();
            if (keyboard.f1Key.wasPressedThisFrame) _simulator.CycleFrameRateCap();
            if (keyboard.f2Key.wasPressedThisFrame) _simulator.ToggleRaceMode();
            if (keyboard.sKey.wasPressedThisFrame) CaptureScreenshot();
        }

        /// <summary>
        /// The <c>P</c> window, added to the simulator on first use so no scene
        /// has to carry a debug tool it may never open.
        /// </summary>
        private void Parameters()
        {
            var window = _simulator.GetComponent<ParameterWindow>();
            if (window == null) window = _simulator.gameObject.AddComponent<ParameterWindow>();
            window.Toggle();
        }

        private void CaptureScreenshot()
        {
            string directory = Path.Combine(Application.persistentDataPath, _screenshotDirectory);
            Directory.CreateDirectory(directory);

            string name = $"shot-{System.DateTime.Now:yyyyMMdd-HHmmss}.png";
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, name));

            LastScreenshotName = name;
            ScreenshotNoticeSeconds = 2f;
        }
    }
}
