using System;

namespace OrangeCarrrrr.Core
{
    public enum KartLongitudinalMode
    {
        Idle = 0,
        Forward = 1,
        Brake = 2,
        Reverse = 3,
        Stopped = 4,
    }

    [Serializable]
    public struct KartLongitudinalState
    {
        public float ReverseTimer;
    }

    public struct KartLongitudinalInput
    {
        public KartVec3 Velocity;
        public KartVec3 ForwardAxis;
        public float ForwardVelocity;
        public float LateralVelocity;
        public float Dt;
        public float ForwardInput;
        public float ReverseInput;
        public bool DriveDisabled;
        public bool DriftInputActive;
        public bool DriftSlipDetected;
        public bool BoostActive;
    }

    public struct KartLongitudinalOutput
    {
        public KartVec3 Force;
        public KartVec3 Velocity;
        public KartLongitudinalMode Mode;
        public bool VelocityOverridden;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// Main forward branch of 0x0042f6c0. A kart that is already sliding
        /// sideways pushes with the drift escape force instead of the ordinary
        /// accelerator force, which is what lets it dig out of a slide.
        /// </summary>
        public static KartVec3 ComputeForwardDriveForce(
            in KartDynamicsConfig config,
            in KartVec3 forwardAxis,
            float inputAmount,
            bool driftSlipDetected,
            bool boostActive)
        {
            float baseForce = driftSlipDetected
                ? config.DriftEscapeForce
                : config.ForwardAccelForce;
            float boost = boostActive ? BoostForceMultiplier : 1f;
            return forwardAxis * (inputAmount * baseForce * boost);
        }

        /// <summary>Reverse-acceleration branch at 0x0042fa98-0x0042fadb.</summary>
        public static KartVec3 ComputeReverseDriveForce(
            in KartDynamicsConfig config,
            in KartVec3 forwardAxis,
            float inputAmount)
            => forwardAxis * (-inputAmount * config.BackwardAccelForce);

        /// <summary>
        /// Brake selection at 0x0042fb6f-0x0042fbcd: a kart travelling roughly
        /// where it points brakes with the grip force, one that is sliding brakes
        /// with the weaker slip force.
        /// </summary>
        public static KartVec3 ComputeDirectionalBrakeForce(
            in KartDynamicsConfig config,
            in KartVec3 velocity,
            in KartVec3 forwardAxis)
        {
            const float gripAlignmentThreshold = 0.800000011920929f;
            KartVec3 direction = velocity.Normalized;
            float alignment = KartVec3.Dot(direction, forwardAxis);
            float magnitude = alignment <= gripAlignmentThreshold
                ? config.SlipBrakeForce
                : config.GripBrakeForce;
            return direction * -magnitude;
        }

        /// <summary>
        /// The full input and timer state machine recovered from 0x0042f6c0.
        ///
        /// The reverse timer is what makes the down key brake first and only back
        /// up after the kart has been near a standstill for 0.2 s.
        /// </summary>
        public static KartLongitudinalOutput StepLongitudinal(
            in KartDynamicsConfig config,
            ref KartLongitudinalState state,
            in KartLongitudinalInput input)
        {
            const float directionThreshold = 0.5f;
            const float reverseDelay = 0.20000000298023224f;
            const float lateralStopThreshold = 0.20000000298023224f;

            var output = new KartLongitudinalOutput
            {
                Velocity = input.Velocity,
                Mode = KartLongitudinalMode.Idle,
            };

            if (input.ForwardInput != 0f && !input.DriveDisabled)
            {
                float speed = input.Velocity.Magnitude;
                output.Force = ComputeForwardDriveForce(
                    config,
                    input.ForwardAxis,
                    input.ForwardInput,
                    input.DriftSlipDetected,
                    input.BoostActive);

                // Extra recovery force opposes residual reverse travel.
                if (input.ForwardVelocity < 0f)
                {
                    float recoverySpeed = input.DriftInputActive || input.DriftSlipDetected
                        ? speed
                        : MathF.Min(speed, KartConstants.LowSpeedDenominator);
                    output.Force += input.ForwardAxis *
                                    (recoverySpeed * config.Mass * KartConstants.Gravity);
                }

                state.ReverseTimer = 0f;
                output.Mode = KartLongitudinalMode.Forward;
                return output;
            }

            if (input.ReverseInput == 0f && !input.DriveDisabled)
            {
                if (input.ForwardVelocity <= directionThreshold &&
                    input.ForwardVelocity >= -directionThreshold)
                {
                    state.ReverseTimer += input.Dt;
                }
                return output;
            }

            bool shouldBrake = true;
            if (input.ForwardVelocity <= directionThreshold)
            {
                state.ReverseTimer += input.Dt;
                if (input.ForwardVelocity <= -directionThreshold) state.ReverseTimer = 1f;

                if (state.ReverseTimer <= reverseDelay)
                {
                    if (MathF.Abs(input.LateralVelocity) <= lateralStopThreshold)
                    {
                        output.Velocity = KartVec3.Zero;
                        output.VelocityOverridden = true;
                        output.Mode = KartLongitudinalMode.Stopped;
                        shouldBrake = false;
                    }
                }
                else if (input.DriveDisabled)
                {
                    if (input.ForwardVelocity <= -directionThreshold)
                    {
                        output.Force = input.Velocity.Normalized * -config.GripBrakeForce;
                        output.Mode = KartLongitudinalMode.Brake;
                    }
                    else
                    {
                        output.Velocity = KartVec3.Zero;
                        output.VelocityOverridden = true;
                        output.Mode = KartLongitudinalMode.Stopped;
                    }
                    shouldBrake = false;
                }
                else
                {
                    output.Force = ComputeReverseDriveForce(
                        config, input.ForwardAxis, input.ReverseInput);
                    output.Mode = KartLongitudinalMode.Reverse;
                    shouldBrake = false;
                }
            }

            if (shouldBrake)
            {
                output.Force = ComputeDirectionalBrakeForce(
                    config, input.Velocity, input.ForwardAxis);
                output.Mode = KartLongitudinalMode.Brake;
            }
            return output;
        }
    }
}
