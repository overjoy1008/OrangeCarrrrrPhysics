using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>Body geometry the wheel rays and the body box are built from.</summary>
    [Serializable]
    public struct KartSimulationGeometry
    {
        public float HalfWidth;
        public float HalfLength;
        public float SuspensionRange;
        public float GroundedDragScale;

        /// <summary>The demo's own default, <c>kart_simulation_default_geometry</c>.</summary>
        public static KartSimulationGeometry Default => new KartSimulationGeometry
        {
            HalfWidth = 0.9f,
            HalfLength = 1.2f,
            SuspensionRange = 0.5f,
            GroundedDragScale = 1.0f,
        };
    }

    /// <summary><c>KartDriftState</c> from <c>kart_dynamics.h</c>.</summary>
    [Serializable]
    public struct KartDriftState
    {
        public bool InputActive;
        public bool SlipDetected;
        public bool TriggerActive;
        public bool EntryWasForward;
        public float TriggerTimer;
        public float LingerTimer;

        /// <summary>The label <c>drift_phase_name</c> prints in the telemetry panel.</summary>
        public string PhaseName
        {
            get
            {
                if (TriggerActive) return "TRIGGER";
                if (SlipDetected) return "SLIP";
                if (InputActive) return "DRIFT";
                return "GRIP";
            }
        }
    }

    /// <summary>Per-wheel suspension compression, 0 extended to 1 fully compressed.</summary>
    [Serializable]
    public struct KartWheelContactState
    {
        public const int WheelCount = 4;

        public float Compression0;
        public float Compression1;
        public float Compression2;
        public float Compression3;
        public bool Grounded;

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Compression0;
                    case 1: return Compression1;
                    case 2: return Compression2;
                    case 3: return Compression3;
                    default: throw new IndexOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: Compression0 = value; break;
                    case 1: Compression1 = value; break;
                    case 2: Compression2 = value; break;
                    case 3: Compression3 = value; break;
                    default: throw new IndexOutOfRangeException(nameof(index));
                }
            }
        }
    }

    /// <summary>What one call to the stepper resolved, for the STEP telemetry row.</summary>
    [Serializable]
    public struct KartSimulationStepResult
    {
        public uint Substeps;
        public uint WheelContacts;
        public uint BodyContacts;
        public bool Grounded;
        public bool Landed;
        public float WallImpactSpeed;
        public float GroundImpactSpeed;
    }

    /// <summary>
    /// The rigid-body state the whole simulator reads, matching
    /// <c>KartSimulationState</c>. Phase 1 fills it at rest and never advances it;
    /// phase 2 replaces <c>KartRestPose</c> with the ported integrator and nothing
    /// downstream of this type has to change.
    ///
    /// A class rather than a struct on purpose: the HUD, the camera rig and the
    /// kart view all observe one instance instead of copying 200 bytes per frame.
    /// </summary>
    [Serializable]
    public class KartSimulationState
    {
        public KartDynamicsConfig Config;
        public KartSimulationGeometry Geometry;

        public KartVec3 Position;
        public KartQuat Orientation = KartQuat.Identity;
        public KartVec3 LinearVelocity;
        public KartVec3 AngularVelocity;

        public KartDriftState Drift;
        public KartLongitudinalState Longitudinal;
        public KartInstantBoostState InstantBoost;
        public KartTimedBoostState TimedBoost;
        public KartWheelContactState Wheels;

        public float PreviousSteerAngleRad;
        public float GroundedDragScale = 1f;
        public bool PreviousForwardInput;
        public bool PreviousDriftInput;
        public bool PreviousBoostInput;
        public bool Grounded;

        /// <summary>
        /// Which boost cutoff model is running: false ends a boost when the
        /// throttle is released, true keeps it alive until reverse is pressed.
        ///
        /// Simulator-side, not recovered. It survives <c>Init</c> because it is a
        /// choice about the bench rather than part of the kart's state.
        /// </summary>
        public bool ReverseInputEndsBoost;

        /// <summary>Finite-differenced by the simulator, not integrated. HUD only.</summary>
        public KartVec3 Acceleration;

        public KartSimulationStepResult LastStep;

        public void GetBodyAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up)
            => Orientation.GetAxes(out right, out forward, out up);

        public float Speed => LinearVelocity.Magnitude;

        public float ForwardSpeed
        {
            get
            {
                GetBodyAxes(out _, out KartVec3 forward, out _);
                return KartVec3.Dot(LinearVelocity, forward);
            }
        }

        public float LateralSpeed
        {
            get
            {
                GetBodyAxes(out KartVec3 right, out _, out _);
                return KartVec3.Dot(LinearVelocity, right);
            }
        }
    }
}
