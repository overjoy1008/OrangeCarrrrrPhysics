using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The handful of scalar conversions the HUD reads, ported from
    /// <c>kart_dynamics.c</c> so the readouts match the original bit for bit.
    /// </summary>
    public static class KartUnits
    {
        public const float DegreesPerRadian = 180f / 3.14159265358979323846f;

        /// <summary>HUD path 0x00450ABD-0x00450AC6: |linear velocity| * 3.6f.</summary>
        public static float SpeedKmh(KartVec3 linearVelocity)
        {
            float speed = MathF.Sqrt(KartVec3.Dot(linearVelocity, linearVelocity));
            return speed * 3.5999999046325684f;
        }

        /// <summary>
        /// The original sends the float value to the gauge and its x87 integer
        /// conversion to the L"%03d" numeric display.
        /// </summary>
        public static int SpeedometerKmh(KartVec3 linearVelocity)
            => (int)MathF.Round(SpeedKmh(linearVelocity), MidpointRounding.ToEven);

        /// <summary>
        /// The HUD's slip angle: atan2(|lateral|, |forward|) in degrees.
        /// </summary>
        public static float SlipAngleDegrees(float forwardSpeed, float lateralSpeed)
            => MathF.Atan2(MathF.Abs(lateralSpeed), MathF.Abs(forwardSpeed)) * DegreesPerRadian;
    }
}
