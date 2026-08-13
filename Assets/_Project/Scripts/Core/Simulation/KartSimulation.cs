using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The fixed-step stepper, ported from <c>kart_simulation.c</c>.
    ///
    /// A frame's milliseconds are cut into 5 ms substeps and each substep runs
    /// the whole pipeline: wheel rays, suspension, longitudinal drive, drift
    /// state, tyre forces, drag, then integration. Boost bookkeeping straddles
    /// the substeps because the original ticks the item boost once per frame in
    /// milliseconds while the instant boost ticks per substep in seconds.
    ///
    /// The jump runs outside the grounded branch, after the tyre forces and
    /// before drag, exactly where <c>kart_step_jump</c> is called. It contributes
    /// nothing at all until the key is pressed, so a kart that never jumps steps
    /// as it did before it existed.
    ///
    /// Deliberately out of scope: the drift gauge, and the simulator's alternate
    /// boost-storage and cutoff models. Where the C source branched on those,
    /// this takes the recovered branch and nothing else.
    /// </summary>
    public static class KartSimulation
    {
        /// <summary>Body dimensions are model inputs; suspension range defaults to 0.5.</summary>
        public static KartSimulationGeometry DefaultGeometry => new KartSimulationGeometry
        {
            HalfWidth = 1f,
            HalfLength = 1f,
            SuspensionRange = 0.5f,
            GroundedDragScale = 1f,
        };

        /// <summary>
        /// How far in from each corner a wheel ray is planted, as a fraction of
        /// the half extents.
        /// </summary>
        public const float WheelInset = 0.800000011920929f;

        /// <summary>Airborne spin damping, applied as a torque.</summary>
        public const float AirborneSpinDamping = -30f;

        /// <summary>
        /// The original iterates every overlapping triangle with no cap. A flat
        /// surface under the body can be dozens, so the buffer is sized to leave
        /// room for a wall behind them rather than to bound the real count.
        /// </summary>
        public const int MaxBodyContacts = 32;

        /// <summary>
        /// Shared scratch. The stepper is not re-entrant and never runs on more
        /// than one thread, which is what the original assumes too.
        /// </summary>
        private static readonly KartBodyContact[] BodyContacts =
            new KartBodyContact[MaxBodyContacts];

        public static void Init(
            KartSimulationState state,
            in KartDynamicsConfig config,
            in KartSimulationGeometry geometry)
        {
            state.Config = config;
            state.Geometry = geometry;

            state.Position = KartVec3.Zero;
            state.Orientation = KartQuat.Identity;
            state.LinearVelocity = KartVec3.Zero;
            state.AngularVelocity = KartVec3.Zero;
            state.Acceleration = KartVec3.Zero;

            state.Drift = default;
            state.Longitudinal = default;
            state.InstantBoost = default;
            state.TimedBoost = default;
            state.Jump = default;
            state.Wheels = default;
            state.LastStep = default;

            state.PreviousSteerAngleRad = 0f;
            state.GroundedDragScale = geometry.GroundedDragScale;
            state.PreviousForwardInput = false;
            state.PreviousDriftInput = false;
            state.PreviousBoostInput = false;
            state.Grounded = false;
        }

        /// <summary>
        /// Puts a different kart under a simulation that is already running.
        /// Everything the kart is doing — where it is, how fast, what it has
        /// stored, how far through the race — carries over; only what the kart
        /// <em>is</em> changes.
        ///
        /// Simulator-side: the original has no way to change kart mid-race, and
        /// the bench wants one so two karts can be compared through the same
        /// corner at the same speed rather than through two separate runs.
        ///
        /// The drag scale is the one field that cannot simply be copied.
        /// <see cref="MultiplyGroundedDragScale"/> writes the trigger's 4.0 or
        /// 0.25 into it at runtime, so the stored value is the kart's own scale
        /// times whatever the trigger has done; carrying the ratio across keeps a
        /// kart swapped inside a trigger volume from leaving with the multiplier
        /// silently reset.
        /// </summary>
        public static void Rekart(
            KartSimulationState state,
            in KartDynamicsConfig config,
            in KartSimulationGeometry geometry)
        {
            if (state == null) return;

            float previousBase = state.Geometry.GroundedDragScale;
            float trigger = previousBase != 0f ? state.GroundedDragScale / previousBase : 1f;

            state.Config = config;
            state.Geometry = geometry;
            state.GroundedDragScale = geometry.GroundedDragScale * trigger;

            // The wheel contacts belong to the old body's ray positions. They are
            // recast at the top of the next step, so they are left alone rather
            // than cleared — zeroing them would read as one frame airborne.
        }

        /// <summary>
        /// Original trigger callbacks 0x00441A10/0x00441AA0 multiply the runtime
        /// field by 4.0 on entry and 0.25 on exit.
        /// </summary>
        public static void MultiplyGroundedDragScale(KartSimulationState state, float multiplier)
            => state.GroundedDragScale *= multiplier;

        /// <summary>
        /// Casts the four wheel rays and turns what they hit into suspension
        /// contacts. Compression is measured along the body's up axis from the
        /// bottom of the suspension travel, and clamped to twice the range.
        /// </summary>
        public static KartWheelQueryOutput QueryWheelContacts(
            ref KartWheelContactState wheels,
            in KartVec3 position,
            in KartVec3 bodyRight,
            in KartVec3 bodyForward,
            in KartVec3 bodyUp,
            in KartSimulationGeometry geometry,
            IKartGroundQuery world)
        {
            var output = new KartWheelQueryOutput();

            for (int i = 0; i < KartConstants.WheelCount; ++i)
            {
                float oldCompression = wheels[i];
                float compression = 0f;

                KartVec3 start = position
                    + bodyRight * (geometry.HalfWidth * KartConstants.WheelRightSign[i] * WheelInset)
                    + bodyForward * (geometry.HalfLength * KartConstants.WheelForwardSign[i] * WheelInset)
                    + bodyUp * geometry.SuspensionRange;
                KartVec3 delta = bodyUp * (-2f * geometry.SuspensionRange);

                if (world != null && world.QueryGround(start, delta, out KartGroundHit hit))
                {
                    float bottomHeight =
                        KartVec3.Dot(position, bodyUp) - geometry.SuspensionRange;
                    float raw = KartVec3.Dot(hit.Point, bodyUp) - bottomHeight;
                    float maximum = 2f * geometry.SuspensionRange;

                    compression = MathF.Max(0f, MathF.Min(raw, maximum));

                    output[i] = new KartSuspensionContact
                    {
                        Active = true,
                        Normal = hit.Normal,
                        Compression = compression,
                        CompressionDelta = compression - oldCompression,
                    };
                    output.AverageNormal += hit.Normal;
                    output.SurfaceId = hit.SurfaceId;
                    output.ActiveContacts += 1;
                }
                wheels[i] = compression;
            }

            output.Grounded = output.ActiveContacts != 0u;
            output.LandedThisStep = output.Grounded && !wheels.Grounded;
            if (output.ActiveContacts != 0u)
            {
                output.AverageNormal *= 1f / output.ActiveContacts;
            }
            wheels.Grounded = output.Grounded;
            return output;
        }

        /// <summary>
        /// Advances the simulation by <paramref name="elapsedMs"/> milliseconds.
        /// </summary>
        public static KartSimulationStepResult SimulateMilliseconds(
            KartSimulationState state,
            in KartSimulationControls controls,
            IKartGroundQuery world,
            uint elapsedMs)
            => SimulateMilliseconds(state, controls, world, null, elapsedMs);

        /// <summary>
        /// Advances the simulation by <paramref name="elapsedMs"/> milliseconds,
        /// resolving body contacts against <paramref name="bodyWorld"/>.
        /// </summary>
        public static KartSimulationStepResult SimulateMilliseconds(
            KartSimulationState state,
            in KartSimulationControls controls,
            IKartGroundQuery world,
            IKartBodyCollisionQuery bodyWorld,
            uint elapsedMs)
        {
            var result = new KartSimulationStepResult();
            uint frameElapsedMs = elapsedMs;
            bool boostPressed = controls.BoostActive;

            // Item input caller at 0x00457ac0 first checks IsBoosting, then starts
            // a 3000 ms timed boost, so a held key cannot retrigger after expiry.
            // Only the timed boost blocks a new one: an item boost may start while
            // the instant boost is still running and the two overlap. The forward
            // multiplier does not stack either way.
            //
            // NoDelayBoost drops the press edge and nothing else, so a hold starts
            // the next boost as soon as the last expires. IsBoosting still gates
            // it, which is what keeps one boost running at a time.
            if (boostPressed &&
                (state.NoDelayBoost || !state.PreviousBoostInput) &&
                !state.TimedBoost.Active)
            {
                KartDynamics.TimedBoostStart(
                    ref state.TimedBoost, controls.ForwardInput, KartConstants.ItemBoostDurationMs);
            }
            state.PreviousBoostInput = boostPressed;

            while (elapsedMs != 0u)
            {
                uint stepMs = elapsedMs > KartConstants.MaxSubstepMs
                    ? KartConstants.MaxSubstepMs
                    : elapsedMs;

                SimulateSubstep(
                    state, controls, world, bodyWorld,
                    stepMs * KartConstants.FixedStepSecondsPerMs,
                    ref result);

                elapsedMs -= stepMs;
                result.Substeps += 1;
            }

            KartDynamics.TimedBoostStepMilliseconds(ref state.TimedBoost, frameElapsedMs);

            // Two simulator-side cutoff models, so they can be compared: the
            // default ends both boosts on throttle release, and the alternate
            // keeps them alive until reverse is pressed. Neither is recovered —
            // which of the two the 2004 engine used is the open question.
            // Whichever is selected is shared by the item and instant boosts.
            bool cutoff = state.ReverseInputEndsBoost
                ? controls.ReverseInput != 0f
                : controls.ForwardInput == 0f;

            if (cutoff)
            {
                if (state.TimedBoost.Active)
                {
                    state.TimedBoost.RemainingMs = 0u;
                    state.TimedBoost.Active = false;
                }
                if (state.InstantBoost.Active)
                {
                    state.InstantBoost.ActiveTimer = 0f;
                    state.InstantBoost.Active = false;
                }
            }

            result.Grounded = state.Grounded;
            state.LastStep = result;
            return result;
        }

        private static void SimulateSubstep(
            KartSimulationState state,
            in KartSimulationControls controls,
            IKartGroundQuery world,
            IKartBodyCollisionQuery bodyWorld,
            float dt,
            ref KartSimulationStepResult result)
        {
            // A fresh press of the accelerator is what spends an open instant
            // boost window, so the edge is detected before anything else.
            bool forwardPressed = controls.ForwardInput != 0f;
            if (forwardPressed && !state.PreviousForwardInput)
            {
                KartDynamics.InstantBoostPressForward(ref state.InstantBoost);
            }
            state.PreviousForwardInput = forwardPressed;
            KartDynamics.InstantBoostStepTimers(ref state.InstantBoost, dt);

            state.Orientation.GetAxes(
                out KartVec3 right, out KartVec3 forward, out KartVec3 up);
            float forwardVelocity = KartVec3.Dot(state.LinearVelocity, forward);
            float lateralVelocity = KartVec3.Dot(state.LinearVelocity, right);
            float speed = state.LinearVelocity.Magnitude;

            KartWheelQueryOutput wheel = QueryWheelContacts(
                ref state.Wheels, state.Position, right, forward, up, state.Geometry, world);

            state.Grounded = wheel.Grounded;
            result.WheelContacts += wheel.ActiveContacts;
            result.Landed = result.Landed || wheel.LandedThisStep;

            var force = KartVec3.Zero;
            var torque = KartVec3.Zero;

            if (wheel.Grounded)
            {
                var suspensionInput = new KartSuspensionInput
                {
                    Dt = dt,
                    HalfWidth = state.Geometry.HalfWidth,
                    HalfLength = state.Geometry.HalfLength,

                    // Read before the jump steps, which is where the C source
                    // reads it: the damping belongs to the phase the kart landed
                    // in, not to the one this substep is about to move it to.
                    CompressionDamping =
                        state.Jump.Phase == KartJumpPhase.Airborne ||
                        state.Jump.Phase == KartJumpPhase.Landing
                            ? state.Config.JumpLandingDamping
                            : 0f,
                    ChassisUp = up,
                };
                for (int i = 0; i < KartConstants.WheelCount; ++i)
                {
                    suspensionInput[i] = wheel[i];
                }

                KartSuspensionOutput suspension =
                    KartDynamics.ComputeSuspensionResponse(state.Config, suspensionInput);
                force += suspension.WorldForce;
                torque += suspension.LocalTorque;

                var longitudinalInput = new KartLongitudinalInput
                {
                    Velocity = state.LinearVelocity,
                    ForwardAxis = forward,
                    ForwardVelocity = forwardVelocity,
                    LateralVelocity = lateralVelocity,
                    Dt = dt,
                    ForwardInput = controls.ForwardInput,
                    ReverseInput = controls.ReverseInput,
                    DriveDisabled = controls.DriveDisabled,
                    DriftInputActive = state.Drift.InputActive,
                    DriftSlipDetected = state.Drift.SlipDetected,
                    BoostActive = KartDynamics.AnyBoostActive(state.TimedBoost, state.InstantBoost),
                };
                KartLongitudinalOutput longitudinal = KartDynamics.StepLongitudinal(
                    state.Config, ref state.Longitudinal, longitudinalInput);

                if (longitudinal.VelocityOverridden)
                {
                    state.LinearVelocity = longitudinal.Velocity;
                }
                force += longitudinal.Force;

                if (controls.DriftInput != state.PreviousDriftInput)
                {
                    KartDynamics.DriftSetInput(
                        ref state.Drift, controls.DriftInput, forwardVelocity);
                    state.PreviousDriftInput = controls.DriftInput;
                }

                bool wasDrifting = state.Drift.InputActive || state.Drift.SlipDetected;
                KartDynamics.DriftUpdateSlipDetection(
                    ref state.Drift, speed, forwardVelocity, lateralVelocity);

                KartLateralMode lateralMode;
                if (state.Drift.TriggerActive)
                {
                    lateralMode = KartLateralMode.DriftTrigger;
                    KartDynamics.DriftStepTrigger(ref state.Drift, state.Config, dt);
                }
                else if (state.Drift.InputActive || state.Drift.SlipDetected ||
                         state.Drift.LingerTimer > 0f)
                {
                    lateralMode = KartLateralMode.Drift;
                    KartDynamics.DriftStepLinger(ref state.Drift, dt);
                }
                else
                {
                    lateralMode = KartLateralMode.Grip;
                }

                var lateralInput = new KartLateralInput
                {
                    ForwardVelocity = forwardVelocity,
                    LateralVelocity = lateralVelocity,
                    YawLeverVelocity = state.AngularVelocity.Z,
                    SteeringInput = controls.SteeringInput,
                    ForwardInput = controls.ForwardInput,
                    PreviousSteerAngleRad = state.PreviousSteerAngleRad,
                    ReverseSteering = controls.ReverseSteering,
                    DriftInputActive = state.Drift.InputActive,
                    Mode = lateralMode,
                };
                KartLateralOutput lateral =
                    KartDynamics.ComputeLateralResponse(state.Config, lateralInput);

                KartDynamics.InstantBoostUpdateDriftExit(
                    ref state.InstantBoost, ref state.Drift, wasDrifting);

                state.PreviousSteerAngleRad = lateral.NextPreviousSteerAngleRad;
                force += right * lateral.LocalLateralForce;
                force += forward * lateral.LocalForwardForce;
                torque.Y += lateral.LocalRollTorque;
                torque.Z += lateral.LocalYawTorque;
            }
            else
            {
                state.Drift.InputActive = false;
                state.Drift.SlipDetected = false;
                state.Drift.TriggerActive = false;
                force.Z += KartConstants.WorldGravity * state.Config.Mass;
                torque += state.AngularVelocity * AirborneSpinDamping;
            }

            // Outside the grounded branch because it has to run either way: the
            // stroke ends when the wheels leave, and the airborne phase is what
            // watches for the landing.
            var jumpInput = new KartJumpInput
            {
                Dt = dt,
                JumpInput = controls.JumpInput,
                Geometry = state.Geometry,
                Right = right,
                Forward = forward,
                Up = up,
                LinearVelocity = state.LinearVelocity,
                Height = state.Position.Z,
                Wheel = wheel,
            };
            KartJumpOutput jump = KartDynamics.StepJump(ref state.Jump, state.Config, jumpInput);
            force += jump.Force;
            torque += jump.Torque;

            var dragInput = new KartDragInput
            {
                LinearVelocity = state.LinearVelocity,
                AngularVelocity = state.AngularVelocity,
                Grounded = wheel.Grounded,
                GroundedDragScale = state.GroundedDragScale,
            };
            KartDragOutput drag = KartDynamics.ComputeDragResponse(state.Config, dragInput);
            force += drag.Force;
            torque += drag.Torque;

            state.LinearVelocity = KartDynamics.IntegrateLinearVelocity(
                state.LinearVelocity, force, state.Config.Mass, dt);
            state.AngularVelocity = KartDynamics.IntegrateAngularVelocity(
                state.AngularVelocity,
                torque,
                KartMat3.DefaultInverseInertia(state.Config.Mass),
                dt);

            var poseInput = new KartPoseInput
            {
                Position = state.Position,
                Orientation = state.Orientation,
                LinearVelocity = state.LinearVelocity,
                AngularVelocity = state.AngularVelocity,
                Dt = dt,
            };
            KartPoseOutput pose = KartDynamics.IntegratePose(poseInput);
            state.Position = pose.Position;
            state.Orientation = pose.Orientation;
            state.AngularVelocity = pose.AngularVelocity;

            ResolveBodyContacts(state, bodyWorld, ref result);
        }

        /// <summary>
        /// The body pass runs after the pose has moved, which is where the
        /// original puts it: contacts are found against the position the kart
        /// just reached and resolved by editing velocity, not by pushing the kart
        /// back out.
        /// </summary>
        private static void ResolveBodyContacts(
            KartSimulationState state,
            IKartBodyCollisionQuery bodyWorld,
            ref KartSimulationStepResult result)
        {
            if (bodyWorld == null) return;

            int count = bodyWorld.QueryBodyCollisions(state, BodyContacts, MaxBodyContacts);
            if (count <= 0) return;
            if (count > MaxBodyContacts) count = MaxBodyContacts;

            state.Orientation.GetAxes(
                out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            for (int i = 0; i < count; ++i)
            {
                var input = new KartCollisionInput
                {
                    Velocity = state.LinearVelocity,
                    AngularVelocity = state.AngularVelocity,
                    Normal = BodyContacts[i].Normal,
                    BodyRight = right,
                    BodyForward = forward,
                    BodyUp = up,
                };
                KartCollisionOutput collision = KartDynamics.ResolveLinearCollision(input);

                state.LinearVelocity = collision.Velocity;
                state.AngularVelocity = collision.AngularVelocity;

                if (!collision.Incoming) continue;

                if (collision.WallContact)
                {
                    if (collision.NormalSpeed > result.WallImpactSpeed)
                    {
                        result.WallImpactSpeed = collision.NormalSpeed;
                    }
                }
                else if (collision.NormalSpeed > result.GroundImpactSpeed)
                {
                    result.GroundImpactSpeed = collision.NormalSpeed;
                }
            }

            result.BodyContacts += (uint)count;
        }
    }
}
