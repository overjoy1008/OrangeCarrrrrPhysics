using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// <c>KartDynamicsConfig</c> from <c>kart_dynamics.h</c>, field for field and in
    /// the same order. Phase 1 only reads it (the HUD prints the force and drag
    /// constants); phase 2 drives the integrator from it.
    ///
    /// The jump block is carried even though jump is out of scope, so adding it
    /// later does not reshape the asset.
    /// </summary>
    [Serializable]
    public struct KartDynamicsConfig
    {
        public float Mass;
        public float AirFriction;
        public float DragFactor;
        public float ForwardAccelForce;
        public float BackwardAccelForce;
        public float GripBrakeForce;
        public float SlipBrakeForce;
        public float MaxSteerAngleDeg;
        public float SteerConstraint;
        public float FrontGripFactor;
        public float RearGripFactor;
        public float DriftTriggerFactor;
        public float DriftTriggerTime;
        public float DriftSlipFactor;
        public float DriftEscapeForce;
        public float CornerDrawFactor;
        public float DriftLeanFactor;
        public float SteerLeanFactor;

        // Active suspension jump. Out of scope for phases 1-2; kept so the
        // parameter asset does not change shape when it lands.
        public float JumpSpringMillionPerM;
        public float JumpMaxCrouchDistance;
        public float JumpGaugeSweepTime;
        public float JumpPushDuration;
        public float JumpMinEfficiency;
        public float JumpMaxEfficiency;
        public float JumpVelocityDirectionBias;
        public float JumpBodyUpBlend;
        public float JumpTorqueScale;
        public float JumpMaxSlopeDeg;
        public float JumpLandingCooldown;
        public float JumpLandingDamping;

        /// <summary>
        /// The tail every <c>DYNAMICS(...)</c> row in <c>kart_demo_data.c</c> shares.
        /// </summary>
        private void ApplySharedTail()
        {
            DriftLeanFactor = 0.07f;
            SteerLeanFactor = 0.01f;
            JumpSpringMillionPerM = 1.2f;
            JumpMaxCrouchDistance = 0.18f;
            JumpGaugeSweepTime = 0.75f;
            JumpPushDuration = 0.09f;
            JumpMinEfficiency = 0.20f;
            JumpMaxEfficiency = 1.0f;
            JumpVelocityDirectionBias = 0.12f;
            JumpBodyUpBlend = 0.25f;
            JumpTorqueScale = 0.02f;
            JumpMaxSlopeDeg = 45.0f;
            JumpLandingCooldown = 0.12f;
            JumpLandingDamping = 1200.0f;
        }

        /// <summary>The <c>DYNAMICS(...)</c> macro, argument for argument.</summary>
        public static KartDynamicsConfig Create(
            float mass,
            float airFriction,
            float dragFactor,
            float forwardAccelForce,
            float backwardAccelForce,
            float gripBrakeForce,
            float slipBrakeForce,
            float maxSteerAngleDeg,
            float steerConstraint,
            float frontGripFactor,
            float rearGripFactor,
            float driftTriggerFactor,
            float driftTriggerTime,
            float driftSlipFactor,
            float driftEscapeForce,
            float cornerDrawFactor)
        {
            var config = new KartDynamicsConfig
            {
                Mass = mass,
                AirFriction = airFriction,
                DragFactor = dragFactor,
                ForwardAccelForce = forwardAccelForce,
                BackwardAccelForce = backwardAccelForce,
                GripBrakeForce = gripBrakeForce,
                SlipBrakeForce = slipBrakeForce,
                MaxSteerAngleDeg = maxSteerAngleDeg,
                SteerConstraint = steerConstraint,
                FrontGripFactor = frontGripFactor,
                RearGripFactor = rearGripFactor,
                DriftTriggerFactor = driftTriggerFactor,
                DriftTriggerTime = driftTriggerTime,
                DriftSlipFactor = driftSlipFactor,
                DriftEscapeForce = driftEscapeForce,
                CornerDrawFactor = cornerDrawFactor,
            };
            config.ApplySharedTail();
            return config;
        }

        /// <summary>
        /// Recovered from 0x0042e190. The original loads these keys from the
        /// "Dynamics" section of a kart's parameter.xml and falls back to these
        /// when a key is absent, so this is the engine's own baseline rather than
        /// any particular kart.
        /// </summary>
        public static KartDynamicsConfig Default() => Create(
            100.0f, 3.0f, 0.5f, 3000.0f, 2000.0f, 2000.0f, 1500.0f,
            10.0f, 30.0f, 5.0f, 5.0f, 0.05f, 0.1f, 0.2f, 5000.0f, 0.0f);

        /// <summary>PRACTICE_DYNAMICS</summary>
        public static KartDynamicsConfig Practice() => Create(
            100.0f, 3.0f, 0.740f, 2000.0f, 1500.0f, 1800.0f, 1200.0f,
            10.0f, 22.0f, 5.0f, 5.0f, 0.2f, 0.2f, 0.2f, 1500.0f, 0.2f);

        /// <summary>STANDARD_DYNAMICS — what every cotten kart above cotten1 uses.</summary>
        public static KartDynamicsConfig Standard() => Create(
            100.0f, 3.0f, 0.725f, 3300.0f, 2000.0f, 2000.0f, 1500.0f,
            10.0f, 28.0f, 5.0f, 5.0f, 0.2f, 0.2f, 0.2f, 4000.0f, 0.05f);

        /// <summary>MARATHON_DYNAMICS</summary>
        public static KartDynamicsConfig Marathon() => Create(
            100.0f, 3.0f, 0.622f, 3000.0f, 2000.0f, 1500.0f, 1500.0f,
            10.0f, 26.0f, 5.0f, 5.0f, 0.2f, 0.2f, 0.2f, 4000.0f, 0.2f);

        /// <summary>SABER_DYNAMICS</summary>
        public static KartDynamicsConfig Saber() => Create(
            100.0f, 3.0f, 0.786f, 3550.0f, 2000.0f, 3000.0f, 1500.0f,
            10.0f, 29.0f, 5.0f, 5.0f, 0.2f, 0.2f, 0.2f, 4000.0f, 0.0f);

        /// <summary>SOLID_DYNAMICS</summary>
        public static KartDynamicsConfig Solid() => Create(
            100.0f, 3.0f, 0.855f, 3800.0f, 2000.0f, 2500.0f, 1500.0f,
            10.0f, 30.0f, 5.0f, 5.0f, 0.2f, 0.2f, 0.2f, 4000.0f, 0.0f);
    }
}
