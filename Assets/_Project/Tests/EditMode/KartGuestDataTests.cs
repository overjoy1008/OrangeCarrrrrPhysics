using System.Collections.Generic;
using NUnit.Framework;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The karts that are nobody's.
    ///
    /// What is worth pinning here is the boundary rather than the cat: a guest
    /// must stay out of the recovered table, and must not need a gallery cell to
    /// get a name and an engine — those are exactly the two things every
    /// recovered kart derives from the grid, and a guest has no row in it.
    /// </summary>
    public sealed class KartGuestDataTests
    {
        [Test]
        public void NoTwoGuestsShareAnAssetName()
        {
            var seen = new HashSet<string>();
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                Assert.That(guest.AssetName, Is.Not.Empty);

                // They become spec assets in one directory, so a duplicate would
                // have the second silently overwrite the first.
                Assert.That(seen.Add(guest.AssetName), Is.True, guest.AssetName);
            }
        }

        [Test]
        public void NoGuestIsInTheRecoveredTable()
        {
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                Assert.That(
                    KartDemoData.FindKart(guest.AssetName), Is.Null,
                    $"{guest.AssetName} is in KARTS[], where only recovered karts belong");
                Assert.That(
                    KartGalleryLayout.RowOf(guest.AssetName), Is.EqualTo(-1),
                    $"{guest.AssetName} claims a gallery cell");
            }
        }

        [Test]
        public void EveryGuestIsNamedAndNamedOnlyOnce()
        {
            var seen = new HashSet<string>();

            foreach (KartSpec kart in KartDemoData.Karts) seen.Add(KartDisplayName.For(kart.AssetName));

            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                string name = KartDisplayName.For(guest.AssetName);

                Assert.That(name, Is.Not.Empty, guest.AssetName);
                Assert.That(
                    name, Is.Not.EqualTo(guest.AssetName),
                    $"{guest.AssetName} fell through to its asset name");
                Assert.That(seen.Add(name), Is.True, $"'{name}' is already a kart");
            }
        }

        [Test]
        public void AGuestTakesTheEngineItAsksFor()
        {
            // Row lookup would give it Classic, so this only passes if the guest
            // table is consulted first.
            Assert.AreEqual(KartEnginePreset.Mew, KartEnginePreset.For(KartGuestData.Maxwell));

            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                Assert.AreEqual(
                    guest.EnginePreset, KartEnginePreset.For(guest.AssetName), guest.AssetName);
            }

            // And a name that is neither recovered nor a guest still falls back.
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("something_new"));
        }

        [Test]
        public void TheGuestEngineReachesNoRecoveredKart()
        {
            // Mew is a cat purring. It is in the preset list because the guests
            // needed a set of their own, and the whole point of giving them one
            // was that the 9th generation should not inherit it.
            foreach (KartSpec kart in KartDemoData.Karts)
            {
                Assert.AreNotEqual(
                    KartEnginePreset.Mew, KartEnginePreset.For(kart.AssetName), kart.AssetName);
            }
        }

        [Test]
        public void AStatedRotationIsThreeNumbers()
        {
            // The importer takes it whole and hands it to Quaternion.Euler, so a
            // short array would be silently ignored and the model would come out
            // however it happened to arrive.
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                if (guest.ModelRotationDeg == null) continue;
                Assert.That(guest.ModelRotationDeg.Length, Is.EqualTo(3), guest.AssetName);
            }
        }

        [Test]
        public void ASpinningGuestRampsRatherThanSnaps()
        {
            // A zero ramp puts the model into its second shape inside one frame,
            // which reads as the kart being swapped rather than as an effect.
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                if (!guest.Spins) continue;
                Assert.That(guest.SpinRampSeconds, Is.GreaterThan(0f), guest.AssetName);
            }
        }

        [Test]
        public void TheOiiaCatSpinsOnItsSecondShape()
        {
            KartGuestSpec oiia = KartGuestData.Find(KartGuestData.Oiia);

            Assert.That(oiia, Is.Not.Null);
            Assert.That(oiia.Spins, Is.True);
            Assert.That(oiia.SpinBlendShape, Is.Not.Empty, "the morph is what the two modes are");
            Assert.That(oiia.SpinDegreesPerSecond, Is.Not.EqualTo(0f));
            Assert.That(oiia.BoosterSound, Is.Not.Empty, "it brought its own booster");

            // Maxwell has one shape and must not have picked any of this up.
            Assert.That(KartGuestData.Find(KartGuestData.Maxwell).Spins, Is.False);
        }

        [Test]
        public void EveryGuestStartsOnAStopTheLeanKeyCanReach()
        {
            // The L key enters the ladder at the stop nearest the kart's own
            // value. A guest whose value sat between two stops would jump the
            // moment the key was first pressed, which reads as the key doing
            // something it did not do.
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                if (guest.DriftLean == 0f) continue;

                Assert.That(
                    SimulatorRoot.DriftLeanSteps, Has.Some.EqualTo(guest.DriftLean),
                    $"{guest.AssetName} starts at {guest.DriftLean}, which is not a stop");
            }
        }

        [Test]
        public void TheLeanLadderIsSymmetricAndNeverZero()
        {
            float[] steps = SimulatorRoot.DriftLeanSteps;

            Assert.That(steps.Length % 2, Is.EqualTo(0), "the same stops each way");

            for (int i = 0; i < steps.Length; ++i)
            {
                Assert.That(steps[i], Is.Not.EqualTo(0f), "a zero stop is a kart that never leans");

                // Mirrored end to end, so walking off one end arrives at the same
                // magnitude the other way round.
                Assert.AreEqual(-steps[i], steps[steps.Length - 1 - i], 1e-6f);
            }
        }

        [Test]
        public void EveryGuestHasSomethingToDrawItWith()
        {
            // A guest with neither is a magenta kart: the builder makes no
            // material for it and the model keeps whatever its import produced.
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                bool textured = !string.IsNullOrEmpty(guest.BodyTexture);
                bool coloured = !string.IsNullOrEmpty(guest.BodyColorHtml);

                Assert.That(textured || coloured, Is.True, guest.AssetName);
            }
        }

        [Test]
        public void ASecondBoosterTakeComesWithASpeedToTurnAtAndAClipToPlay()
        {
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                if (guest.BoosterSoundSlowStart <= 0f) continue;

                Assert.That(
                    guest.BoosterSound, Is.Not.Empty,
                    $"{guest.AssetName} names a slow take with no clip to take it from");
                Assert.That(
                    guest.BoosterSoundSlowStart, Is.GreaterThan(guest.BoosterSoundStart),
                    $"{guest.AssetName}: the second take is later in the file than the first");

                // Zero would stop the spin dead while the slow take sang on.
                if (!guest.Spins) continue;
                Assert.That(
                    guest.SlowSpinScale, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f),
                    guest.AssetName);
            }
        }

        [Test]
        public void EveryGuestIsSizedAgainstALineThatExists()
        {
            foreach (KartGuestSpec guest in KartGuestData.Guests)
            {
                // A guest fitted by height names no line at all.
                if (guest.SizeHeightMeters > 0f)
                {
                    Assert.That(
                        guest.SizeHeightMeters, Is.InRange(0.5f, 4.0f),
                        $"{guest.AssetName} stands {guest.SizeHeightMeters} m");
                    continue;
                }

                float length = KartGuestData.ReferenceLength(guest.SizeReference);

                Assert.That(
                    length, Is.GreaterThan(0f),
                    $"{guest.AssetName} is sized against '{guest.SizeReference}', which no kart matches");

                // Every kart in the table is between two and two and a half
                // metres long, so a reference outside that is a typo rather than
                // a choice.
                Assert.That(length, Is.InRange(1.5f, 3.0f), guest.SizeReference);
            }
        }

        [Test]
        public void TheParagonLineIsWhatMaxwellIsMeasuredAgainst()
        {
            KartGuestSpec maxwell = KartGuestData.Find(KartGuestData.Maxwell);
            Assert.That(maxwell, Is.Not.Null);

            float total = 0f;
            int count = 0;
            foreach (KartSpec kart in KartDemoData.Karts)
            {
                if (!kart.AssetName.StartsWith("paragon")) continue;
                total += kart.Geometry.HalfLength * 2f;
                ++count;
            }

            Assert.That(count, Is.EqualTo(6), "the six paragons");
            Assert.AreEqual(
                total / count, KartGuestData.ReferenceLength(maxwell.SizeReference), 1e-5f);
        }
    }
}
