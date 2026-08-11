using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>One update's worth of camera placement, all in the engine frame.</summary>
    public struct KartChaseCameraPose
    {
        public KartVec3 Position;
        public KartVec3 Right;
        public KartVec3 Up;
        public KartVec3 Forward;
        public float FieldOfViewDegrees;
    }

    /// <summary>
    /// Chase-camera orientation follow recovered from the original demo, ported
    /// from <c>kart_camera.c</c> without behavioural change.
    ///
    /// <c>ChaseCameraman::Update</c> is vftable slot 8 at <c>0x00444C30</c>. It does
    /// not read the kart's angular velocity: it samples the kart's current
    /// orientation each update and lets a persistent camera quaternion approach
    /// it, so a fast-turning kart stays visibly rotated relative to the view
    /// until the camera catches up.
    /// </summary>
    public class KartChaseCamera
    {
        // Follow time is pushed as an immediate at the call site:
        //   0x00444DAF  PUSH 0x43C80000   400.0f  Overhead Chase Cameraman
        //   0x00445244  PUSH 0x42C80000   100.0f  Front Chase Cameraman
        public const float FollowOverheadMs = 400.0f;
        public const float FollowFrontMs = 100.0f;

        // Speed filter, ChaseCameraman+0x24. Asymmetric: the geometry eases
        // outward slowly as speed rises and snaps back quickly as it falls.
        public const float SpeedRiseMs = 10000.0f;
        public const float SpeedFallMs = 100.0f;

        // Field of view, ChaseCameraman+0x34, in degrees.
        public const float FovNarrowDegrees = 75.0f;
        public const float FovWideDegrees = 110.0f;
        public const float FovNarrowMs = 1500.0f;
        public const float FovWideMs = 1000.0f;

        // Chase geometry constants, read from .rdata.
        public const float PitchBase = 0.25f;        // 0x005712C0
        public const float PitchDivisor = 400.0f;    // 0x00572680
        public const float DistanceBase = 5.5f;      // 0x00572684
        public const float DistanceTermA = 0.015f;   // 0x00572688
        public const float DistanceTermB = 0.03f;    // 0x0057267C
        public const float HeightDivisor = 60.0f;    // 0x00572678
        public const float HeightBase = 3.0f;        // 0x005722C0

        /// <summary>The final position's Z is smoothed on its own; X and Y are copied.</summary>
        public const float PositionZMs = 100.0f;

        private KartQuat _orientation = KartQuat.Identity;
        private float _filteredSpeed;
        private float _fieldOfView = FovNarrowDegrees;
        private float _previousPositionZ;
        private bool _initialized;

        /// <summary>
        /// ChaseCameraman+0x3C. Reset to zero; which code writes it is not traced,
        /// so it stays an input the caller may leave at zero.
        /// </summary>
        public float ExtraPitch;

        public KartQuat Orientation => _orientation;
        public float FilteredSpeed => _filteredSpeed;
        public float FieldOfViewDegrees => _fieldOfView;

        /// <summary>Makes the next follow step snap, as the original does after a reset.</summary>
        public void Reset()
        {
            _orientation = KartQuat.Identity;
            _filteredSpeed = 0f;
            // The reset path stores 0x42960000 into +0x34.
            _fieldOfView = FovNarrowDegrees;
            _previousPositionZ = 0f;
            ExtraPitch = 0f;
            _initialized = false;
        }

        /// <summary>0x00447510: alpha = min(elapsed / response, 1).</summary>
        private static float FollowAlpha(uint elapsedMs, float responseMs)
        {
            if (responseMs <= 0f) return 1f;
            float alpha = elapsedMs / responseMs;
            return alpha > 1f ? 1f : alpha;
        }

        /// <summary>
        /// 0x00482590. The two magic constants are read directly from .rdata:
        /// 0x005758F0 = 0.8227968811988831f, 0x005758F4 = 0.5854921936988831f.
        /// At k = 0 this is the identity, so nearly-aligned quaternions blend
        /// linearly; the wider the angle, the more the curve bends.
        /// </summary>
        public static float SlerpWeight(float t, float cosine)
        {
            float baseTerm = 1f - 0.8227968811988831f * cosine;
            float k = 0.5854921936988831f * baseTerm * baseTerm;
            return ((2f * t - 3f) * k * t + 1f + k) * t;
        }

        /// <summary>
        /// 0x00481CD0. The weight is evaluated on whichever half of the interval
        /// keeps t small, then the components are blended linearly and
        /// renormalized.
        ///
        /// The original normalizes with a fast reciprocal-square-root
        /// approximation (0x00482490). This uses the exact square root instead;
        /// that is a numerical difference of about 1e-7, not a behavioural one,
        /// and it is the only place this port departs from the recovered code.
        /// </summary>
        public static KartQuat Interpolate(KartQuat a, KartQuat b, float t)
        {
            float cosine = KartQuat.Dot(a, b);
            float weight = t > 0.5f
                ? 1f - SlerpWeight(1f - t, cosine)
                : SlerpWeight(t, cosine);

            var result = new KartQuat(
                a.W + (b.W - a.W) * weight,
                a.X + (b.X - a.X) * weight,
                a.Y + (b.Y - a.Y) * weight,
                a.Z + (b.Z - a.Z) * weight);

            float length = MathF.Sqrt(
                result.W * result.W + result.X * result.X +
                result.Y * result.Y + result.Z * result.Z);
            if (length > 0f)
            {
                float inverse = 1f / length;
                result.W *= inverse;
                result.X *= inverse;
                result.Y *= inverse;
                result.Z *= inverse;
            }
            return result;
        }

        /// <summary>One update of 0x00444C30's orientation path.</summary>
        public KartQuat Follow(KartQuat kartOrientation, uint elapsedMs, float followMs)
        {
            if (!_initialized || followMs <= 0f)
            {
                _orientation = kartOrientation;
                _initialized = true;
                return _orientation;
            }

            // q and -q are the same orientation. The original picks the same
            // hemisphere first so the blend takes the shorter arc. This is not an
            // angle limit.
            float cosine = KartQuat.Dot(_orientation, kartOrientation);
            if (cosine < 0f)
            {
                _orientation = -_orientation;
            }

            float alpha = elapsedMs / followMs;
            if (alpha > 1f) alpha = 1f;

            _orientation = Interpolate(_orientation, kartOrientation, alpha);
            return _orientation;
        }

        /// <summary>
        /// The whole mode-0 update of 0x00444C30: orientation follow, speed
        /// filter, FOV state, chase pitch/distance/height, and the Z-only
        /// position smoothing.
        /// </summary>
        public KartChaseCameraPose Update(
            KartVec3 kartPosition,
            KartQuat kartOrientation,
            float kartSpeed,
            bool wideView,
            uint elapsedMs,
            float followMs)
        {
            bool wasInitialized = _initialized;

            Follow(kartOrientation, elapsedMs, followMs);

            if (!wasInitialized)
            {
                // The reset branch stores the sampled values straight through
                // instead of filtering them.
                _filteredSpeed = kartSpeed;
                _fieldOfView = FovNarrowDegrees;
                ExtraPitch = 0f;
            }
            else
            {
                // Rising speed eases out over 10 s; falling speed pulls in over 100 ms.
                float speedBeta = FollowAlpha(
                    elapsedMs, _filteredSpeed <= kartSpeed ? SpeedRiseMs : SpeedFallMs);
                float fovTarget = wideView ? FovWideDegrees : FovNarrowDegrees;
                float fovGamma = FollowAlpha(elapsedMs, wideView ? FovWideMs : FovNarrowMs);

                _filteredSpeed = kartSpeed * speedBeta + (1f - speedBeta) * _filteredSpeed;
                _fieldOfView = fovTarget * fovGamma + (1f - fovGamma) * _fieldOfView;
            }

            float filtered = _filteredSpeed;
            _orientation.GetAxes(out KartVec3 right, out KartVec3 forward, out KartVec3 up);

            // The pitch input is clamped at zero by 0x0042AFA0 before the divide.
            float pitch = (filtered > 0f ? filtered : 0f) / PitchDivisor + PitchBase + ExtraPitch;
            float pitchCos = MathF.Cos(pitch);
            float pitchSin = MathF.Sin(pitch);

            float distance = filtered * DistanceTermA + DistanceBase;
            distance = filtered * DistanceTermB + distance;
            if (distance < DistanceBase) distance = DistanceBase;

            float height = filtered / HeightDivisor + HeightBase;

            // out_orientation = camera_rotation * RotationX(pitch) (0x0042B370 with
            // 0x0047EE40). Taking that product's columns rotates forward and up in
            // their own plane and leaves right alone, so a positive pitch tilts the
            // view down toward the kart.
            var pose = new KartChaseCameraPose
            {
                Right = right,
                Forward = new KartVec3(
                    forward.X * pitchCos - up.X * pitchSin,
                    forward.Y * pitchCos - up.Y * pitchSin,
                    forward.Z * pitchCos - up.Z * pitchSin),
                Up = new KartVec3(
                    forward.X * pitchSin + up.X * pitchCos,
                    forward.Y * pitchSin + up.Y * pitchCos,
                    forward.Z * pitchSin + up.Z * pitchCos),
            };

            // position = kart - forward * distance + up * height, using the
            // unpitched axes, which is the order 0x00444C30 builds it in.
            pose.Position = new KartVec3(
                kartPosition.X - forward.X * distance + up.X * height,
                kartPosition.Y - forward.Y * distance + up.Y * height,
                kartPosition.Z - forward.Z * distance + up.Z * height);

            // Only Z is smoothed, and only once the camera has a previous frame.
            if (wasInitialized)
            {
                float zAlpha = FollowAlpha(elapsedMs, PositionZMs);
                pose.Position.Z = pose.Position.Z * zAlpha + (1f - zAlpha) * _previousPositionZ;
            }
            _previousPositionZ = pose.Position.Z;

            pose.FieldOfViewDegrees = _fieldOfView;
            return pose;
        }

        /// <summary>
        /// Focal length in pixels for a viewport height, matching the original's
        /// projection which takes tan(fov/2) at 0x004A98B1. The stored value is a
        /// full vertical angle in degrees, which is exactly what Unity's
        /// <c>Camera.fieldOfView</c> means, so the port sets that directly and only
        /// needs this for the software-projection tests.
        /// </summary>
        public static float FocalLength(float fieldOfViewDegrees, float height)
        {
            float half = fieldOfViewDegrees * 0.00872664f;
            float tangent = MathF.Tan(half);
            if (tangent <= 0f) return height;
            return height * 0.5f / tangent;
        }
    }
}
