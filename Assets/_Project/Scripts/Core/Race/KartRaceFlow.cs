namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Whether the race ends the way the original's time challenge ends, or never
    /// ends at all.
    ///
    /// <see cref="Race"/> is the recovered rule: the last gate closes the race, the
    /// kart stops answering the wheel, the result panel comes up and the stage is
    /// left. <see cref="Free"/> is the port's own bench mode — the lap counter keeps
    /// counting and nothing is ever taken away from the driver, which is what the
    /// parameter window and the kart and track cycles are for.
    ///
    /// The flag exists because the two cannot both be true: a bench that ends after
    /// three laps is not a bench.
    /// </summary>
    public enum KartRaceMode
    {
        /// <summary>The original's rule. The final gate finishes the race.</summary>
        Race = 0,

        /// <summary>The port's bench. A finish is recorded but changes nothing.</summary>
        Free = 1,
    }

    /// <summary>
    /// Where a race is in its run, in the order the original walks them.
    ///
    /// The first two are one 7000 ms countdown cut in two at its <c>count_3</c>
    /// cue, because that is where the original swaps the camera: the opening
    /// stretch belongs to <c>KartReCameraman</c> and everything after it to
    /// <c>ChaseCameraman</c>.
    /// </summary>
    public enum KartRacePhase
    {
        /// <summary>Armed to <c>count_3</c>. The original's <c>readyCamera</c> plays here.</summary>
        Ready = 0,

        /// <summary>3, 2, 1. The chase camera is live from the first digit.</summary>
        Countdown = 1,

        /// <summary>Released at GO and driving.</summary>
        Running = 2,

        /// <summary>Past the final gate. The surround camera has the kart.</summary>
        Finished = 3,
    }

    /// <summary>
    /// One update's worth of transitions. Every flag but <see cref="DriveHeld"/> is
    /// an edge, true only on the update that crossed it, so a caller can hang a
    /// camera swap or a sound off it without keeping its own memory of the phase.
    /// </summary>
    public struct KartRaceFlowCues
    {
        /// <summary>The countdown's own cues, unchanged.</summary>
        public KartCountdownCues Countdown;

        /// <summary><c>count_3</c>: install the chase camera.</summary>
        public bool EnteredCountdown;

        /// <summary>GO: the kart is released.</summary>
        public bool EnteredRunning;

        /// <summary>Three seconds after the finish: the time and best-lap panel.</summary>
        public bool ShowResults;

        /// <summary>Eight seconds after the finish: leave the stage.</summary>
        public bool ExitDue;

        /// <summary>
        /// True while the drive inputs are to be ignored — before GO and after the
        /// finish. Not an edge: it is read every update.
        /// </summary>
        public bool DriveHeld;
    }

    /// <summary>
    /// The race's state machine, from the grid to the result panel.
    ///
    /// The two delays after the finish are the original time challenge's:
    /// <c>finish</c> runs, the kart is put into its finished state and the surround
    /// camera is installed at once; the time and best-lap panel comes up three
    /// seconds later; the stage is left eight seconds after the finish.
    ///
    /// The countdown itself is not re-implemented here — <see cref="KartCountdown"/>
    /// is the recovered 7000 ms machine and this only reads its cues, so the start
    /// boost window and the 3 / 2 / 1 / GO thresholds stay in one place.
    /// </summary>
    public sealed class KartRaceFlow
    {
        /// <summary>Finish to result panel.</summary>
        public const uint ResultsDelayMs = 3000u;

        /// <summary>Finish to leaving the stage.</summary>
        public const uint ExitDelayMs = 8000u;

        /// <summary>
        /// Which rule set is in force. Changing it mid-race is allowed and only
        /// decides whether the next finish is acted on; a race already finished
        /// stays finished until the next <see cref="Start"/>.
        /// </summary>
        public KartRaceMode Mode = KartRaceMode.Race;

        public KartRacePhase Phase { get; private set; } = KartRacePhase.Ready;

        /// <summary>The recovered start countdown, armed by <see cref="Start"/>.</summary>
        public KartCountdown Countdown;

        /// <summary>When the final gate was crossed, or zero while it has not been.</summary>
        public uint FinishMs { get; private set; }

        /// <summary>
        /// Where the kart came in, counting from one.
        ///
        /// There is only ever one kart in this port, so it is always first; the
        /// value is carried rather than assumed so the result reads WINNER or
        /// FINISH off the race instead of off a constant in the HUD.
        /// </summary>
        public uint FinishPlace { get; private set; } = 1u;

        /// <summary>
        /// The finishing time, measured from GO rather than from the grid.
        ///
        /// The original stamps it as <c>now - raceStart</c> at <c>0x00456F...</c>'s
        /// finish branch, and the countdown's deadline is that start: it is the
        /// moment the karts are released.
        /// </summary>
        public uint FinishTimeMs =>
            FinishMs > Countdown.DeadlineMs ? FinishMs - Countdown.DeadlineMs : 0u;

        private bool _resultsShown;
        private bool _exitRaised;

        /// <summary>True once the result panel is up, and for as long as it is.</summary>
        public bool ResultsVisible => _resultsShown;

        public bool Finished => Phase == KartRacePhase.Finished;

        /// <summary>
        /// True while the drive inputs are ignored. The original releases every kart
        /// on GO and takes the wheel away again at the finish.
        /// </summary>
        public bool DriveHeld => Phase != KartRacePhase.Running;

        /// <summary>Puts the race back on the grid and arms the countdown.</summary>
        public void Start(uint nowMs)
        {
            Phase = KartRacePhase.Ready;
            Countdown = default;
            Countdown.Start(nowMs);
            FinishMs = 0u;
            FinishPlace = 1u;
            _resultsShown = false;
            _exitRaised = false;
        }

        /// <summary>
        /// Advances the countdown and the post-finish delays. Call once per frame
        /// with the race clock; the crossing of the final gate comes in separately
        /// through <see cref="Finish"/>, since only the course knows about it.
        /// </summary>
        public KartRaceFlowCues Update(uint nowMs)
        {
            var cues = new KartRaceFlowCues();

            if (Phase == KartRacePhase.Finished)
            {
                uint since = nowMs - FinishMs;

                if (!_resultsShown && since >= ResultsDelayMs)
                {
                    _resultsShown = true;
                    cues.ShowResults = true;
                }

                if (!_exitRaised && since >= ExitDelayMs)
                {
                    _exitRaised = true;
                    cues.ExitDue = true;
                }

                // The countdown is long done, and a finished race is released in
                // the countdown's sense — it is this phase that holds the wheel.
                cues.Countdown.Released = true;
                cues.DriveHeld = true;
                return cues;
            }

            cues.Countdown = Countdown.Update(nowMs);

            if (Phase == KartRacePhase.Ready && cues.Countdown.PlayThree)
            {
                Phase = KartRacePhase.Countdown;
                cues.EnteredCountdown = true;
            }

            if (Phase != KartRacePhase.Running && cues.Countdown.Released)
            {
                Phase = KartRacePhase.Running;
                cues.EnteredRunning = true;
            }

            cues.DriveHeld = DriveHeld;
            return cues;
        }

        /// <summary>
        /// The final gate has been crossed. Ignored unless a race is actually
        /// running under the original's rule, so a bench run and a second crossing
        /// of the same gate both do nothing.
        /// </summary>
        /// <returns>True when this call ended the race.</returns>
        public bool Finish(uint nowMs, uint place = 1u)
        {
            if (Mode != KartRaceMode.Race || Phase != KartRacePhase.Running) return false;

            Phase = KartRacePhase.Finished;
            FinishMs = nowMs;
            FinishPlace = place < 1u ? 1u : place;
            return true;
        }
    }
}
