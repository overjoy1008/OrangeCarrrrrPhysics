using System;
using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The assertions from <c>Scripts/Tests/test_kart_dynamics.c</c>, ported so
    /// the numbers the port produces are pinned to the ones the recovery pinned.
    ///
    /// These are the regression net for the physics: if a refactor moves a
    /// constant or reorders an expression, this is what catches it. Every
    /// expected value here came out of the original executable, not out of this
    /// implementation.
    /// </summary>
    public sealed class KartDynamicsTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void RecoveredDefaults_MatchTheOriginal()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();

            Assert.That(config.Mass, Is.EqualTo(100f).Within(Epsilon));
            Assert.That(config.ForwardAccelForce, Is.EqualTo(3000f).Within(Epsilon));
            Assert.That(config.DriftEscapeForce, Is.EqualTo(5000f).Within(Epsilon));
            Assert.That(config.DriftTriggerTime, Is.EqualTo(0.1f).Within(Epsilon));
        }

        [Test]
        public void Cotten5_MatchesTheExtractedParameters()
        {
            KartSpec cotten5 = KartDemoData.Cotten5;

            Assert.That(cotten5.Geometry.HalfLength, Is.EqualTo(1.13917575f).Within(Epsilon));
            Assert.That(cotten5.Geometry.HalfWidth, Is.EqualTo(0.87533175f).Within(Epsilon));
            Assert.That(cotten5.Dynamics.DragFactor, Is.EqualTo(0.725f).Within(Epsilon));
            Assert.That(cotten5.Dynamics.DriftTriggerTime, Is.EqualTo(0.2f).Within(Epsilon));
            Assert.That(cotten5.MaxBoosters, Is.EqualTo(2u));
        }

        /// <summary>
        /// Steering authority decays as exp(-|v| / steerConstraint), so at a speed
        /// equal to the constraint it is down to 1/e of its standing value.
        /// </summary>
        [Test]
        public void SteeringAttenuates_WithForwardSpeed()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();

            float atRest = KartDynamics.SteerAngleRad(config, 0f, 1f, false);
            float atSpeed = KartDynamics.SteerAngleRad(config, 30f, 1f, false);
            float reversed = KartDynamics.SteerAngleRad(config, 0f, 1f, true);

            Assert.That(atRest, Is.EqualTo(0.17453294f).Within(0.00001f));
            Assert.That(atSpeed, Is.EqualTo(atRest * MathF.Exp(-1f)).Within(0.00001f));
            Assert.That(reversed, Is.EqualTo(-atRest).Within(0.00001f));
        }

        [Test]
        public void StraightRunning_ProducesNoTyreForce()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();
            var input = new KartLateralInput
            {
                ForwardVelocity = 20f,
                Mode = KartLateralMode.Grip,
            };

            KartLateralOutput output = KartDynamics.ComputeLateralResponse(config, input);

            Assert.That(output.FrontForce, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(output.RearForce, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(output.LocalYawTorque, Is.EqualTo(0f).Within(Epsilon));
        }

        /// <summary>
        /// 0x0042FEF8 takes a dedicated speed &lt;= 5 branch before any drift
        /// branch, so below that speed every mode produces the same forces and no
        /// corner-draw force at all.
        /// </summary>
        [Test]
        public void BelowFiveUnits_AllDriftModesAgree()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();
            var input = new KartLateralInput
            {
                ForwardVelocity = 3f,
                LateralVelocity = 2f,
                SteeringInput = 1f,
                DriftInputActive = true,
                Mode = KartLateralMode.Grip,
            };

            KartLateralOutput grip = KartDynamics.ComputeLateralResponse(config, input);

            config.CornerDrawFactor = 0.5f;
            input.Mode = KartLateralMode.Drift;
            KartLateralOutput drift = KartDynamics.ComputeLateralResponse(config, input);
            input.Mode = KartLateralMode.DriftTrigger;
            KartLateralOutput trigger = KartDynamics.ComputeLateralResponse(config, input);

            Assert.That(drift.FrontForce, Is.EqualTo(grip.FrontForce).Within(0.001f));
            Assert.That(drift.RearForce, Is.EqualTo(grip.RearForce).Within(0.001f));
            Assert.That(trigger.FrontForce, Is.EqualTo(grip.FrontForce).Within(0.001f));
            Assert.That(trigger.RearForce, Is.EqualTo(grip.RearForce).Within(0.001f));
            Assert.That(drift.LocalForwardForce, Is.EqualTo(0f).Within(0.001f));
            Assert.That(trigger.LocalForwardForce, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void CornerDrawForce_AppliesOnlyWhileGripping()
        {
            KartDynamicsConfig config = KartDynamicsConfig.Default();
            config.CornerDrawFactor = 0.2f;

            var input = new KartLateralInput
            {
                ForwardVelocity = 20f,
                SteeringInput = 1f,
                Mode = KartLateralMode.Grip,
            };

            KartLateralOutput output = KartDynamics.ComputeLateralResponse(config, input);
            Assert.That(
                output.LocalForwardForce,
                Is.EqualTo(MathF.Abs(output.LocalLateralForce) * 0.2f).Within(0.001f));

            input.Mode = KartLateralMode.Drift;
            output = KartDynamics.ComputeLateralResponse(config, input);
            Assert.That(output.LocalForwardForce, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Speedometer_ConvertsWithTheOriginalFactor()
        {
            var velocity = new KartVec3(3f, 4f, 0f);

            Assert.That(KartUnits.SpeedKmh(velocity), Is.EqualTo(18f).Within(Epsilon));
            Assert.That(KartUnits.SpeedometerKmh(velocity), Is.EqualTo(18));
        }

        /// <summary>
        /// The recovered input rule: the most recent direction owns steering, so
        /// holding both keys does not cancel to zero. This is what makes the
        /// original's new-cut drift input possible.
        /// </summary>
        [Test]
        public void SteeringOwnership_FollowsTheMostRecentPress()
        {
            var steering = new KartSteeringInput();

            steering.KeyEvent(KartSteeringKey.Left, true);
            Assert.That(steering.Value, Is.EqualTo(-1f));

            steering.KeyEvent(KartSteeringKey.Right, true);
            Assert.That(steering.Value, Is.EqualTo(1f), "the newer press takes ownership");

            steering.KeyEvent(KartSteeringKey.Right, false);
            Assert.That(steering.Value, Is.EqualTo(0f), "releasing the owner clears it");

            steering.KeyEvent(KartSteeringKey.Left, false);
            Assert.That(steering.Value, Is.EqualTo(0f));
        }
    }
}
