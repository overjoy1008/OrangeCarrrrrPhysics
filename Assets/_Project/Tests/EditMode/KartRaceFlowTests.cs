using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The race's state machine.
    ///
    /// Two things are being checked. The first is that the machine is the
    /// original's: the grid ends at <c>count_3</c> rather than at a timer of its
    /// own, the kart is released at GO, and the finish is followed by the two
    /// delays the time challenge uses. The second is that wrapping the countdown
    /// changed nothing about it — the port's whole start line, including the boost
    /// window, hangs off <see cref="KartCountdown"/> and the flow only reads it.
    /// </summary>
    public sealed class KartRaceFlowTests
    {
        /// <summary>Steps the flow to a time in one update and returns that update's cues.</summary>
        private static KartRaceFlowCues At(KartRaceFlow flow, uint nowMs) => flow.Update(nowMs);

        private static KartRaceFlow Started()
        {
            var flow = new KartRaceFlow();
            flow.Start(0u);
            return flow;
        }

        /// <summary>
        /// Runs the flow forward in 16 ms steps — the original's window timer — and
        /// returns the cues of an update landing exactly on <paramref name="targetMs"/>,
        /// since 16 ms steps from zero do not otherwise land on the deadline.
        /// </summary>
        private static KartRaceFlowCues RunTo(KartRaceFlow flow, uint targetMs)
        {
            for (uint now = 0u; now < targetMs; now += 16u) flow.Update(now);
            return flow.Update(targetMs);
        }

        [Test]
        public void StartsOnTheGrid()
        {
            KartRaceFlow flow = Started();

            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Ready));
            Assert.That(flow.DriveHeld, Is.True, "The kart is held on the grid.");
            Assert.That(flow.Finished, Is.False);
            Assert.That(flow.Countdown.DeadlineMs, Is.EqualTo(KartCountdown.TotalMs));
        }

        [Test]
        public void GridLastsUntilTheFirstCountdownDigit()
        {
            KartRaceFlow flow = Started();

            // One millisecond before count_3 is due the sweep is still running.
            KartRaceFlowCues before = At(flow, KartCountdown.TotalMs - KartCountdown.StartCueMs - 1u);
            Assert.That(before.EnteredCountdown, Is.False);
            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Ready));

            KartRaceFlowCues onCue = At(flow, KartCountdown.TotalMs - KartCountdown.StartCueMs);
            Assert.That(onCue.Countdown.PlayThree, Is.True, "count_3 is what ends the grid.");
            Assert.That(onCue.EnteredCountdown, Is.True);
            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Countdown));
            Assert.That(flow.DriveHeld, Is.True, "3, 2, 1 still holds the kart.");
        }

        [Test]
        public void ReleasesTheKartOnGo()
        {
            KartRaceFlow flow = Started();
            KartRaceFlowCues cues = RunTo(flow, KartCountdown.TotalMs);

            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Running));
            Assert.That(flow.DriveHeld, Is.False);
            Assert.That(cues.DriveHeld, Is.False);
        }

        [Test]
        public void RaisesEveryTransitionExactlyOnce()
        {
            KartRaceFlow flow = Started();

            int countdown = 0;
            int running = 0;
            for (uint now = 0u; now <= KartCountdown.TotalMs + 2000u; now += 16u)
            {
                KartRaceFlowCues cues = flow.Update(now);
                if (cues.EnteredCountdown) ++countdown;
                if (cues.EnteredRunning) ++running;
            }

            Assert.That(countdown, Is.EqualTo(1));
            Assert.That(running, Is.EqualTo(1));
        }

        [Test]
        public void WrappingTheCountdownLeavesItUnchanged()
        {
            KartRaceFlow flow = Started();

            var bare = new KartCountdown();
            bare.Start(0u);

            for (uint now = 0u; now <= KartCountdown.TotalMs + 1000u; now += 16u)
            {
                KartCountdownCues expected = bare.Update(now);
                KartCountdownCues actual = flow.Update(now).Countdown;

                Assert.That(actual.PlayThree, Is.EqualTo(expected.PlayThree), $"3 at {now} ms");
                Assert.That(actual.PlayTwo, Is.EqualTo(expected.PlayTwo), $"2 at {now} ms");
                Assert.That(actual.PlayOne, Is.EqualTo(expected.PlayOne), $"1 at {now} ms");
                Assert.That(actual.PlayGo, Is.EqualTo(expected.PlayGo), $"GO at {now} ms");
                Assert.That(actual.Released, Is.EqualTo(expected.Released), $"release at {now} ms");
                Assert.That(actual.RemainingMs, Is.EqualTo(expected.RemainingMs), $"remaining at {now} ms");
            }

            // The start-boost window is read off the flow's own countdown, so it
            // has to still be the armed one.
            Assert.That(flow.Countdown.StartBoostGranted(KartCountdown.TotalMs), Is.True);
        }

        [Test]
        public void FinishTakesTheWheelAndRunsTheTwoDelays()
        {
            KartRaceFlow flow = Started();
            RunTo(flow, KartCountdown.TotalMs);

            const uint finishMs = 90000u;
            Assert.That(flow.Finish(finishMs), Is.True);
            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Finished));
            Assert.That(flow.FinishMs, Is.EqualTo(finishMs));
            Assert.That(flow.DriveHeld, Is.True, "The finished kart stops answering the wheel.");

            KartRaceFlowCues early = At(flow, finishMs + KartRaceFlow.ResultsDelayMs - 1u);
            Assert.That(early.ShowResults, Is.False);
            Assert.That(flow.ResultsVisible, Is.False);

            KartRaceFlowCues panel = At(flow, finishMs + KartRaceFlow.ResultsDelayMs);
            Assert.That(panel.ShowResults, Is.True);
            Assert.That(flow.ResultsVisible, Is.True);

            KartRaceFlowCues waiting = At(flow, finishMs + KartRaceFlow.ExitDelayMs - 1u);
            Assert.That(waiting.ExitDue, Is.False);
            Assert.That(waiting.ShowResults, Is.False, "The panel is raised once, not held raised.");

            KartRaceFlowCues leave = At(flow, finishMs + KartRaceFlow.ExitDelayMs);
            Assert.That(leave.ExitDue, Is.True);

            Assert.That(At(flow, finishMs + KartRaceFlow.ExitDelayMs + 5000u).ExitDue, Is.False);
        }

        [Test]
        public void FreeModeNeverFinishes()
        {
            KartRaceFlow flow = Started();
            flow.Mode = KartRaceMode.Free;
            RunTo(flow, KartCountdown.TotalMs);

            Assert.That(flow.Finish(90000u), Is.False);
            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Running));
            Assert.That(flow.DriveHeld, Is.False, "The bench never takes the wheel away.");
        }

        [Test]
        public void FinishIsIgnoredOutsideARunningRace()
        {
            KartRaceFlow onTheGrid = Started();
            Assert.That(onTheGrid.Finish(1000u), Is.False, "A race that has not started cannot finish.");

            KartRaceFlow flow = Started();
            RunTo(flow, KartCountdown.TotalMs);
            Assert.That(flow.Finish(90000u), Is.True);
            Assert.That(flow.Finish(91000u), Is.False, "A second crossing changes nothing.");
            Assert.That(flow.FinishMs, Is.EqualTo(90000u));
        }

        [Test]
        public void StartPutsAFinishedRaceBackOnTheGrid()
        {
            KartRaceFlow flow = Started();
            RunTo(flow, KartCountdown.TotalMs);
            flow.Finish(90000u);
            At(flow, 90000u + KartRaceFlow.ExitDelayMs);

            flow.Mode = KartRaceMode.Free;
            flow.Start(0u);

            Assert.That(flow.Phase, Is.EqualTo(KartRacePhase.Ready));
            Assert.That(flow.FinishMs, Is.EqualTo(0u));
            Assert.That(flow.ResultsVisible, Is.False);
            Assert.That(flow.Mode, Is.EqualTo(KartRaceMode.Free), "The mode is a setting, not race state.");

            // The two delays are armed again rather than left spent.
            RunTo(flow, KartCountdown.TotalMs);
            flow.Mode = KartRaceMode.Race;
            Assert.That(flow.Finish(50000u), Is.True);
            Assert.That(At(flow, 50000u + KartRaceFlow.ResultsDelayMs).ShowResults, Is.True);
        }
    }
}
