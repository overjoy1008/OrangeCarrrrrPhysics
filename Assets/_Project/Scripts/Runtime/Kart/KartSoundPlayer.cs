using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Plays what <see cref="KartSoundDriver"/> decides, the way
    /// <c>kart_sound_win32.h</c> joins the recovered driver to the mixer.
    ///
    /// The C build needs a streaming mixer over waveOut because it refuses to
    /// depend on anything the OS does not already provide; here an
    /// <c>AudioSource</c> per voice does the same job, and Unity's <c>pitch</c> is
    /// the same playback-rate change waveOut's resampler was doing.
    ///
    /// Everything is 2D (<c>spatialBlend = 0</c>) because the original mixer has
    /// no positional model at all. Making it 3D would sound better and would stop
    /// being a reproduction.
    ///
    /// The motor voice is opened once at volume 0 and only ever has its volume and
    /// rate changed, which is what the original does — restarting it would clip
    /// the loop every time the note moved.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class KartSoundPlayer : MonoBehaviour
    {
        [Tooltip("The preset to play. Without one the simulator runs silent.")]
        [SerializeField] private KartSoundSet _sounds;

        [SerializeField, Range(0f, 1f)] private float _masterVolume = 1f;

        private AudioSource _motor;
        private AudioSource _drift;
        private AudioSource _boosterIdle;
        private AudioSource _booster;
        private AudioSource _instantBoost;
        private AudioSource _impact;
        private AudioSource _countdown;

        /// <summary>
        /// The final lap's cue.
        ///
        /// It lives on the player rather than in a <see cref="KartSoundSet"/>
        /// because it is not part of an engine preset: the original opens the
        /// <c>etc</c> archive and plays <c>ufo_lab</c> whatever kart is being driven.
        /// </summary>
        private AudioSource _race;

        [Tooltip("The final lap cue, sound_fx_etc's ufo_lab. Left empty, it is resolved from the project.")]
        [SerializeField] private AudioClip _finalLap;

        private KartSoundDriver _driver;
        private uint _instantBoostActivations;
        private bool _started;

        /// <summary>
        /// The note the driver is holding, for the tachometer. It only recomputes
        /// every 64 ms, and the dial is meant to show exactly that.
        /// </summary>
        public float MotorPitch { get; private set; } = KartSoundConstants.MotorBase;

        public float MotorVolume { get; private set; }

        public KartSoundSet Sounds
        {
            get => _sounds;
            set { _sounds = value; Restart(); }
        }

        /// <summary>
        /// A booster one-shot that replaces the set's for as long as it is set.
        ///
        /// Only the booster, and only the sample: the volume, the voice it plays
        /// on and the rule that a boost ending cuts it are all unchanged. Set from
        /// the kart's own spec, so it follows the kart rather than the engine
        /// preset — which is the point, since a guest borrows a preset it is not
        /// really part of.
        /// </summary>
        public AudioClip BoosterOverride { get; set; }

        /// <summary>
        /// Seconds to skip into <see cref="BoosterOverride"/>.
        ///
        /// A boost is a short thing and the sample may not start at its first
        /// sample: the OIIA booster carries 1.48 s of digital silence before the
        /// voice. Trimming the file would work too, but the offset keeps the asset
        /// as its author uploaded it, which is what the licence asks of the model
        /// beside it. Ignored for the engine sets, whose samples start where they
        /// should.
        /// </summary>
        public float BoosterOverrideStart { get; set; }

        /// <summary>
        /// A second take in the same clip, or 0 for none. An item boost picks
        /// between this and <see cref="BoosterOverrideStart"/> at random.
        /// </summary>
        public float BoosterOverrideSlowStart { get; set; }

        /// <summary>
        /// Which take the booster now playing took. Read by the simulator so the
        /// kart's own effects can follow the sound — the OIIA cat turns at half
        /// speed while its slow take is running.
        /// </summary>
        public bool BoosterSlow { get; private set; }

        /// <summary>
        /// Makes the next booster take the fast one whatever the dice say.
        ///
        /// The start boost is not a moment to be slow at, and it goes through the
        /// same timed boost an item does, so it says so here rather than being
        /// told apart afterwards. Consumed by the next booster and then forgotten.
        /// </summary>
        public void ForceNextBoosterFast() => _forceFastBooster = true;

        private bool _forceFastBooster;

        private AudioClip BoosterClip =>
            BoosterOverride != null ? BoosterOverride : _sounds.Booster;

        /// <summary>
        /// The short booster borrows the kart's own booster sample when it has
        /// one. A kart that says something when it boosts should say it whichever
        /// boost fired, and the set's instant sample under a cat's booster sounded
        /// like two different karts.
        /// </summary>
        private AudioClip InstantBoostClip =>
            BoosterOverride != null ? BoosterOverride : _sounds.InstantBoost;

        private float InstantBoostStart => BoosterOverride != null ? BoosterOverrideStart : 0f;

        /// <summary>
        /// Where the booster one-shot should start, and which take that is.
        ///
        /// Called once per booster rather than per frame: it rolls the dice.
        /// </summary>
        private float TakeBoosterStart()
        {
            bool forced = _forceFastBooster;
            _forceFastBooster = false;

            if (BoosterOverride == null)
            {
                BoosterSlow = false;
                return 0f;
            }

            BoosterSlow =
                !forced && BoosterOverrideSlowStart > 0f && Random.value < 0.5f;

            return BoosterSlow ? BoosterOverrideSlowStart : BoosterOverrideStart;
        }

        private const string DefaultSetPath = "Assets/_Project/Data/Audio/Classic.asset";

        private void OnEnable() => Restart();

        private void OnDisable() => StopAll();

#if UNITY_EDITOR
        /// <summary>
        /// Picks up the classic preset the first time the component is seen in the
        /// editor, so the reference is serialised into the scene and a build has
        /// sound without anyone having had to drag the asset in.
        /// </summary>
        private void OnValidate()
        {
            if (_sounds != null) return;
            _sounds = UnityEditor.AssetDatabase.LoadAssetAtPath<KartSoundSet>(DefaultSetPath);
            if (_sounds != null) UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Resolves the preset for an instance created at run time, which never
        /// went through the editor and so has nothing serialised.
        /// </summary>
        private void ResolveDefaultSet()
        {
#if UNITY_EDITOR
            if (_sounds == null)
            {
                _sounds = UnityEditor.AssetDatabase.LoadAssetAtPath<KartSoundSet>(DefaultSetPath);
            }
#endif
        }

        /// <summary>Rebuilds the voices and re-opens the motor loop.</summary>
        public void Restart()
        {
            _driver.Reset();
            _instantBoostActivations = 0u;
            ResolveDefaultSet();

            // Edit mode gets the components so the inspector shows them, but no
            // sound: the simulation is parked and a held engine loop would just
            // drone in the editor.
            if (!Application.isPlaying)
            {
                _started = false;
                return;
            }

            EnsureListener();
            EnsureVoices();
            StopAll();

            if (_sounds != null && _sounds.Motor != null)
            {
                _motor.clip = _sounds.Motor;
                _motor.volume = 0f;
                _motor.pitch = KartSoundConstants.MotorBase;
                _motor.Play();
            }
            _started = true;
        }

        /// <summary>
        /// Guarantees the one listener Unity needs to hear anything.
        ///
        /// It goes on this object rather than on a camera because the simulator
        /// has two of them and only ever leaves one enabled — a listener on each
        /// would be right half the time and a duplicate-listener warning the rest.
        /// Nothing is lost by keeping it here: every voice is 2D, exactly as the
        /// original's mixer is, so the listener's position never enters into it.
        /// </summary>
        private void EnsureListener()
        {
            var existing = FindFirstObjectByType<AudioListener>(FindObjectsInactive.Exclude);
            if (existing != null) return;

            gameObject.AddComponent<AudioListener>();
        }

        private void EnsureVoices()
        {
            if (_motor == null) _motor = AddVoice("Motor", loop: true);
            if (_drift == null) _drift = AddVoice("Drift", loop: true);
            if (_boosterIdle == null) _boosterIdle = AddVoice("Booster idle", loop: true);
            if (_booster == null) _booster = AddVoice("Booster", loop: false);
            if (_instantBoost == null) _instantBoost = AddVoice("Instant boost", loop: false);
            if (_impact == null) _impact = AddVoice("Impact", loop: false);
            if (_countdown == null) _countdown = AddVoice("Countdown", loop: false);
            if (_race == null) _race = AddVoice("Race", loop: false);
        }

        private AudioSource AddVoice(string label, bool loop)
        {
            var holder = new GameObject(label) { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(transform, worldPositionStays: false);

            var source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }

        private void StopAll()
        {
            Stop(_motor); Stop(_drift); Stop(_boosterIdle);
            Stop(_booster); Stop(_instantBoost); Stop(_impact); Stop(_countdown);
        }

        private static void Stop(AudioSource source)
        {
            if (source != null && source.isPlaying) source.Stop();
        }

        /// <summary>
        /// Re-pitches the engine loop the driver already opened, leaving its
        /// volume alone.
        ///
        /// This is how the gearbox reaches the sound: <c>Single</c> never calls
        /// it, so the recovered note is untouched, and <c>Multi</c> replaces the
        /// pitch with its own sawtooth. Applied after <see cref="Step"/>, which is
        /// the order the original uses — the driver computes the note first and
        /// the gearbox overwrites it.
        /// </summary>
        public void OverrideMotorPitch(float pitch)
        {
            if (!_started) return;

            MotorPitch = pitch;
            if (_motor != null) _motor.pitch = pitch;
        }

        /// <summary>
        /// One frame, driven by the same millisecond clock the simulation uses so
        /// the 64 ms motor interval lands where the original puts it.
        /// </summary>
        public void Step(KartSimulationState kart, uint nowMs)
        {
            if (!_started || _sounds == null || kart == null) return;

            // The linger timer counts as drifting here for the same reason the
            // skid marks and the HUD treat it that way: without it the loop cut
            // out in the grip phase between the trigger and the slip, then
            // restarted.
            bool driftActive = SkidMarkTrail.DriftVisualActive(kart);

            KartSoundState state = _driver.Update(
                kart.LinearVelocity.Magnitude,
                driftActive,
                kart.TimedBoost.Active,
                kart.InstantBoost.Active,
                kart.LastStep.WallImpactSpeed,
                kart.LastStep.GroundImpactSpeed,
                nowMs);

            MotorPitch = state.MotorPitch;
            MotorVolume = state.MotorVolume;

            if (_motor != null)
            {
                _motor.volume = state.MotorVolume * _masterVolume;
                _motor.pitch = state.MotorPitch;
            }

            Loop(_drift, _sounds.Drift, state.DriftLooping);

            // The booster sample belongs to the state: a boost that ends early
            // takes it with it rather than playing on over a kart that has
            // already slowed down.
            if (state.StartBooster) PlayOnce(_booster, BoosterClip, 1f, TakeBoosterStart());
            else if (!kart.TimedBoost.Active) Stop(_booster);

            // The instant boost can retrigger inside its own window, which the
            // activation counter is there to catch — a rising edge alone would
            // miss the second one.
            bool retriggered = kart.InstantBoost.Active &&
                               kart.InstantBoost.ActivationCount != _instantBoostActivations;
            if (state.StartInstantBoost || retriggered)
            {
                // Always the fast take. The short booster is a flick out of a
                // drift and it is over before the slow one has finished its first
                // syllable.
                PlayOnce(_instantBoost, InstantBoostClip, 1f, InstantBoostStart);
                if (BoosterOverride != null) BoosterSlow = false;
            }
            else if (!kart.InstantBoost.Active)
            {
                Stop(_instantBoost);
            }
            _instantBoostActivations = kart.InstantBoost.ActivationCount;

            // The original's mode 0xE guard: a second hit while the first is
            // still sounding is dropped rather than restarting it.
            if (state.StartCrash) PlayGuarded(_impact, _sounds.Crash, state.CrashVolume);
            if (state.StartShock) PlayGuarded(_impact, _sounds.Shock, state.ShockVolume);
        }

        /// <summary>The loop held while the player revs on the start line.</summary>
        public void SetBoosterIdle(bool on)
        {
            if (!_started || _sounds == null) return;
            Loop(_boosterIdle, _sounds.BoosterIdle, on);
        }

        /// <summary>
        /// The final lap cue, from <c>0x00456...</c>'s lap branch: when the kart's
        /// lap counter reaches the course's lap count the stage opens the
        /// <c>etc</c> sound archive and plays <c>ufo_lab</c>, then runs the
        /// <c>finallap</c> action.
        /// </summary>
        public void PlayFinalLap()
        {
            if (!_started) return;
            PlayOnce(_race, ResolveFinalLap(), 1f);
        }

        private AudioClip ResolveFinalLap()
        {
#if UNITY_EDITOR
            if (_finalLap == null)
            {
                _finalLap = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/_Project/Audio/Race/ufo_lab.wav");
            }
#endif
            return _finalLap;
        }

        /// <summary>The 3 / 2 / 1 / GO cues.</summary>
        public void PlayCountdown(in KartCountdownCues cues)
        {
            if (!_started || _sounds == null) return;

            if (cues.PlayThree) PlayOnce(_countdown, _sounds.CountThree, 1f);
            if (cues.PlayTwo) PlayOnce(_countdown, _sounds.CountTwo, 1f);
            if (cues.PlayOne) PlayOnce(_countdown, _sounds.CountOne, 1f);
            if (cues.PlayGo) PlayOnce(_countdown, _sounds.CountGo, 1f);
        }

        private void Loop(AudioSource source, AudioClip clip, bool on)
        {
            if (source == null) return;

            if (on)
            {
                if (clip == null || source.isPlaying) return;
                source.clip = clip;
                source.volume = _masterVolume;
                source.pitch = 1f;
                source.Play();
            }
            else
            {
                Stop(source);
            }
        }

        private void PlayOnce(AudioSource source, AudioClip clip, float volume, float startTime = 0f)
        {
            if (source == null || clip == null) return;
            source.clip = clip;
            source.volume = volume * _masterVolume;
            source.pitch = 1f;
            source.time = startTime > 0f && startTime < clip.length ? startTime : 0f;
            source.Play();
        }

        private void PlayGuarded(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null || source.isPlaying) return;
            PlayOnce(source, clip, volume);
        }
    }
}
