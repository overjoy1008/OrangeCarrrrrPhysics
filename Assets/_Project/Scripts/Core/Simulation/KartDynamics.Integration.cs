using System;

namespace OrangeCarrrrr.Core
{
    public struct KartDragInput
    {
        public KartVec3 LinearVelocity;
        public KartVec3 AngularVelocity;
        public bool Grounded;
        public float GroundedDragScale;
    }

    public struct KartDragOutput
    {
        public KartVec3 Force;
        public KartVec3 Torque;
    }

    public struct KartPoseInput
    {
        public KartVec3 Position;
        public KartQuat Orientation;
        public KartVec3 LinearVelocity;
        public KartVec3 AngularVelocity;
        public float Dt;
    }

    public struct KartPoseOutput
    {
        public KartVec3 Position;
        public KartQuat Orientation;
        public KartVec3 AngularVelocity;
        public float UpZ;
        public uint TiltRetries;
        public bool TiltClamped;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// Recovered from 0x00430640. Air friction is linear in both velocity and
        /// spin; on the ground a quadratic term is added on top, and that is the
        /// term the drag trigger scales.
        /// </summary>
        public static KartDragOutput ComputeDragResponse(
            in KartDynamicsConfig config,
            in KartDragInput input)
        {
            float speed = input.LinearVelocity.Magnitude;

            var output = new KartDragOutput
            {
                Force = input.LinearVelocity * -config.AirFriction,
                Torque = input.AngularVelocity * -config.AirFriction,
            };

            if (input.Grounded)
            {
                output.Force += input.LinearVelocity *
                                -(speed * config.DragFactor * input.GroundedDragScale);
            }
            return output;
        }

        /// <summary>Linear half of 0x00430740: v += (force / mass) * dt.</summary>
        public static KartVec3 IntegrateLinearVelocity(
            in KartVec3 velocity,
            in KartVec3 accumulatedForce,
            float mass,
            float dt)
        {
            if (mass <= 0f || dt <= 0f) return velocity;
            return velocity + accumulatedForce * (dt / mass);
        }

        /// <summary>
        /// Angular half of 0x00430740, including the gyroscopic term the original
        /// subtracts before applying the inverse inertia.
        /// </summary>
        public static KartVec3 IntegrateAngularVelocity(
            in KartVec3 angularVelocity,
            in KartVec3 accumulatedTorque,
            in KartMat3 inverseInertia,
            float dt)
        {
            KartVec3 inertiaVelocity = inverseInertia.Multiply(angularVelocity);
            KartVec3 gyroscopic = KartVec3.Cross(angularVelocity, inertiaVelocity);
            KartVec3 effectiveTorque = accumulatedTorque - gyroscopic;
            KartVec3 angularAcceleration = inverseInertia.Multiply(effectiveTorque);

            if (dt <= 0f) return angularVelocity;
            return angularVelocity + angularAcceleration * dt;
        }

        /// <summary>Quaternion helper 0x0042da70: body-local angular integration.</summary>
        public static KartQuat IntegrateOrientation(in KartQuat q, in KartVec3 omega, float dt)
        {
            float halfDt = dt * 0.5f;

            var result = new KartQuat(
                q.W + (-q.X * omega.X - q.Y * omega.Y - q.Z * omega.Z) * halfDt,
                q.X + (q.Y * omega.Z + q.W * omega.X - q.Z * omega.Y) * halfDt,
                q.Y + (q.Z * omega.X + q.W * omega.Y - q.X * omega.Z) * halfDt,
                q.Z + (q.X * omega.Y + q.W * omega.Z - q.Y * omega.X) * halfDt);

            float length = MathF.Sqrt(
                result.W * result.W + result.X * result.X +
                result.Y * result.Y + result.Z * result.Z);
            if (length > 0f)
            {
                result.W /= length;
                result.X /= length;
                result.Y /= length;
                result.Z /= length;
            }
            return result;
        }

        /// <summary>
        /// Recovered from 0x00430ed0. After integrating, the original refuses any
        /// orientation whose up axis has tipped past 60 degrees: it damps the
        /// tipping spin by 10x and retries up to three times, then zeroes it
        /// outright. That is why the kart cannot roll over however hard it is hit.
        /// </summary>
        public static KartPoseOutput IntegratePose(in KartPoseInput input)
        {
            const float minimumUpZ = 0.5f;
            const float retryDamping = 0.1f;

            var output = new KartPoseOutput
            {
                Position = input.Position + input.LinearVelocity * input.Dt,
                AngularVelocity = input.AngularVelocity,
            };
            output.Orientation = IntegrateOrientation(
                input.Orientation, output.AngularVelocity, input.Dt);
            output.UpZ = output.Orientation.UpZ;

            for (uint retry = 0; retry < 3u && output.UpZ < minimumUpZ; ++retry)
            {
                output.AngularVelocity.X *= retryDamping;
                output.AngularVelocity.Y *= retryDamping;
                output.Orientation = IntegrateOrientation(
                    input.Orientation, output.AngularVelocity, input.Dt);
                output.UpZ = output.Orientation.UpZ;
                output.TiltRetries += 1;
            }

            if (output.UpZ < minimumUpZ)
            {
                output.AngularVelocity.X = 0f;
                output.AngularVelocity.Y = 0f;
                output.Orientation = IntegrateOrientation(
                    input.Orientation, output.AngularVelocity, input.Dt);
                output.UpZ = output.Orientation.UpZ;
                output.TiltClamped = true;
            }
            return output;
        }
    }
}
