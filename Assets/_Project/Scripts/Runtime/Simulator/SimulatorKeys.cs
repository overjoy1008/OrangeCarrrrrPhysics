using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The view-side keys from the original's HUD help line. Driving input lives
    /// in <see cref="SimulatorDriverInput"/>.
    ///
    /// Letters are the things you reach for while driving:
    ///
    ///   C  open the paint list (in the HUD's SelectionMenu, not here)
    ///   I  cycle the skid face
    ///   R  respawn onto the checkpoint the kart is at, half a second later
    ///   F  emulate the ground-drag trigger enter/leave (x4 / x0.25)
    ///   E  engine note: one ramp, or four gear bands
    ///   P  the live parameter editor
    ///   T  open the track list (in the HUD's SelectionMenu, not here)
    ///   K  open the kart list (likewise)
    ///
    /// The function row is the view and the bench rules — nothing that changes how
    /// the kart drives:
    ///
    ///   F1 race rule: the original's, or the bench's
    ///   F2 save a screenshot
    ///   F3 show or hide the course's checkpoint gates
    ///   F4 show or hide the kart's AABB bounds
    ///   F5 switch between the chase camera and the top-down projection
    ///   F6 cap the frame rate
    ///
    /// The number row is the experimental layers, and only those — see
    /// <c>HudStatusLines.WriteExperimental</c> for what each one is comparing:
    ///
    ///   1 gauge charging model   2 short booster   3 booster storage
    ///   4 booster starter        5 booster stopper
    ///
    /// The split is the point. A key on the number row changes a hypothesis the
    /// simulator is being run against; a key anywhere else does not. F1, F2 and F6
    /// are not the original's either — the port has to be frame-rate independent
    /// and pinning 60 or 40 on demand is how that gets checked rather than
    /// assumed, and a bench that ends after three laps is not a bench.
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

            if (keyboard.iKey.wasPressedThisFrame) _simulator.NextSkidStyle();
            if (keyboard.rKey.wasPressedThisFrame) _simulator.RequestRespawn();
            if (keyboard.fKey.wasPressedThisFrame) _simulator.ToggleDragTrigger();
            if (keyboard.eKey.wasPressedThisFrame) _simulator.ToggleGearMode();
            if (keyboard.pKey.wasPressedThisFrame) Parameters();

            if (keyboard.f1Key.wasPressedThisFrame) _simulator.ToggleRaceMode();
            if (keyboard.f2Key.wasPressedThisFrame) CaptureScreenshot();
            if (keyboard.f3Key.wasPressedThisFrame) _simulator.ShowCheckpoints = !_simulator.ShowCheckpoints;
            if (keyboard.f4Key.wasPressedThisFrame) _simulator.ShowBounds = !_simulator.ShowBounds;
            if (keyboard.f5Key.wasPressedThisFrame) _simulator.ToggleViewMode();
            if (keyboard.f6Key.wasPressedThisFrame) _simulator.CycleFrameRateCap();

            // The experimental layers are on the number row, together, so that a
            // key that changes what is being compared is never next to one that
            // only changes what is on screen.
            if (keyboard.digit1Key.wasPressedThisFrame) _simulator.NextGaugeModel();
            if (keyboard.digit2Key.wasPressedThisFrame) _simulator.ToggleStoredInstantBoost();
            if (keyboard.digit3Key.wasPressedThisFrame) _simulator.ToggleUnlimitedBoosters();
            if (keyboard.digit4Key.wasPressedThisFrame) _simulator.ToggleNoDelayBoost();
            if (keyboard.digit5Key.wasPressedThisFrame) _simulator.ToggleBoostCutoffModel();
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
