using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The active-suspension jump, against <c>test_jump_is_force_driven_and_opt_in</c>.
    ///
    /// The bounds are the C test's own. They are wide on purpose — this is not an
    /// oracle comparison, because there is no recovered trajectory to compare
    /// against, so what is pinned is the shape of the behaviour: that the jump is
    /// opt-in, that it is driven by a force rather than written onto the
    /// velocity, that mass and timing both cost height, and that the wheel bias
    /// rolls the body the way it is sliding.
    /// </summary>
    public sealed class KartJumpTests
    {
        private const uint StepMs = 5u;
        private const uint SettleMs = 250u;

        /// <summary>
        /// The recovered efficiency pair, which the C test's bounds are written
        /// against.
        ///
        /// Set here rather than taken from the config because the port's own
        /// default has since been tuned up — see
        /// <see cref="TheProjectJumpsHigherThanTheRecoveredNumbers"/>. These tests
        /// are about whether the ported jump behaves like the C one, so they pin
        /// the C one's inputs.
        /// </summary>
        private const float RecoveredMinEfficiency = 0.2f;
        private const float RecoveredMaxEfficiency = 1.0f;

        private static KartDynamicsConfig Recovered()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();
            config.JumpMinEfficiency = RecoveredMinEfficiency;
            config.JumpMaxEfficiency = RecoveredMaxEfficiency;
            return config;
        }

        private static KartSimulationState CreateDefault(float mass, out KartFlatGround ground)
        {
            KartDynamicsConfig config = Recovered();
            config.Mass = mass;
            return Create(config, out ground);
        }

        private static KartSimulationState Create(
            in KartDynamicsConfig config, out KartFlatGround ground)
        {
            var state = new KartSimulationState();
            KartSimulation.Init(state, config, KartSimulation.DefaultGeometry);
            ground = new KartFlatGround(0f);
            return state;
        }

        private static void Run(
            KartSimulationState state,
            KartFlatGround ground,
            in KartSimulationControls controls,
            uint totalMs)
        {
            for (uint elapsed = 0; elapsed < totalMs; elapsed += StepMs)
            {
                KartSimulation.SimulateMilliseconds(state, controls, ground, StepMs);
            }
        }

        /// <summary>
        /// Settles the kart, holds the jump key for <paramref name="holdMs"/>,
        /// releases it and follows the kart back down. Returns how far above the
        /// takeoff height it got.
        /// </summary>
        private static float JumpHeight(float mass, uint holdMs, out bool sawAirborne)
        {
            KartSimulationState state = CreateDefault(mass, out KartFlatGround ground);
            var controls = new KartSimulationControls();

            Run(state, ground, controls, SettleMs);

            float takeoff = state.Position.Z;
            float peak = takeoff;
            sawAirborne = false;

            controls.JumpInput = true;
            Run(state, ground, controls, holdMs);

            controls.JumpInput = false;
            for (uint elapsed = 0; elapsed < 1200u; elapsed += StepMs)
            {
                KartSimulation.SimulateMilliseconds(state, controls, ground, StepMs);
                if (state.Jump.Phase == KartJumpPhase.Airborne) sawAirborne = true;
                if (state.Position.Z > peak) peak = state.Position.Z;
            }

            // It came back down and stayed on the plane rather than through it.
            Assert.That(state.Position.Z, Is.GreaterThan(-0.5f));
            Assert.That(state.Grounded, Is.True);

            return peak - takeoff;
        }

        /// <summary>
        /// The same jump taken while sliding sideways at 20, with the tyres taken
        /// out of it so the roll that comes back is the jump's own.
        /// </summary>
        private static float LateralRoll(float velocityBias)
        {
            KartDynamicsConfig config = Recovered();
            config.FrontGripFactor = 0f;
            config.RearGripFactor = 0f;
            config.JumpVelocityDirectionBias = velocityBias;
            config.JumpTorqueScale = 0.2f;

            KartSimulationState state = Create(config, out KartFlatGround ground);
            var controls = new KartSimulationControls();

            Run(state, ground, controls, SettleMs);

            controls.JumpInput = true;
            Run(state, ground, controls, 750u);

            state.LinearVelocity.X = 20f;
            controls.JumpInput = false;
            Run(state, ground, controls, 50u);

            return state.AngularVelocity.Y;
        }

        [Test]
        public void AKartThatNeverJumpsStepsExactlyAsItDidBefore()
        {
            // The C test memcmps the whole state. The pose and the velocities are
            // what every other test reads, and any jump force at all would move
            // them, so comparing those is the same claim.
            KartSimulationState untouched = CreateDefault(100f, out KartFlatGround groundA);
            KartSimulationState baseline = CreateDefault(100f, out KartFlatGround groundB);
            var driving = new KartSimulationControls { ForwardInput = 1f };

            Run(untouched, groundA, driving, 1000u);
            Run(baseline, groundB, driving, 1000u);

            Assert.AreEqual(baseline.Position.X, untouched.Position.X);
            Assert.AreEqual(baseline.Position.Y, untouched.Position.Y);
            Assert.AreEqual(baseline.Position.Z, untouched.Position.Z);
            Assert.AreEqual(baseline.LinearVelocity.X, untouched.LinearVelocity.X);
            Assert.AreEqual(baseline.LinearVelocity.Y, untouched.LinearVelocity.Y);
            Assert.AreEqual(baseline.LinearVelocity.Z, untouched.LinearVelocity.Z);
            Assert.AreEqual(KartJumpPhase.Ready, untouched.Jump.Phase);
            Assert.AreEqual(0f, untouched.Jump.AppliedForce);
        }

        [Test]
        public void AWellTimedJumpLeavesTheGroundAndComesBack()
        {
            float height = JumpHeight(100f, 750u, out bool sawAirborne);

            Assert.That(sawAirborne, Is.True, "the kart never reached the airborne phase");
            Assert.That(height, Is.GreaterThan(1.5f).And.LessThan(4.5f), $"height {height}");
        }

        [Test]
        public void AHeavierKartJumpsLowerFromTheSameSpring()
        {
            // The spring stores the same joules either way, and p = sqrt(2mE)
            // buys less height as the mass grows. This is the test that would
            // fail if the jump were an impulse written onto the velocity, which
            // would give both karts the same height.
            float light = JumpHeight(100f, 750u, out bool lightAirborne);
            float heavy = JumpHeight(200f, 750u, out bool heavyAirborne);

            Assert.That(lightAirborne && heavyAirborne, Is.True);
            Assert.That(heavy, Is.GreaterThan(0.1f).And.LessThan(light), $"heavy {heavy}, light {light}");
        }

        [Test]
        public void ReleasingEarlyWastesTheJump()
        {
            float light = JumpHeight(100f, 750u, out _);
            float early = JumpHeight(100f, 150u, out bool earlyAirborne);

            Assert.That(earlyAirborne, Is.True);
            Assert.That(early, Is.LessThan(light * 0.4f), $"early {early}, light {light}");
        }

        [Test]
        public void HoldingPastTheSweepFallsBackToTheMinimum()
        {
            // The gauge sweeps up, back down, then sits at zero. Holding for ever
            // is not a failed jump, it is the kart's minimum efficiency — which is
            // 0.2, not nothing.
            float light = JumpHeight(100f, 750u, out _);
            float overheld = JumpHeight(100f, 1700u, out bool overheldAirborne);

            Assert.That(overheldAirborne, Is.True);
            Assert.That(
                overheld, Is.GreaterThan(0.2f).And.LessThan(light * 0.35f),
                $"overheld {overheld}, light {light}");
        }

        [Test]
        public void TheWheelBiasRollsTheBodyIntoTheSlide()
        {
            // A positive bias loads the wheels on the side the kart is sliding
            // towards, which lifts that side; a negative one lifts the other.
            // The two signs are what says the bias is doing anything at all.
            Assert.That(LateralRoll(0.5f), Is.LessThan(0f));
            Assert.That(LateralRoll(-0.5f), Is.GreaterThan(0f));
        }

        [Test]
        public void TheGaugeSweepsUpThenDownThenSitsAtZero()
        {
            KartSimulationState state = CreateDefault(100f, out KartFlatGround ground);
            var controls = new KartSimulationControls();
            Run(state, ground, controls, SettleMs);

            controls.JumpInput = true;

            // The sweep time is 0.75 s, so the peak is there and the trough one
            // sweep later.
            Run(state, ground, controls, 750u);
            Assert.AreEqual(KartJumpPhase.Crouch, state.Jump.Phase);
            Assert.That(state.Jump.GaugePosition, Is.GreaterThan(0.95f), "at the top");
            Assert.That(
                state.Jump.JumpStrength,
                Is.EqualTo(state.Config.JumpMaxEfficiency).Within(0.05f));

            Run(state, ground, controls, 750u);
            Assert.That(state.Jump.GaugePosition, Is.LessThan(0.05f), "back at the bottom");

            Run(state, ground, controls, 500u);
            Assert.AreEqual(0f, state.Jump.GaugePosition, "flat zero past the sweep");
            Assert.That(
                state.Jump.JumpStrength,
                Is.EqualTo(state.Config.JumpMinEfficiency).Within(1e-5f));
        }

        [Test]
        public void APressWhileAirborneDoesNotStartASecondJump()
        {
            // Holding the key through the landing must not wind the spring again
            // the moment the wheels touch: the crouch takes the press edge.
            KartSimulationState state = CreateDefault(100f, out KartFlatGround ground);
            var controls = new KartSimulationControls();
            Run(state, ground, controls, SettleMs);

            controls.JumpInput = true;
            Run(state, ground, controls, 750u);
            controls.JumpInput = false;
            Run(state, ground, controls, 100u);

            Assert.AreEqual(KartJumpPhase.Airborne, state.Jump.Phase);

            controls.JumpInput = true;
            Run(state, ground, controls, 1500u);

            Assert.That(
                state.Jump.Phase,
                Is.Not.EqualTo(KartJumpPhase.Crouch).And.Not.EqualTo(KartJumpPhase.Push),
                "a held key started a second jump");
        }

        [Test]
        public void TheProjectJumpsHigherThanTheRecoveredNumbers()
        {
            // The tuned pair, which every kart in the project now carries. It is
            // pinned because it is the one place the port deliberately walks away
            // from kart_demo_data.c's tail, and a silent drift back to 0.2/1.0
            // would look like a bug in the gauge rather than a changed constant.
            KartDynamicsConfig shipped = KartDynamicsConfig.Standard();

            Assert.AreEqual(1.0f, shipped.JumpMinEfficiency, 1e-5f);
            Assert.AreEqual(2.5f, shipped.JumpMaxEfficiency, 1e-5f);
            Assert.That(
                shipped.JumpMinEfficiency, Is.GreaterThanOrEqualTo(RecoveredMaxEfficiency),
                "a fluffed jump is now worth what a perfect one used to be");

            // The rest of the tail is still the demo's.
            Assert.AreEqual(0.18f, shipped.JumpMaxCrouchDistance, 1e-5f);
            Assert.AreEqual(0.75f, shipped.JumpGaugeSweepTime, 1e-5f);
            Assert.AreEqual(1.2f, shipped.JumpSpringMillionPerM, 1e-5f);
        }

        [Test]
        public void TheJumpIsRefusedOnASlopeSteeperThanTheKartAllows()
        {
            // Nothing under the kart at all is the sharpest form of "not on
            // ground it can push off", and it needs no slope geometry to set up.
            KartDynamicsConfig config = Recovered();
            var jump = new KartJumpState();
            var wheel = new KartWheelQueryOutput();

            Assert.That(KartDynamics.JumpCanStart(config, wheel, KartVec3.UnitZ), Is.False);

            // One wheel down is not enough either — a kart balanced on a kerb
            // must not launch off it.
            wheel.ActiveContacts = 1u;
            wheel.AverageNormal = KartVec3.UnitZ;
            Assert.That(KartDynamics.JumpCanStart(config, wheel, KartVec3.UnitZ), Is.False);

            wheel.ActiveContacts = 2u;
            Assert.That(KartDynamics.JumpCanStart(config, wheel, KartVec3.UnitZ), Is.True);

            var input = new KartJumpInput
            {
                Dt = 0.005f,
                JumpInput = true,
                Geometry = KartSimulation.DefaultGeometry,
                Right = KartVec3.UnitX,
                Forward = KartVec3.UnitY,
                Up = KartVec3.UnitZ,
                Wheel = new KartWheelQueryOutput(),
            };
            KartDynamics.StepJump(ref jump, config, input);
            Assert.AreEqual(KartJumpPhase.Ready, jump.Phase, "pressed with no ground under it");
        }
    }
}
