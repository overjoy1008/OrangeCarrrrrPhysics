using System;

namespace OrangeCarrrrr.Core
{
    public static partial class KartDynamics
    {
        /// <summary>
        /// Recovered from 0x00431a30. A press only takes while the linger timer
        /// has run out, which is what stops a held key from re-arming the trigger
        /// every frame.
        /// </summary>
        public static void DriftSetInput(
            ref KartDriftState state,
            bool pressed,
            float forwardVelocity)
        {
            if (!pressed)
            {
                state.InputActive = false;
                return;
            }

            if (state.LingerTimer <= 0f)
            {
                state.InputActive = true;
                state.TriggerActive = true;
                state.EntryWasForward = forwardVelocity > 0f;
            }
        }

        /// <summary>
        /// Recovered from 0x0042fff0-0x00430076. The first step arms the timers;
        /// later steps count the trigger down. The linger window is twice the
        /// trigger time.
        /// </summary>
        public static void DriftStepTrigger(
            ref KartDriftState state,
            in KartDynamicsConfig config,
            float dt)
        {
            if (!state.TriggerActive) return;

            if (state.TriggerTimer <= 0f)
            {
                state.TriggerTimer = config.DriftTriggerTime;
                state.LingerTimer = config.DriftTriggerTime * 2f;
                return;
            }

            state.TriggerTimer -= dt;
            if (state.TriggerTimer <= 0f)
            {
                state.TriggerTimer = 0f;
                state.TriggerActive = false;
            }
        }

        /// <summary>Recovered from 0x0042fef8-0x0042ff0c.</summary>
        public static void DriftClearForLowSpeed(ref KartDriftState state)
        {
            state.TriggerActive = false;
            state.InputActive = false;
            state.SlipDetected = false;
        }

        /// <summary>
        /// Recovered from 0x0042fef8-0x0042ff58. Automatic slip only latches when
        /// the driver is not already drifting, and only above the 5-unit speed
        /// floor — below it the whole drift state is cleared.
        /// </summary>
        public static void DriftUpdateSlipDetection(
            ref KartDriftState state,
            float speed,
            float forwardVelocity,
            float lateralVelocity)
        {
            const float slipRatio = 1.2000000476837158f;

            state.SlipDetected = false;
            if (speed <= KartConstants.LowSpeedDenominator)
            {
                DriftClearForLowSpeed(ref state);
                return;
            }

            if (!state.InputActive && !state.TriggerActive)
            {
                state.SlipDetected =
                    MathF.Abs(lateralVelocity) > MathF.Abs(forwardVelocity) * slipRatio;
            }
        }

        /// <summary>Recovered from 0x00430244-0x0043026e.</summary>
        public static void DriftStepLinger(ref KartDriftState state, float dt)
            => state.LingerTimer = MathF.Max(state.LingerTimer - dt, 0f);
    }
}
