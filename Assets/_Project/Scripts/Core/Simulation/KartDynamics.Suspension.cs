using System;

namespace OrangeCarrrrr.Core
{
    public struct KartSuspensionContact
    {
        public bool Active;
        public KartVec3 Normal;
        public float Compression;
        public float CompressionDelta;
    }

    public struct KartSuspensionInput
    {
        public float Dt;
        public float HalfWidth;
        public float HalfLength;
        public float CompressionDamping;
        public KartVec3 ChassisUp;
        public KartSuspensionContact Contact0;
        public KartSuspensionContact Contact1;
        public KartSuspensionContact Contact2;
        public KartSuspensionContact Contact3;

        public KartSuspensionContact this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Contact0;
                    case 1: return Contact1;
                    case 2: return Contact2;
                    case 3: return Contact3;
                    default: throw new IndexOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: Contact0 = value; break;
                    case 1: Contact1 = value; break;
                    case 2: Contact2 = value; break;
                    case 3: Contact3 = value; break;
                    default: throw new IndexOutOfRangeException(nameof(index));
                }
            }
        }
    }

    public struct KartSuspensionOutput
    {
        public KartVec3 WorldForce;
        public KartVec3 LocalTorque;
        public float ContactForce0;
        public float ContactForce1;
        public float ContactForce2;
        public float ContactForce3;
        public uint ActiveContacts;
    }

    public static partial class KartDynamics
    {
        /// <summary>
        /// Recovered from 0x0042f460; the contact generation that precedes it is
        /// at 0x0042ef90.
        ///
        /// Gravity goes in first, then each loaded wheel pushes the chassis along
        /// its own up axis. The push is scaled by dot(contact normal, chassis up),
        /// which is what makes a face whose normal points into the surface pull
        /// the kart down instead — the reason a mis-wound export drops a kart
        /// through a road.
        /// </summary>
        public static KartSuspensionOutput ComputeSuspensionResponse(
            in KartDynamicsConfig config,
            in KartSuspensionInput input)
        {
            var output = new KartSuspensionOutput();

            float staticForce = MathF.Abs(KartConstants.WorldGravity) * config.Mass * 0.5f;
            float compressionDamping = MathF.Max(input.CompressionDamping, 0f);
            float reboundDamping = staticForce * KartConstants.SuspensionReboundRatio;

            output.WorldForce = new KartVec3(0f, 0f, KartConstants.WorldGravity * config.Mass);

            for (int i = 0; i < KartConstants.WheelCount; ++i)
            {
                KartSuspensionContact contact = input[i];
                if (!contact.Active || input.Dt <= 0f) continue;

                // Rebound is damped at 20% of the static force; compression is
                // undamped unless a caller supplies a value.
                float damping = contact.CompressionDelta <= 0f
                    ? reboundDamping
                    : compressionDamping;

                float force =
                    contact.CompressionDelta / input.Dt * damping +
                    staticForce * contact.Compression;

                if (force <= 0f) continue;

                force *= KartVec3.Dot(contact.Normal, input.ChassisUp);
                SetContactForce(ref output, i, force);
                output.ActiveContacts += 1;
                output.WorldForce += input.ChassisUp * force;

                var lever = new KartVec3(
                    input.HalfWidth * KartConstants.WheelRightSign[i],
                    -input.HalfLength * KartConstants.WheelForwardSign[i],
                    0f);
                var localForce = new KartVec3(0f, 0f, force);
                output.LocalTorque += KartVec3.Cross(lever, localForce) *
                                      KartConstants.SuspensionTorqueScale;
            }

            return output;
        }

        private static void SetContactForce(ref KartSuspensionOutput output, int index, float force)
        {
            switch (index)
            {
                case 0: output.ContactForce0 = force; break;
                case 1: output.ContactForce1 = force; break;
                case 2: output.ContactForce2 = force; break;
                case 3: output.ContactForce3 = force; break;
            }
        }
    }
}
