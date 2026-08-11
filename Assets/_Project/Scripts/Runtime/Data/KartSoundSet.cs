using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// One engine preset's samples, plus the shared effect and countdown cues.
    ///
    /// The demo ships seven engine presets — classic, sr, z7, ht, jiu, x, v1 —
    /// each with a kart and a bike set of four slots. This holds one of them, so
    /// swapping preset later is swapping the asset rather than rewiring anything.
    ///
    /// Every original sample is 16-bit mono 22050 Hz PCM, which Unity imports
    /// without conversion.
    /// </summary>
    [CreateAssetMenu(
        fileName = "KartSoundSet",
        menuName = "OrangeCarrrrr/Kart Sound Set",
        order = 3)]
    public sealed class KartSoundSet : ScriptableObject
    {
        [Tooltip("Which sound_fx_engine folder these four slots came from.")]
        [SerializeField] private string _preset = "classic";

        [Header("Engine preset (sound_fx_engine/<preset>)")]
        [Tooltip("engine.wav — the looping motor, whose rate and volume follow the speed ramp.")]
        [SerializeField] private AudioClip _motor;

        [Tooltip("booster.wav — the 3000 ms item boost.")]
        [SerializeField] private AudioClip _booster;

        [Tooltip("boosterDrift.wav — the recovered instant boost off the end of a drift.")]
        [SerializeField] private AudioClip _instantBoost;

        [Tooltip("boosterPlay.wav — the loop held while revving on the start line.")]
        [SerializeField] private AudioClip _boosterIdle;

        [Header("Kart effects (sound_fx_kart)")]
        [SerializeField] private AudioClip _drift;
        [SerializeField] private AudioClip _crash;
        [SerializeField] private AudioClip _shock;

        [Header("Countdown (sound_fx_etc)")]
        [SerializeField] private AudioClip _countThree;
        [SerializeField] private AudioClip _countTwo;
        [SerializeField] private AudioClip _countOne;
        [SerializeField] private AudioClip _countGo;

        public string Preset => _preset;

        public AudioClip Motor => _motor;
        public AudioClip Booster => _booster;
        public AudioClip InstantBoost => _instantBoost;
        public AudioClip BoosterIdle => _boosterIdle;

        public AudioClip Drift => _drift;
        public AudioClip Crash => _crash;
        public AudioClip Shock => _shock;

        public AudioClip CountThree => _countThree;
        public AudioClip CountTwo => _countTwo;
        public AudioClip CountOne => _countOne;
        public AudioClip CountGo => _countGo;

#if UNITY_EDITOR
        /// <summary>Editor-only, for the builder to wire what the project imported.</summary>
        internal void SetClips(
            string preset,
            AudioClip motor, AudioClip booster, AudioClip instantBoost, AudioClip boosterIdle,
            AudioClip drift, AudioClip crash, AudioClip shock,
            AudioClip countThree, AudioClip countTwo, AudioClip countOne, AudioClip countGo)
        {
            _preset = preset;
            _motor = motor;
            _booster = booster;
            _instantBoost = instantBoost;
            _boosterIdle = boosterIdle;
            _drift = drift;
            _crash = crash;
            _shock = shock;
            _countThree = countThree;
            _countTwo = countTwo;
            _countOne = countOne;
            _countGo = countGo;
        }
#endif
    }
}
