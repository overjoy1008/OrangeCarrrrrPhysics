using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>What a wheel ray found.</summary>
    public struct KartGroundHit
    {
        public KartVec3 Point;
        public KartVec3 Normal;
        public uint SurfaceId;
    }

    /// <summary>
    /// The world the simulation asks about the ground.
    ///
    /// Body collision — the hashed triangle grid and the oriented body box — is
    /// out of scope for this phase, so this is the whole world interface. Adding
    /// it later means a second method here and a call at the end of the substep;
    /// nothing else in the stepper changes.
    /// </summary>
    public interface IKartGroundQuery
    {
        /// <summary>
        /// Casts one wheel ray. Returns false when nothing was hit.
        /// </summary>
        bool QueryGround(in KartVec3 rayStart, in KartVec3 rayDelta, out KartGroundHit hit);
    }

    /// <summary>
    /// The flat ground plane the synthetic <c>flat_test</c> track uses, ported
    /// from <c>query_flat_ground</c>.
    ///
    /// The ray is rejected unless it points downward and actually straddles the
    /// plane, which is what stops a wheel that is already below the ground from
    /// generating a contact.
    /// </summary>
    public sealed class KartFlatGround : IKartGroundQuery
    {
        public float Height { get; set; }

        public KartFlatGround(float height = 0f) => Height = height;

        public bool QueryGround(in KartVec3 rayStart, in KartVec3 rayDelta, out KartGroundHit hit)
        {
            hit = default;

            float startZ = rayStart.Z - Height;
            if (rayDelta.Z >= 0f || startZ < 0f || startZ + rayDelta.Z > 0f) return false;

            float fraction = -startZ / rayDelta.Z;
            hit.Point = new KartVec3(
                rayStart.X + rayDelta.X * fraction,
                rayStart.Y + rayDelta.Y * fraction,
                Height);
            hit.Normal = KartVec3.UnitZ;
            hit.SurfaceId = 1u;
            return true;
        }
    }

    /// <summary>One frame's driver input, as the stepper reads it.</summary>
    [Serializable]
    public struct KartSimulationControls
    {
        public float ForwardInput;
        public float ReverseInput;
        public float SteeringInput;
        public bool ReverseSteering;
        public bool DriftInput;
        public bool BoostActive;

        /// <summary>
        /// Held, not pressed. The jump takes the press edge itself so that a key
        /// held from before the kart landed cannot start a new crouch.
        /// </summary>
        public bool JumpInput;
        public bool DriveDisabled;
    }

    /// <summary>What one wheel-contact query produced.</summary>
    public struct KartWheelQueryOutput
    {
        public KartSuspensionContact Contact0;
        public KartSuspensionContact Contact1;
        public KartSuspensionContact Contact2;
        public KartSuspensionContact Contact3;
        public KartVec3 AverageNormal;
        public uint SurfaceId;
        public uint ActiveContacts;
        public bool Grounded;
        public bool LandedThisStep;

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
}
