using System;

namespace OrangeCarrrrr.Core
{
    public enum KartGearMode
    {
        Single = 0,
        Multi = 1,
    }

    /// <summary>One gear's stretch of the engine note.</summary>
    public readonly struct KartGearBand
    {
        public KartGearBand(float upperSpeed, float lowPitch, float highPitch)
        {
            UpperSpeed = upperSpeed;
            LowPitch = lowPitch;
            HighPitch = highPitch;
        }

        /// <summary>Where this gear hands over.</summary>
        public float UpperSpeed { get; }

        /// <summary>The note just after engaging it.</summary>
        public float LowPitch { get; }

        /// <summary>The note just before leaving it.</summary>
        public float HighPitch { get; }
    }

    /// <summary>
    /// The gearbox, ported from <c>kart_gearbox.h</c>. A simulator-side
    /// experiment, <b>not</b> recovered code.
    ///
    /// The original has no gear or crankshaft of any kind: the engine note is one
    /// straight ramp in speed, and that is what <see cref="KartGearMode.Single"/>
    /// keeps, exactly as the recovered sound driver computes it. Nothing about the
    /// car's motion is affected by either mode — this drives the engine note and
    /// its dial and nothing else.
    ///
    /// <see cref="KartGearMode.Multi"/> is invented. It splits the same speed
    /// range into bands and runs the note from idle to redline inside each one, so
    /// the note is a sawtooth in speed rather than a single line: it drops on an
    /// upshift and jumps on a downshift. The band edges below are chosen to make
    /// that shape easy to watch, and are not claimed to be anything the original
    /// did.
    /// </summary>
    public sealed class KartGearbox
    {
        public const int GearCount = 4;

        /// <summary>
        /// Consecutive gears overlap, so an upshift drops the note only as far as
        /// the next gear's bottom rather than all the way to idle. Lower gears
        /// cover the widest pitch range, which is what makes the first shift the
        /// loudest step and the later ones progressively milder.
        /// </summary>
        public static readonly KartGearBand[] Bands =
        {
            new KartGearBand(40f, 0.25f, 1.10f),
            new KartGearBand(55f, 0.95f, 1.20f),
            new KartGearBand(70f, 1.10f, 1.30f),

            // Top gear: there is nothing to shift into, so this speed only sets
            // where its note stops climbing.
            new KartGearBand(128f, 1.20f, 1.50f),
        };

        /// <summary>
        /// A shift is fast but not instant: the note slews at this many pitch
        /// units per second, so a step of 0.15 takes about 30 ms. Steep enough to
        /// hear as a step, finite enough that the trace has a slope.
        /// </summary>
        public const float PitchSlew = 5f;

        /// <summary>
        /// Downshifts wait until the speed is this far below the band, so a kart
        /// sitting on an edge does not chatter between two gears.
        /// </summary>
        public const float DownshiftMargin = 4f;

        public KartGearMode Mode;

        /// <summary>1..<see cref="GearCount"/>, meaningful in multi.</summary>
        public int Gear = 1;

        /// <summary>The note this gear is producing, or 0 in single.</summary>
        public float Pitch;

        /// <summary>The <c>E</c> key.</summary>
        public void ToggleMode()
        {
            Mode = Mode == KartGearMode.Multi ? KartGearMode.Single : KartGearMode.Multi;
            Gear = 1;
        }

        /// <summary>The speed this gear engages at.</summary>
        public static float LowerSpeed(int gear)
            => gear <= 1 ? 0f : Bands[gear - 2].UpperSpeed;

        public void Step(float speed, float deltaSeconds)
        {
            Gear = Math.Clamp(Gear, 1, GearCount);

            if (Mode != KartGearMode.Multi)
            {
                Pitch = 0f;
                return;
            }

            while (Gear < GearCount && speed > Bands[Gear - 1].UpperSpeed) Gear += 1;
            while (Gear > 1 && speed < LowerSpeed(Gear) - DownshiftMargin) Gear -= 1;

            KartGearBand band = Bands[Gear - 1];
            float low = LowerSpeed(Gear);
            float high = band.UpperSpeed;

            float t = high > low ? (speed - low) / (high - low) : 0f;
            t = Math.Clamp(t, 0f, 1f);

            float target = band.LowPitch + (band.HighPitch - band.LowPitch) * t;

            // Within a gear the note moves slowly enough that the limit never
            // binds; it only shapes the step at a shift.
            if (Pitch <= 0f || deltaSeconds <= 0f)
            {
                Pitch = target;
                return;
            }

            float step = PitchSlew * deltaSeconds;
            if (target > Pitch + step) Pitch += step;
            else if (target < Pitch - step) Pitch -= step;
            else Pitch = target;
        }
    }
}
