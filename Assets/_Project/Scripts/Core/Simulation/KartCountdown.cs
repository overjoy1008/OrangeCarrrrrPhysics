namespace OrangeCarrrrr.Core
{
    public enum KartCountdownStage
    {
        Idle = 0,
        Three = 1,
        Two = 2,
        One = 3,
        Running = 4,
    }

    /// <summary>Cues raised by a single update, each true only on the crossing update.</summary>
    public struct KartCountdownCues
    {
        public bool PlayThree;
        public bool PlayTwo;
        public bool PlayOne;
        public bool PlayGo;

        /// <summary>False until GO; the kart is held at the line while it is false.</summary>
        public bool Released;

        /// <summary>Milliseconds until GO, zero once running.</summary>
        public uint RemainingMs;
    }

    /// <summary>
    /// Race start countdown recovered from the original demo, ported from
    /// <c>kart_countdown.c</c>.
    ///
    /// FUN_00451EB0 and FUN_00456CD0 are the same state machine at two different
    /// field offsets. Arming sets <c>deadline = now + 7000</c> and the per-frame
    /// body counts down against it, firing 3 / 2 / 1 / GO as the deadline comes
    /// within 3000 / 2000 / 1000 / 0 ms.
    ///
    /// Pressing the accelerator within 100 ms either side of the deadline grants
    /// a 1000 ms timed boost — the same call an item boost makes, so the start
    /// boost drives the booster sound and the camera's wide field of view
    /// identically.
    /// </summary>
    public struct KartCountdown
    {
        public const uint TotalMs = 7000u;
        public const uint StartCueMs = 3000u;

        /// <summary>Half-width of the window around the deadline that grants the boost.</summary>
        public const uint StartBoostWindowMs = 100u;

        public const uint StartBoostDurationMs = 1000u;

        /// <summary>Absolute time of the GO moment.</summary>
        public uint DeadlineMs;

        public KartCountdownStage Stage;
        public bool Armed;
        public bool Released;

        /// <summary>Arms the countdown: the deadline becomes now + 7000 ms.</summary>
        public void Start(uint nowMs)
        {
            DeadlineMs = nowMs + TotalMs;
            Stage = KartCountdownStage.Idle;
            Armed = true;
            Released = false;
        }

        public KartCountdownCues Update(uint nowMs)
        {
            var cues = new KartCountdownCues();
            if (!Armed)
            {
                cues.Released = true;
                return cues;
            }

            if (nowMs < DeadlineMs)
            {
                cues.RemainingMs = DeadlineMs - nowMs;

                // Each threshold is written as the original tests it: the deadline
                // compared against now plus the offset, so only one stage advances
                // per update even if several are already due.
                if (Stage == KartCountdownStage.Idle && DeadlineMs <= nowMs + 3000u)
                {
                    Stage = KartCountdownStage.Three;
                    cues.PlayThree = true;
                }
                else if (Stage == KartCountdownStage.Three && DeadlineMs <= nowMs + 2000u)
                {
                    Stage = KartCountdownStage.Two;
                    cues.PlayTwo = true;
                }
                else if (Stage == KartCountdownStage.Two && DeadlineMs <= nowMs + 1000u)
                {
                    Stage = KartCountdownStage.One;
                    cues.PlayOne = true;
                }
            }
            else if (!Released)
            {
                Stage = KartCountdownStage.Running;
                Released = true;
                cues.PlayGo = true;
            }

            cues.Released = Released;
            return cues;
        }

        /// <summary>
        /// True when a forward press at <paramref name="pressMs"/> falls inside
        /// the window around the deadline. The original compares both bounds
        /// against the same deadline, so the window is symmetric and inclusive.
        /// </summary>
        public bool StartBoostGranted(uint pressMs)
        {
            if (!Armed || DeadlineMs == 0u) return false;
            return pressMs <= DeadlineMs + StartBoostWindowMs &&
                   DeadlineMs - StartBoostWindowMs <= pressMs;
        }
    }
}
