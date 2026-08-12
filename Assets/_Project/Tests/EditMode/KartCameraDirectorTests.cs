using NUnit.Framework;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// Which cameraman is installed, and what happens to the slots that are still
    /// empty.
    ///
    /// The fallback is the part that matters right now: the ready sweep and the
    /// surround orbit are not recovered yet, and until they are, every phase has to
    /// end up on the chase camera — otherwise installing the director would have
    /// blanked the view for two of the four phases.
    /// </summary>
    public sealed class KartCameraDirectorTests
    {
        /// <summary>A cameraman with no camera, which is all the director needs.</summary>
        private sealed class FakeCameraman : IKartCameraman
        {
            public int Activations;
            public int Deactivations;
            public int Steps;
            public bool Live;

            public UnityEngine.Camera Camera => null;

            public void Activate(KartSimulationState kart)
            {
                ++Activations;
                Live = true;
            }

            public void Deactivate()
            {
                ++Deactivations;
                Live = false;
            }

            public void Step(KartSimulationState kart, uint elapsedMs) => ++Steps;
        }

        [Test]
        public void PhasesAskForTheOriginalsCameramen()
        {
            Assert.That(KartCameraDirector.SlotFor(KartRacePhase.Ready),
                Is.EqualTo(KartCameraSlot.Ready));
            Assert.That(KartCameraDirector.SlotFor(KartRacePhase.Countdown),
                Is.EqualTo(KartCameraSlot.Chase));
            Assert.That(KartCameraDirector.SlotFor(KartRacePhase.Running),
                Is.EqualTo(KartCameraSlot.Chase));
            Assert.That(KartCameraDirector.SlotFor(KartRacePhase.Finished),
                Is.EqualTo(KartCameraSlot.Surround));
        }

        [Test]
        public void EmptySlotsFallBackToTheChaseCamera()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);

            foreach (KartRacePhase phase in new[]
                     {
                         KartRacePhase.Ready, KartRacePhase.Countdown,
                         KartRacePhase.Running, KartRacePhase.Finished,
                     })
            {
                director.Select(KartCameraDirector.SlotFor(phase), kart: null);

                Assert.That(director.InstalledSlot, Is.EqualTo(KartCameraSlot.Chase), $"{phase}");
                Assert.That(director.Active, Is.SameAs(chase), $"{phase}");
                Assert.That(chase.Live, Is.True, $"{phase}");
            }

            Assert.That(chase.Deactivations, Is.Zero,
                "Falling back to the same rig must not put it away and take it out again.");
        }

        [Test]
        public void SelectingAFilledSlotSwapsTheCameraman()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            var surround = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);
            director.Install(KartCameraSlot.Surround, surround);

            director.Select(KartCameraSlot.Chase, kart: null);
            Assert.That(chase.Live, Is.True);
            Assert.That(surround.Live, Is.False);

            director.Select(KartCameraSlot.Surround, kart: null);
            Assert.That(director.Active, Is.SameAs(surround));
            Assert.That(surround.Live, Is.True);
            Assert.That(chase.Live, Is.False, "Only one cameraman is installed at a time.");
        }

        [Test]
        public void SelectingTheInstalledSlotAgainDoesNothing()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);

            director.Select(KartCameraSlot.Chase, kart: null);
            int activations = chase.Activations;

            for (int frame = 0; frame < 10; ++frame) director.Select(KartCameraSlot.Chase, kart: null);

            Assert.That(chase.Activations, Is.EqualTo(activations),
                "The slot is selected every frame, so re-selecting has to be free.");
        }

        [Test]
        public void TopDownOverridesThePhasesCameraman()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            var topDown = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);
            director.Install(KartCameraSlot.TopDown, topDown);

            director.Select(KartCameraSlot.TopDown, kart: null);
            Assert.That(topDown.Live, Is.True);
            Assert.That(chase.Live, Is.False);

            director.Select(KartCameraDirector.SlotFor(KartRacePhase.Running), kart: null);
            Assert.That(chase.Live, Is.True);
            Assert.That(topDown.Live, Is.False);
        }

        [Test]
        public void OnlyTheInstalledCameramanIsStepped()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            var topDown = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);
            director.Install(KartCameraSlot.TopDown, topDown);

            director.Select(KartCameraSlot.Chase, kart: null);
            director.Step(kart: null, elapsedMs: 16u);

            Assert.That(chase.Steps, Is.EqualTo(1));
            Assert.That(topDown.Steps, Is.Zero);
        }

        [Test]
        public void InstallingIntoTheSelectedSlotPutsTheFallbackAway()
        {
            var director = new KartCameraDirector();
            var chase = new FakeCameraman();
            director.Install(KartCameraSlot.Chase, chase);
            director.Select(KartCameraSlot.Surround, kart: null);
            Assert.That(chase.Live, Is.True, "Nothing in the slot yet, so the chase stands in.");

            var surround = new FakeCameraman();
            director.Install(KartCameraSlot.Surround, surround);

            Assert.That(director.Active, Is.SameAs(surround),
                "A cameraman arriving into the selected slot takes it over.");
            Assert.That(chase.Live, Is.False);
        }
    }
}
