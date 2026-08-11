using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// Behaviour of the whole stepper on the flat reference plane: the pieces
    /// above are pinned individually, these check that wiring them together in
    /// the recovered order still produces a kart that rests, drives, drifts and
    /// boosts.
    /// </summary>
    public sealed class KartSimulationTests
    {
        private static KartSimulationState CreateCotten5(out KartFlatGround ground)
        {
            KartSpec spec = KartDemoData.Cotten5;
            var state = new KartSimulationState();
            KartSimulation.Init(state, spec.Dynamics, spec.Geometry);
            ground = new KartFlatGround(0f);
            return state;
        }

        private static void Run(
            KartSimulationState state,
            KartFlatGround ground,
            in KartSimulationControls controls,
            int frames,
            uint msPerFrame = 16u)
        {
            for (int i = 0; i < frames; ++i)
            {
                KartSimulation.SimulateMilliseconds(state, controls, ground, msPerFrame);
            }
        }

        [Test]
        public void AtRestOnTheFlatPlane_TheKartSettlesAndStays()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);

            Run(state, ground, default, frames: 120);

            Assert.That(state.Grounded, Is.True, "all four wheels should be on the plane");
            Assert.That(state.LinearVelocity.Magnitude, Is.LessThan(0.05f),
                "a parked kart must not drift");
            Assert.That(state.Position.Z, Is.GreaterThan(-0.5f).And.LessThan(0.5f),
                "it must neither sink through the plane nor float");

            for (int wheel = 0; wheel < KartConstants.WheelCount; ++wheel)
            {
                Assert.That(state.Wheels[wheel], Is.GreaterThan(0f),
                    $"wheel {wheel} should carry load");
            }
        }

        [Test]
        public void HoldingTheAccelerator_MovesTheKartForward()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            Run(state, ground, default, frames: 60);

            var throttle = new KartSimulationControls { ForwardInput = 1f };
            Run(state, ground, throttle, frames: 120);

            state.GetBodyAxes(out _, out KartVec3 forward, out _);
            float forwardSpeed = KartVec3.Dot(state.LinearVelocity, forward);

            Assert.That(forwardSpeed, Is.GreaterThan(5f), "two seconds of throttle should build speed");
            Assert.That(KartVec3.Dot(state.Position, forward), Is.GreaterThan(0f),
                "and it should have travelled the way it points");
        }

        /// <summary>
        /// Drag is quadratic on the ground, so speed converges instead of growing
        /// without bound. A boost raises the terminal speed because it multiplies
        /// the drive force by 1.5.
        /// </summary>
        [Test]
        public void TopSpeed_IsBoundedByDrag()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            var throttle = new KartSimulationControls { ForwardInput = 1f };

            Run(state, ground, throttle, frames: 600);
            float settled = state.LinearVelocity.Magnitude;

            Run(state, ground, throttle, frames: 120);
            float later = state.LinearVelocity.Magnitude;

            Assert.That(later, Is.EqualTo(settled).Within(0.5f), "speed should have converged");
            Assert.That(settled, Is.GreaterThan(10f).And.LessThan(200f));
        }

        [Test]
        public void ItemBoost_RunsForThreeSecondsAndEndsOnRelease()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            Run(state, ground, new KartSimulationControls { ForwardInput = 1f }, frames: 60);

            var boosting = new KartSimulationControls { ForwardInput = 1f, BoostActive = true };
            KartSimulation.SimulateMilliseconds(state, boosting, ground, 16u);

            Assert.That(state.TimedBoost.Active, Is.True);
            Assert.That(state.TimedBoost.RemainingMs, Is.EqualTo(KartConstants.ItemBoostDurationMs - 16u));

            // A held key must not retrigger, so after the full duration it is over.
            Run(state, ground, boosting, frames: 200);
            Assert.That(state.TimedBoost.Active, Is.False, "3000 ms should have run out");

            // Restart, then release the throttle: the cutoff kills it immediately.
            state.PreviousBoostInput = false;
            KartSimulation.SimulateMilliseconds(state, boosting, ground, 16u);
            Assert.That(state.TimedBoost.Active, Is.True);

            KartSimulation.SimulateMilliseconds(
                state, new KartSimulationControls { BoostActive = true }, ground, 16u);
            Assert.That(state.TimedBoost.Active, Is.False, "releasing the throttle ends the boost");
        }

        [Test]
        public void ItemBoost_DoesNotStartWithoutThrottle()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            Run(state, ground, default, frames: 60);

            KartSimulation.SimulateMilliseconds(
                state, new KartSimulationControls { BoostActive = true }, ground, 16u);

            Assert.That(state.TimedBoost.Active, Is.False);
        }

        /// <summary>
        /// Finishing a forward drift opens a half-second window; pressing the
        /// accelerator inside it fires the instant boost for another half second.
        /// </summary>
        [Test]
        public void InstantBoost_OpensAWindowWhenAForwardDriftEnds()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);

            var throttle = new KartSimulationControls { ForwardInput = 1f };
            Run(state, ground, throttle, frames: 180);

            var drifting = new KartSimulationControls
            {
                ForwardInput = 1f,
                SteeringInput = 1f,
                DriftInput = true,
            };
            Run(state, ground, drifting, frames: 40);

            Assert.That(state.Drift.EntryWasForward, Is.True, "the drift was entered going forward");

            // Release drift and throttle so the exit is detected with no residual
            // manual or automatic slip.
            Run(state, ground, default, frames: 30);

            Assert.That(
                state.InstantBoost.OpportunityTimer, Is.GreaterThan(0f),
                "ending a forward drift should open the instant-boost window");

            // Pressing the accelerator inside the window spends it.
            KartSimulation.SimulateMilliseconds(state, throttle, ground, 16u);

            Assert.That(state.InstantBoost.Active, Is.True);
            Assert.That(state.InstantBoost.ActivationCount, Is.EqualTo(1u));
            Assert.That(state.InstantBoost.OpportunityTimer, Is.EqualTo(0f));
        }

        [Test]
        public void InstantBoost_ExpiresAfterHalfASecond()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            state.InstantBoost.Active = true;
            state.InstantBoost.ActiveTimer = KartDynamics.InstantBoostWindowSeconds;

            var throttle = new KartSimulationControls { ForwardInput = 1f };
            Run(state, ground, throttle, frames: 20);
            Assert.That(state.InstantBoost.Active, Is.True, "0.32 s in, it should still run");

            Run(state, ground, throttle, frames: 20);
            Assert.That(state.InstantBoost.Active, Is.False, "0.64 s in, it should be over");
        }

        [Test]
        public void SteeringWhileMoving_YawsTheKart()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            Run(state, ground, new KartSimulationControls { ForwardInput = 1f }, frames: 180);

            float yawBefore = state.Orientation.Z;
            Run(state, ground,
                new KartSimulationControls { ForwardInput = 1f, SteeringInput = 1f },
                frames: 60);

            Assert.That(state.Orientation.Z, Is.Not.EqualTo(yawBefore).Within(0.001f),
                "a second of steering should have turned it");
            Assert.That(state.Orientation.UpZ, Is.GreaterThan(0.5f),
                "and the tilt clamp should have kept it upright");
        }

        /// <summary>
        /// The linger timer is what stops a held drift key from re-arming the
        /// trigger every substep.
        /// </summary>
        [Test]
        public void DriftTrigger_ArmsOnceAndThenRunsDown()
        {
            KartSimulationState state = CreateCotten5(out KartFlatGround ground);
            Run(state, ground, new KartSimulationControls { ForwardInput = 1f }, frames: 180);

            var drifting = new KartSimulationControls
            {
                ForwardInput = 1f,
                SteeringInput = 1f,
                DriftInput = true,
            };

            KartSimulation.SimulateMilliseconds(state, drifting, ground, 16u);
            Assert.That(state.Drift.InputActive, Is.True);

            Run(state, ground, drifting, frames: 60);
            Assert.That(state.Drift.TriggerActive, Is.False,
                "the trigger is a brief kick, not a sustained state");
            Assert.That(state.Drift.InputActive, Is.True, "but the drift itself is still held");
        }

        [Test]
        public void DragTrigger_SlowsTheKart()
        {
            KartSimulationState fast = CreateCotten5(out KartFlatGround ground);
            var throttle = new KartSimulationControls { ForwardInput = 1f };
            Run(fast, ground, throttle, frames: 400);

            KartSimulationState slow = CreateCotten5(out KartFlatGround slowGround);
            KartSimulation.MultiplyGroundedDragScale(slow, 4f);
            Run(slow, slowGround, throttle, frames: 400);

            Assert.That(slow.LinearVelocity.Magnitude, Is.LessThan(fast.LinearVelocity.Magnitude),
                "four times the ground drag must cap the kart lower");
        }
    }
}
