using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The recovered instant boost: finishing a forward drift opens a half-second
    /// window, and pressing the accelerator inside it applies the forward-force
    /// multiplier for another half second.
    ///
    /// The simulator's alternate "stored" model — where the window banks a charge
    /// to spend later — is deliberately not ported: it is a hypothesis rather
    /// than recovered behaviour, and it is out of scope.
    /// </summary>
    [Serializable]
    public struct KartInstantBoostState
    {
        public float OpportunityTimer;
        public float ActiveTimer;
        public uint ActivationCount;
        public bool Active;
    }

    /// <summary>The 3000 ms item boost.</summary>
    [Serializable]
    public struct KartTimedBoostState
    {
        public uint RemainingMs;
        public bool Active;
    }

    public static partial class KartDynamics
    {
        /// <summary>Window length, and how long an activation lasts.</summary>
        public const float InstantBoostWindowSeconds = 0.5f;

        /// <summary>The multiplier applied to the forward drive force.</summary>
        public const float BoostForceMultiplier = 1.5f;

        /// <summary>
        /// 0x0042eda0: the 0x2e0 opportunity window is clamped at zero, and 0x2d4
        /// remains active while the 0x2d8 timer is positive.
        /// </summary>
        public static void InstantBoostStepTimers(ref KartInstantBoostState state, float dt)
        {
            if (state.OpportunityTimer > 0f)
            {
                state.OpportunityTimer = MathF.Max(state.OpportunityTimer - dt, 0f);
            }

            if (state.Active)
            {
                state.ActiveTimer -= dt;
                if (state.ActiveTimer <= 0f)
                {
                    state.ActiveTimer = 0f;
                    state.Active = false;
                }
            }
        }

        /// <summary>GoKart::SetAccel(true), 0x00431960.</summary>
        public static void InstantBoostPressForward(ref KartInstantBoostState state)
        {
            if (state.OpportunityTimer <= 0f) return;

            state.OpportunityTimer = 0f;
            state.ActiveTimer = InstantBoostWindowSeconds;
            state.Active = true;
            state.ActivationCount += 1;
        }

        /// <summary>
        /// Tail of 0x0042fc40. A forward drift ending without remaining manual or
        /// automatic slip opens the one-shot accelerator-input window.
        /// </summary>
        public static void InstantBoostUpdateDriftExit(
            ref KartInstantBoostState boost,
            ref KartDriftState drift,
            bool wasDrifting)
        {
            if (!drift.EntryWasForward || !wasDrifting ||
                drift.InputActive || drift.SlipDetected)
            {
                return;
            }

            drift.EntryWasForward = false;
            if (boost.OpportunityTimer == 0f)
            {
                boost.OpportunityTimer = InstantBoostWindowSeconds;
            }
        }

        /// <summary>GoKart::StartTimedBoost at 0x00431ab0.</summary>
        public static bool TimedBoostStart(
            ref KartTimedBoostState state,
            float forwardInput,
            uint durationMs)
        {
            if (forwardInput != 0f)
            {
                state.RemainingMs = durationMs;
                state.Active = true;
            }
            return state.Active;
        }

        /// <summary>Tail of 0x0042e750: subtract min(remaining, frame ms).</summary>
        public static void TimedBoostStepMilliseconds(
            ref KartTimedBoostState state,
            uint elapsedMs)
        {
            if (state.RemainingMs == 0u) return;

            uint consumed = state.RemainingMs < elapsedMs ? state.RemainingMs : elapsedMs;
            state.RemainingMs -= consumed;
            if (state.RemainingMs == 0u) state.Active = false;
        }

        /// <summary>GoKart::IsBoosting at 0x00431b00.</summary>
        public static bool AnyBoostActive(
            in KartTimedBoostState timed,
            in KartInstantBoostState instant)
            => timed.Active || instant.Active;
    }
}
