using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>What one substep of the jump needs to know about the kart.</summary>
    public struct KartJumpInput
    {
        public float Dt;

        /// <summary>The key as it is this substep. The press edge is taken inside.</summary>
        public bool JumpInput;

        public KartSimulationGeometry Geometry;
        public KartVec3 Right;
        public KartVec3 Forward;
        public KartVec3 Up;
        public KartVec3 LinearVelocity;

        /// <summary>The kart's world height, <c>position.z</c>, for the apex trace.</summary>
        public float Height;

        public KartWheelQueryOutput Wheel;
    }

    /// <summary>The jump's contribution to this substep, to be added to the totals.</summary>
    public struct KartJumpOutput
    {
        public KartVec3 Force;
        public KartVec3 Torque;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// Speed at which the velocity bias reaches full strength, in the
        /// original's world units.
        /// </summary>
        public const float JumpBiasFullSpeed = 20f;

        /// <summary>Below this the kart is not sliding anywhere worth leaning into.</summary>
        public const float JumpBiasMinSpeed = 0.1f;

        /// <summary>No wheel is ever unloaded completely, however the bias falls.</summary>
        public const float JumpMinWheelWeight = 0.1f;

        /// <summary>How far either way the bias is allowed to push, whatever the kart asks for.</summary>
        public const float JumpMaxVelocityBias = 0.9f;

        /// <summary>
        /// Airborne time before a touch counts as a landing, so the substep the
        /// wheels are still under the kart does not end the jump immediately.
        /// </summary>
        public const float JumpLandingGraceTime = 0.05f;

        /// <summary>
        /// <c>jump_can_start</c>: two wheels down, on ground flat enough, and the
        /// body roughly the same way up as the ground.
        ///
        /// All three matter. Two contacts rather than one keeps a kart balanced
        /// on a kerb from launching; the slope limit is the kart's own
        /// <c>JumpMaxSlopeDeg</c>; and the last test is what stops a kart that has
        /// landed on its roof from jumping off it.
        /// </summary>
        public static bool JumpCanStart(
            in KartDynamicsConfig config, in KartWheelQueryOutput wheel, in KartVec3 up)
        {
            float minimumNormalZ = MathF.Cos(
                config.JumpMaxSlopeDeg * KartConstants.Pi / KartConstants.DegreesPerHalfTurn);

            return wheel.ActiveContacts >= 2u &&
                   wheel.AverageNormal.Z >= minimumNormalZ &&
                   KartVec3.Dot(wheel.AverageNormal, up) > 0.5f;
        }

        /// <summary>
        /// <c>jump_power</c>: what the gauge is worth, from the kart's minimum
        /// efficiency to its maximum.
        ///
        /// Smoothstepped rather than linear, so the reward for releasing near the
        /// top of the sweep is flat rather than knife-edged — a few milliseconds
        /// either side of the peak are worth almost the same.
        /// </summary>
        public static float JumpPower(in KartDynamicsConfig config, float gaugePosition)
        {
            float position = MathF.Max(0f, MathF.Min(gaugePosition, 1f));
            float timing = position * position * (3f - 2f * position);
            float minimum = MathF.Max(config.JumpMinEfficiency, 0f);
            float maximum = MathF.Max(config.JumpMaxEfficiency, minimum);
            return minimum + (maximum - minimum) * timing;
        }

        /// <summary>
        /// The spring's charge at a gauge position: how far the suspension is
        /// wound down, and the energy that puts in it.
        ///
        /// The crouch goes as the square root of the strength so that the energy,
        /// which goes as the square of the crouch, comes out linear in it. A
        /// gauge read at half power stores half the joules rather than a quarter.
        /// </summary>
        private static void JumpCharge(
            in KartDynamicsConfig config,
            float strength,
            out float crouchDistance,
            out float storedEnergy)
        {
            float stiffness = MathF.Max(config.JumpSpringMillionPerM, 0f) * 1000000f;
            float maxCrouch = MathF.Max(config.JumpMaxCrouchDistance, 0f);

            crouchDistance = maxCrouch * MathF.Sqrt(strength);
            storedEnergy = 0.5f * stiffness * crouchDistance * crouchDistance;
        }

        /// <summary>
        /// <c>kart_step_jump</c>: one substep of the jump state machine, and the
        /// force and torque it asks for.
        ///
        /// The jump is force-driven, not an impulse written onto the velocity.
        /// The stroke is a half sine over <c>JumpPushDuration</c> whose area is
        /// the momentum the stored energy is worth, so the kart is pushed off the
        /// ground over several substeps and the suspension, the tyres and gravity
        /// all keep acting while it happens. That is why a heavier kart jumps
        /// lower from the same spring rather than the same height: the energy is
        /// fixed and <c>p = sqrt(2mE)</c> buys less height as m grows.
        ///
        /// Nothing here runs unless the key is pressed, and a kart whose key is
        /// never touched steps bit-for-bit as it did before the jump existed.
        /// </summary>
        public static KartJumpOutput StepJump(
            ref KartJumpState jump, in KartDynamicsConfig config, in KartJumpInput input)
        {
            var output = new KartJumpOutput();
            bool pressed = input.JumpInput;
            float dt = input.Dt;

            jump.AppliedForce = 0f;

            if (jump.Phase == KartJumpPhase.Ready)
            {
                if (pressed && !jump.PreviousInput &&
                    JumpCanStart(config, input.Wheel, input.Up))
                {
                    jump.Phase = KartJumpPhase.Crouch;
                    jump.PhaseTime = 0f;
                    jump.GaugePosition = 0f;
                    jump.JumpStrength = 0f;
                    jump.CrouchDistance = 0f;
                    jump.StoredEnergy = 0f;
                }
            }
            else if (jump.Phase == KartJumpPhase.Crouch)
            {
                if (!JumpCanStart(config, input.Wheel, input.Up))
                {
                    // Drove off the edge, or onto something too steep. The charge
                    // is dropped rather than kept for the next flat ground.
                    jump.Phase = KartJumpPhase.Ready;
                    jump.GaugePosition = 0f;
                    jump.JumpStrength = 0f;
                    jump.CrouchDistance = 0f;
                }
                else if (!pressed)
                {
                    BeginPush(ref jump, config, input.Height);
                }
                else
                {
                    // Up over the sweep time, back down over the next, then flat
                    // zero: holding past the second sweep is not a smaller jump
                    // than a mistimed one, it is the minimum.
                    float sweepTime = MathF.Max(config.JumpGaugeSweepTime, dt);
                    jump.PhaseTime += dt;

                    float cycle = jump.PhaseTime / sweepTime;
                    jump.GaugePosition = cycle <= 1f ? cycle : (cycle <= 2f ? 2f - cycle : 0f);
                    jump.JumpStrength = JumpPower(config, jump.GaugePosition);
                    JumpCharge(
                        config, jump.JumpStrength,
                        out jump.CrouchDistance, out jump.StoredEnergy);
                }
            }

            if (jump.Phase == KartJumpPhase.Push)
            {
                float duration = MathF.Max(config.JumpPushDuration, dt);

                if (!input.Wheel.Grounded)
                {
                    jump.Phase = KartJumpPhase.Airborne;
                    jump.PhaseTime = 0f;
                }
                else if (jump.PhaseTime < duration && config.Mass > 0f)
                {
                    Push(ref jump, config, input, duration, ref output);
                    jump.PhaseTime += dt;
                }
                else
                {
                    // The powered stroke is over. Residual suspension contact may
                    // remain for a few substeps while the chassis clears the ray.
                    jump.Phase = KartJumpPhase.Airborne;
                    jump.PhaseTime = 0f;
                }
            }
            else if (jump.Phase == KartJumpPhase.Airborne)
            {
                jump.PhaseTime += dt;
                if (input.Height > jump.ApexHeight) jump.ApexHeight = input.Height;

                // Either a fresh touch, or wheels that have been down a moment
                // and are no longer moving away from the ground. The second test
                // is what catches a kart that never fully cleared the rays.
                if (input.Wheel.LandedThisStep ||
                    (input.Wheel.Grounded && jump.PhaseTime > JumpLandingGraceTime &&
                     KartVec3.Dot(input.LinearVelocity, input.Wheel.AverageNormal) <= 0f))
                {
                    jump.Phase = KartJumpPhase.Landing;
                    jump.PhaseTime = config.JumpLandingCooldown;
                }
            }
            else if (jump.Phase == KartJumpPhase.Landing)
            {
                jump.PhaseTime = MathF.Max(jump.PhaseTime - dt, 0f);
                if (jump.PhaseTime == 0f && input.Wheel.Grounded)
                {
                    jump.Phase = KartJumpPhase.Ready;
                    jump.GaugePosition = 0f;
                    jump.JumpStrength = 0f;
                    jump.CrouchDistance = 0f;
                }
            }

            jump.PreviousInput = pressed;
            return output;
        }

        /// <summary><c>jump_begin_push</c>: the key came up, so the spring is let go.</summary>
        private static void BeginPush(
            ref KartJumpState jump, in KartDynamicsConfig config, float height)
        {
            jump.JumpStrength = JumpPower(config, jump.GaugePosition);
            JumpCharge(config, jump.JumpStrength, out jump.CrouchDistance, out jump.StoredEnergy);

            jump.Phase = KartJumpPhase.Push;
            jump.PhaseTime = 0f;
            jump.TakeoffHeight = height;
            jump.ApexHeight = height;
        }

        /// <summary>
        /// One substep of the powered stroke, spread over the wheels that are
        /// still down.
        ///
        /// The spread is what makes the jump steerable. A wheel on the side the
        /// kart is already sliding towards takes more of the push, which rolls
        /// the body into the slide — the original's own way of letting a jump out
        /// of a drift lean rather than go straight up. The bias is scaled by
        /// speed, so a standing jump is even across all four.
        /// </summary>
        private static void Push(
            ref KartJumpState jump,
            in KartDynamicsConfig config,
            in KartJumpInput input,
            float duration,
            ref KartJumpOutput output)
        {
            // Area of the half sine is the momentum the stored energy is worth.
            float impulse = MathF.Sqrt(MathF.Max(2f * config.Mass * jump.StoredEnergy, 0f));
            float peakForce = KartConstants.Pi * impulse / (2f * duration);
            float totalForce = peakForce * MathF.Sin(
                KartConstants.Pi * jump.PhaseTime / duration);

            // Between the ground's normal and the body's own up. Straight up the
            // slope's normal would throw a kart on a bank sideways; straight up
            // the body would ignore the bank altogether.
            float blend = MathF.Max(0f, MathF.Min(config.JumpBodyUpBlend, 1f));
            KartVec3 direction = (input.Wheel.AverageNormal * (1f - blend) +
                                  input.Up * blend).Normalized;

            KartVec3 tangentVelocity = input.LinearVelocity + input.Wheel.AverageNormal *
                -KartVec3.Dot(input.LinearVelocity, input.Wheel.AverageNormal);
            float tangentSpeed = tangentVelocity.Magnitude;

            float velocityBias = MathF.Max(-JumpMaxVelocityBias,
                MathF.Min(config.JumpVelocityDirectionBias, JumpMaxVelocityBias));
            float speedFactor = MathF.Min(tangentSpeed / JumpBiasFullSpeed, 1f);

            Span<float> weights = stackalloc float[KartConstants.WheelCount];
            float activeWeight = 0f;

            for (int i = 0; i < KartConstants.WheelCount; ++i)
            {
                if (!input.Wheel[i].Active) continue;

                float alignment = 0f;
                if (tangentSpeed > JumpBiasMinSpeed)
                {
                    KartVec3 contactOffset =
                        input.Right * (input.Geometry.HalfWidth * KartConstants.WheelRightSign[i]) +
                        input.Forward * (input.Geometry.HalfLength * KartConstants.WheelForwardSign[i]);

                    alignment = KartVec3.Dot(
                        contactOffset.Normalized, tangentVelocity * (1f / tangentSpeed));
                }

                weights[i] = MathF.Max(
                    JumpMinWheelWeight, 1f + velocityBias * speedFactor * alignment);
                activeWeight += weights[i];
            }

            if (activeWeight > 0f)
            {
                for (int i = 0; i < KartConstants.WheelCount; ++i)
                {
                    if (!input.Wheel[i].Active) continue;

                    float wheelForce = totalForce * weights[i] / activeWeight;
                    KartVec3 worldForce = direction * wheelForce;

                    // The lever and the force are both in body space here, which
                    // is where the torque has to be: the integrator's inertia
                    // tensor is the body's.
                    var lever = new KartVec3(
                        input.Geometry.HalfWidth * KartConstants.WheelRightSign[i],
                        -input.Geometry.HalfLength * KartConstants.WheelForwardSign[i],
                        0f);
                    var localForce = new KartVec3(
                        KartVec3.Dot(worldForce, input.Right),
                        -KartVec3.Dot(worldForce, input.Forward),
                        KartVec3.Dot(worldForce, input.Up));

                    output.Force += worldForce;
                    output.Torque += KartVec3.Cross(lever, localForce) * config.JumpTorqueScale;
                }
            }

            jump.AppliedForce = totalForce;
        }
    }
}
