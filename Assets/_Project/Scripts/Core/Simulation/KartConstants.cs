namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Constants read from KartRider.exe .rdata at 0x00571d20-0x00571d38, plus
    /// the fixed-step figures the simulator loop uses.
    ///
    /// The two gravity values are not interchangeable. The world pulls the body
    /// down at -58.8, while the tyre model scales its forces by 9.8: rescaling
    /// the world to metres would break the ratio between them, which is why the
    /// port keeps the original's units end to end.
    /// </summary>
    public static class KartConstants
    {
        public const float Pi = 3.1415927410125732f;
        public const float DegreesPerHalfTurn = 180.0f;
        public const float Gravity = 9.800000190734863f;
        public const float WorldGravity = -58.79999923706055f;

        public const float LowSpeedDenominator = 5.0f;
        public const float SteerActiveSpeed = 0.5f;
        public const float LeanSpeedThreshold = 10.0f;

        public const float SuspensionReboundRatio = 0.20000000298023224f;
        public const float SuspensionTorqueScale = 0.1f;

        /// <summary>The stepper's own step size: 5 ms substeps.</summary>
        public const uint MaxSubstepMs = 5u;

        public const float FixedStepSecondsPerMs = 0.0010000000474974513f;

        /// <summary>The item boost the original's 0x00457ac0 starts.</summary>
        public const uint ItemBoostDurationMs = 3000u;

        public const int WheelCount = 4;

        /// <summary>Corner signs for wheel 0..3, as the original orders them.</summary>
        public static readonly float[] WheelRightSign = { 1f, -1f, 1f, -1f };
        public static readonly float[] WheelForwardSign = { 1f, 1f, -1f, -1f };
    }
}
