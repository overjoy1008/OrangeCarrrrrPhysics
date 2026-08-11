using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The driving keys, read straight off the keyboard and turned into one
    /// frame's <see cref="KartSimulationControls"/>.
    ///
    ///   Up            accelerate
    ///   Down          brake, then reverse once the kart has been near a stop
    ///   Left / Right  steer
    ///   Shift or W    drift
    ///   Ctrl or D     item boost
    ///
    /// Steering goes through <see cref="KartSteeringInput"/> rather than a 1D Axis
    /// composite: the recovered rule is that the most recent direction owns the
    /// input, so holding both keys steers rather than cancelling to zero.
    /// Reproducing that with a composite is not possible, and losing it would
    /// remove the original's new-cut drift input.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/Simulator Driver Input")]
    public sealed class SimulatorDriverInput : MonoBehaviour
    {
        private KartSteeringInput _steering;

        /// <summary>The steering value the recovered ownership rule produced.</summary>
        public float SteeringValue => _steering.Value;

        public bool ForwardHeld { get; private set; }
        public bool ReverseHeld { get; private set; }
        public bool DriftHeld { get; private set; }
        public bool BoostHeld { get; private set; }

        private void OnDisable() => _steering.Reset();

        /// <summary>Samples the keyboard and builds this frame's controls.</summary>
        public KartSimulationControls Sample()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return default;

            if (keyboard.leftArrowKey.wasPressedThisFrame)
                _steering.KeyEvent(KartSteeringKey.Left, true);
            if (keyboard.leftArrowKey.wasReleasedThisFrame)
                _steering.KeyEvent(KartSteeringKey.Left, false);
            if (keyboard.rightArrowKey.wasPressedThisFrame)
                _steering.KeyEvent(KartSteeringKey.Right, true);
            if (keyboard.rightArrowKey.wasReleasedThisFrame)
                _steering.KeyEvent(KartSteeringKey.Right, false);

            ForwardHeld = keyboard.upArrowKey.isPressed;
            ReverseHeld = keyboard.downArrowKey.isPressed;
            DriftHeld = keyboard.leftShiftKey.isPressed ||
                        keyboard.rightShiftKey.isPressed ||
                        keyboard.wKey.isPressed;
            BoostHeld = keyboard.leftCtrlKey.isPressed ||
                        keyboard.rightCtrlKey.isPressed ||
                        keyboard.dKey.isPressed;

            return new KartSimulationControls
            {
                ForwardInput = ForwardHeld ? 1f : 0f,
                ReverseInput = ReverseHeld ? 1f : 0f,
                SteeringInput = _steering.Value,
                ReverseSteering = false,
                DriftInput = DriftHeld,
                BoostActive = BoostHeld,
                DriveDisabled = false,
            };
        }

        public void ResetSteering() => _steering.Reset();
    }
}
