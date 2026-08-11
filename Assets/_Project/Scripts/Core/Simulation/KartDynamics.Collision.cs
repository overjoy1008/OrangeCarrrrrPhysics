using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>One triangle the body box is touching.</summary>
    public struct KartBodyContact
    {
        public KartVec3 Normal;

        /// <summary>
        /// Triangle centroid, which is what the original reports at 0x00433310.
        /// The linear resolver does not read it; it is here so the contact
        /// carries the same information the original's did.
        /// </summary>
        public KartVec3 Point;

        public float SweepFraction;
        public uint SurfaceId;
    }

    /// <summary>The body-box half of the world, kept separate from the ground ray.</summary>
    public interface IKartBodyCollisionQuery
    {
        /// <summary>Fills <paramref name="contacts"/> and returns how many.</summary>
        int QueryBodyCollisions(
            KartSimulationState state, KartBodyContact[] contacts, int capacity);
    }

    public struct KartCollisionInput
    {
        public KartVec3 Velocity;
        public KartVec3 AngularVelocity;
        public KartVec3 Normal;
        public KartVec3 BodyRight;
        public KartVec3 BodyForward;
        public KartVec3 BodyUp;
    }

    public struct KartCollisionOutput
    {
        public KartVec3 Velocity;
        public KartVec3 AngularVelocity;
        public bool Incoming;

        /// <summary>True when the contact took the wall branch, i.e. normal.z &lt;= 0.65.</summary>
        public bool WallContact;

        public float NormalSpeed;
        public float TangentialSpeedRemoved;
        public float WallYawKick;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// The linear-velocity portion of 0x00430830.
        ///
        /// The branch is on the contact normal's Z, not on the sweep fraction:
        /// a steep face takes the wall branch, a shallow one takes the ground
        /// branch. The wall branch removes the approach speed with a 1.5x
        /// impulse, scrubs tangential speed, and adds a yaw kick; the ground
        /// branch is a soft 0.2 restitution plus a small levelling torque.
        /// </summary>
        public static KartCollisionOutput ResolveLinearCollision(in KartCollisionInput input)
        {
            const float wallNormalZLimit = 0.6499999761581421f;
            const float hardNormalImpulse = 1.5f;
            const float hardTangentLimit = 0.6000000238418579f;
            const float softRestitution = 0.20000000298023224f;

            var output = new KartCollisionOutput
            {
                Velocity = input.Velocity,
                AngularVelocity = input.AngularVelocity,
            };

            float signedNormalSpeed = KartVec3.Dot(input.Normal, input.Velocity);
            if (signedNormalSpeed >= 0f) return output;

            output.Incoming = true;
            output.NormalSpeed = -signedNormalSpeed;

            KartVec3 normalVelocity = input.Normal * signedNormalSpeed;
            KartVec3 tangentVelocity = input.Velocity - normalVelocity;

            if (input.Normal.Z <= wallNormalZLimit)
            {
                float tangentSpeed = tangentVelocity.Magnitude;
                float normalForward = KartVec3.Dot(input.Normal, input.BodyForward);
                float normalRight = KartVec3.Dot(input.Normal, input.BodyRight);
                float turnSpeed = MathF.Min(MathF.Max(output.NormalSpeed, 1f), 30f);

                output.WallContact = true;
                output.TangentialSpeedRemoved = MathF.Min(
                    output.NormalSpeed * hardNormalImpulse,
                    tangentSpeed * hardTangentLimit);

                var tangentDirection = KartVec3.Zero;
                if (tangentSpeed > 0f) tangentDirection = tangentVelocity * (1f / tangentSpeed);

                KartVec3 correction =
                    normalVelocity * -hardNormalImpulse +
                    tangentDirection * -output.TangentialSpeedRemoved;
                // The original removes the vertical component of the hard correction.
                correction.Z = 0f;
                output.Velocity = input.Velocity + correction;

                if (MathF.Abs(normalForward) <= MathF.Abs(normalRight))
                {
                    float sideSign = normalRight <= 0f ? 1f : -1f;
                    output.WallYawKick = normalForward * sideSign * turnSpeed;
                }
                else
                {
                    float forwardSign = normalForward <= 0f ? -1f : 1f;
                    output.WallYawKick = normalRight * forwardSign * turnSpeed;
                }

                // The candidate is accepted unless existing same-direction spin
                // is already strong.
                if (output.WallYawKick * output.AngularVelocity.Z <= 1f)
                {
                    output.AngularVelocity.Z += output.WallYawKick;
                }
                else
                {
                    output.WallYawKick = 0f;
                }
            }
            else
            {
                KartVec3 wallTurnAxis = KartVec3.Cross(input.Normal, input.BodyUp);
                output.Velocity = tangentVelocity + normalVelocity * -softRestitution;

                // Late-contact rotational correction at 0x00430df2-0x00430e73.
                output.AngularVelocity.X -=
                    KartVec3.Dot(wallTurnAxis, input.BodyRight) * 0.1f;
                output.AngularVelocity.Y +=
                    KartVec3.Dot(wallTurnAxis, input.BodyForward) * 0.1f;
            }

            return output;
        }
    }
}
