using System;

namespace OrangeCarrrrr.Core
{
    public enum KartLateralMode
    {
        Grip = 0,
        Drift = 1,
        DriftTrigger = 2,
    }

    public struct KartLateralInput
    {
        public float ForwardVelocity;
        public float LateralVelocity;
        public float YawLeverVelocity;
        public float SteeringInput;
        public float ForwardInput;
        public float PreviousSteerAngleRad;
        public bool ReverseSteering;
        public bool DriftInputActive;
        public KartLateralMode Mode;
    }

    public struct KartLateralOutput
    {
        public float Speed;
        public float SteerAngleRad;
        public float NextPreviousSteerAngleRad;
        public float FrontSlip;
        public float RearSlip;
        public float FrontForce;
        public float RearForce;
        public float LocalLateralForce;
        public float LocalForwardForce;
        public float LocalRollTorque;
        public float LocalYawTorque;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// Recovered from 0x0042fcde-0x0042fd42. Steering authority decays
        /// exponentially with forward speed, so the same key deflection bends the
        /// kart far less at speed than it does from rest.
        /// </summary>
        public static float SteerAngleRad(
            in KartDynamicsConfig config,
            float forwardVelocity,
            float steeringInput,
            bool reverseSteering)
        {
            float direction = reverseSteering ? -1f : 1f;
            float maximum =
                KartConstants.Pi * config.MaxSteerAngleDeg / KartConstants.DegreesPerHalfTurn;
            float attenuation = MathF.Exp(-MathF.Abs(forwardVelocity / config.SteerConstraint));
            return maximum * direction * steeringInput * attenuation;
        }

        /// <summary>
        /// The tyre model, recovered from 0x0042fe1c-0x00430588.
        ///
        /// Front and rear slip are the steering angle and the yaw lever measured
        /// against a denominator that is floored at 5, which is what stops the
        /// forces exploding at a standstill. 0x0042FEF8 enters a dedicated speed
        /// &lt;= 5 branch before any drift branch, so below that speed the kart
        /// always uses ordinary grip forces however the drift state reads.
        /// </summary>
        public static KartLateralOutput ComputeLateralResponse(
            in KartDynamicsConfig config,
            in KartLateralInput input)
        {
            var output = new KartLateralOutput();

            float travelDirection = input.ForwardVelocity <= 0f ? -1f : 1f;
            float steeringDirection = input.ReverseSteering ? -1f : 1f;
            float maximumSteer =
                KartConstants.Pi * config.MaxSteerAngleDeg / KartConstants.DegreesPerHalfTurn *
                steeringDirection * input.SteeringInput;

            float filteredSteer = SteerAngleRad(
                config, input.ForwardVelocity, input.SteeringInput, input.ReverseSteering);

            output.Speed = MathF.Sqrt(
                input.ForwardVelocity * input.ForwardVelocity +
                input.LateralVelocity * input.LateralVelocity);

            // While the throttle is down, the steering angle is not allowed to
            // grow past what it already was in the same direction: the filter
            // holds the previous value instead.
            if (input.ForwardInput != 0f &&
                ((input.PreviousSteerAngleRad > 0f && filteredSteer > 0f) ||
                 (input.PreviousSteerAngleRad < 0f && filteredSteer < 0f)) &&
                MathF.Abs(input.PreviousSteerAngleRad) < MathF.Abs(filteredSteer))
            {
                filteredSteer = input.PreviousSteerAngleRad;
                output.NextPreviousSteerAngleRad = input.PreviousSteerAngleRad;
            }
            else
            {
                output.NextPreviousSteerAngleRad = filteredSteer;
            }
            output.SteerAngleRad = filteredSteer;

            float denominator = output.Speed <= KartConstants.LowSpeedDenominator
                ? KartConstants.LowSpeedDenominator
                : output.Speed;

            float steerForSlip = output.Speed >= KartConstants.SteerActiveSpeed
                ? travelDirection *
                  ((output.Speed > KartConstants.LowSpeedDenominator &&
                    input.Mode == KartLateralMode.Drift && input.DriftInputActive)
                      ? maximumSteer
                      : filteredSteer)
                : 0f;

            output.FrontSlip =
                steerForSlip -
                input.LateralVelocity / denominator -
                input.YawLeverVelocity * 0.5f / denominator;
            output.RearSlip =
                -input.LateralVelocity / denominator +
                input.YawLeverVelocity * 0.5f / denominator;

            if (output.Speed > KartConstants.LowSpeedDenominator &&
                input.Mode == KartLateralMode.DriftTrigger)
            {
                output.FrontForce = 0f;
                output.RearForce =
                    maximumSteer * config.DriftTriggerFactor *
                    -(KartConstants.Gravity * config.Mass) * config.FrontGripFactor;
            }
            else
            {
                output.FrontForce =
                    output.FrontSlip * KartConstants.Gravity * config.Mass * config.FrontGripFactor;
                output.RearForce =
                    output.RearSlip * KartConstants.Gravity * config.Mass * config.RearGripFactor;

                if (output.Speed > KartConstants.LowSpeedDenominator &&
                    input.Mode == KartLateralMode.Drift)
                {
                    output.FrontForce *= config.DriftSlipFactor;
                    output.RearForce *= config.DriftSlipFactor;
                }
            }

            output.LocalLateralForce = output.FrontForce + output.RearForce;
            output.LocalYawTorque = 0.5f * output.FrontForce - 0.5f * output.RearForce;

            // 0x00430311-0x0043033A stores -abs(lateral force) * factor in the
            // executable's local Y axis. Body forward is -local-Y, so this is the
            // equivalent positive body-forward force.
            if (output.Speed > KartConstants.LowSpeedDenominator &&
                input.Mode == KartLateralMode.Grip)
            {
                output.LocalForwardForce =
                    MathF.Abs(output.LocalLateralForce) * config.CornerDrawFactor;
            }

            if (output.Speed > KartConstants.LowSpeedDenominator &&
                input.Mode == KartLateralMode.Drift)
            {
                float lowSpeedScale =
                    output.Speed <= KartConstants.LeanSpeedThreshold ? 0.5f : 1f;
                output.LocalRollTorque =
                    -output.LocalLateralForce * config.DriftLeanFactor * lowSpeedScale;
            }
            else if (input.Mode == KartLateralMode.Grip &&
                     output.Speed > KartConstants.LowSpeedDenominator)
            {
                output.LocalRollTorque = -output.LocalLateralForce * config.SteerLeanFactor;
            }

            return output;
        }
    }
}
