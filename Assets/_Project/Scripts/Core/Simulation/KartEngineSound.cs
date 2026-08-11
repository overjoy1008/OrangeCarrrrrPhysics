namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The kart's sound laws, recovered from <c>FUN_00452E60</c> and
    /// <c>FUN_00458000</c>. Playback is the platform's problem; this only decides
    /// what should be true.
    ///
    /// The engine note is one straight ramp in speed — no gears, no crankshaft —
    /// and it is only recomputed every 64 ms, which is what gives the original its
    /// stepped engine note rather than a smooth sweep. Reproducing the interval is
    /// as much a part of the sound as the ramp itself.
    /// </summary>
    public static class KartSoundConstants
    {
        /// <summary><c>0x00452E60</c> only recomputes the note this often.</summary>
        public const uint MotorIntervalMs = 64u;

        /// <summary>Pitch and volume share one ramp: <c>speed * slope + base</c>.</summary>
        public const float MotorSlope = 0.01171875f;   // 0x005730D0
        public const float MotorBase = 0.25f;          // 0x005712C0

        /// <summary>Above these speeds each output is pinned instead of ramping.</summary>
        public const float MotorPitchSpeed = 128.0f;   // 0x005730D4
        public const float MotorPitchCap = 1.5f;
        public const float MotorVolumeSpeed = 64.0f;   // 0x005730CC
        public const float MotorVolumeCap = 1.0f;

        /// <summary>Impact volume is <c>clamp(magnitude * scale, 0.1, 1.0)</c>.</summary>
        public const float CrashScale = 0.1f;          // 0x00571D44
        public const float ShockScale = 0.04f;         // 0x005730C8
        public const float ImpactMinVolume = 0.1f;
        public const float ImpactMaxVolume = 1.0f;
    }

    /// <summary>What the platform layer should make true after an update.</summary>
    public struct KartSoundState
    {
        /// <summary>Held between updates; only refreshed every 64 ms.</summary>
        public float MotorPitch;
        public float MotorVolume;

        /// <summary>The drift loop runs exactly while the kart's drift flag is set.</summary>
        public bool DriftLooping;

        /// <summary>Edges — true for the single update that should start the one-shot.</summary>
        public bool StartBooster;
        public bool StartInstantBoost;
        public bool StartCrash;
        public bool StartShock;

        public float CrashVolume;
        public float ShockVolume;
    }

    /// <summary><c>KartSoundDriver</c> from <c>kart_engine_sound.h</c>.</summary>
    public struct KartSoundDriver
    {
        private uint _previousMotorMs;
        private float _motorPitch;
        private float _motorVolume;
        private bool _boosterActive;
        private bool _instantBoostActive;
        private bool _initialized;

        public void Reset()
        {
            _previousMotorMs = 0u;
            // The engine loop is opened at volume 0 and only rises once the
            // first 64 ms refresh runs.
            _motorPitch = KartSoundConstants.MotorBase;
            _motorVolume = 0f;
            _boosterActive = false;
            _instantBoostActive = false;
            _initialized = false;
        }

        /// <summary>
        /// One frame of sound. <paramref name="speed"/> is the magnitude of the
        /// kart's linear velocity, as the original reads it from
        /// <c>kart+0x5C</c>; the impact magnitudes are zero when nothing was hit.
        ///
        /// The item boost and the instant boost are tracked apart so each can
        /// have its own sample — the original drives one booster sound from a
        /// single vftable flag that covers both.
        /// </summary>
        public KartSoundState Update(
            float speed,
            bool driftActive,
            bool boostActive,
            bool instantBoostActive,
            float crashMagnitude,
            float shockMagnitude,
            uint nowMs)
        {
            var state = new KartSoundState();

            if (!_initialized)
            {
                _previousMotorMs = nowMs;
                _initialized = true;
            }

            // 0x004537A5: if (0x40 < now - previous) refresh, otherwise hold.
            if (nowMs - _previousMotorMs > KartSoundConstants.MotorIntervalMs)
            {
                float ramp = speed * KartSoundConstants.MotorSlope + KartSoundConstants.MotorBase;
                _previousMotorMs = nowMs;
                _motorPitch = speed >= KartSoundConstants.MotorPitchSpeed
                    ? KartSoundConstants.MotorPitchCap
                    : ramp;
                _motorVolume = speed >= KartSoundConstants.MotorVolumeSpeed
                    ? KartSoundConstants.MotorVolumeCap
                    : ramp;
            }

            state.MotorPitch = _motorPitch;
            state.MotorVolume = _motorVolume;

            // The drift loop is edge driven: opened when the flag rises, stopped
            // when it falls. Holding the flag does not reopen it.
            state.DriftLooping = driftActive;

            // The booster is level triggered every frame in the original; its
            // mode 0xC single-instance guard is what stops it restarting.
            // Reproducing that as a rising edge gives the same audible result
            // without needing the guard.
            state.StartBooster = boostActive && !_boosterActive;
            _boosterActive = boostActive;

            state.StartInstantBoost = instantBoostActive && !_instantBoostActive;
            _instantBoostActive = instantBoostActive;

            if (crashMagnitude > 0f)
            {
                state.StartCrash = true;
                state.CrashVolume = ImpactVolume(crashMagnitude, KartSoundConstants.CrashScale);
            }
            if (shockMagnitude > 0f)
            {
                state.StartShock = true;
                state.ShockVolume = ImpactVolume(shockMagnitude, KartSoundConstants.ShockScale);
            }

            return state;
        }

        /// <summary>0x0042AFA0 then 0x00431EA0: max then min.</summary>
        public static float ImpactVolume(float magnitude, float scale)
        {
            float volume = magnitude * scale;
            if (volume < KartSoundConstants.ImpactMinVolume) volume = KartSoundConstants.ImpactMinVolume;
            if (volume > KartSoundConstants.ImpactMaxVolume) volume = KartSoundConstants.ImpactMaxVolume;
            return volume;
        }
    }
}
