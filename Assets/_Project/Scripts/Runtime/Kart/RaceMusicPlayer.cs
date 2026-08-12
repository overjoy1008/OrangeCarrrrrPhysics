using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The race's music, from the demo's own <c>sound_bgm_*.rho</c> archives.
    ///
    /// Each theme ships one or more tracks — <c>village_01</c>, <c>village_02</c>,
    /// <c>forest_01</c> and so on — and the main archive carries the three the race
    /// ends on: <c>game_end</c>, <c>game_result</c> and <c>game_win</c>.
    ///
    /// <b>What is recovered and what is not.</b> The files are the original's and
    /// the theme they belong to is in their names. Which of a theme's tracks the
    /// 2004 game picks for a given course, and whether the ending stinger is
    /// <c>game_end</c> or <c>game_win</c> in a time challenge, is not traced — this
    /// takes the first track of the theme and plays <c>game_end</c> at the finish,
    /// with <c>game_result</c> under the result panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceMusicPlayer : MonoBehaviour
    {
        public const float DefaultVolume = 0.45f;

        [SerializeField] private SimulatorRoot _simulator;

        [Tooltip("The theme loop. Left empty, it is resolved from the track's name.")]
        [SerializeField] private AudioClip _theme;

        [SerializeField] private AudioClip _finishStinger;
        [SerializeField] private AudioClip _resultLoop;

        [Range(0f, 1f)]
        [SerializeField] private float _volume = DefaultVolume;

        private AudioSource _source;
        private string _resolvedFor;

        private void Awake()
        {
            if (_simulator == null) _simulator = GetComponentInParent<SimulatorRoot>();
            if (_simulator == null) _simulator = FindFirstObjectByType<SimulatorRoot>();

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.volume = _volume;
        }

        private void OnEnable()
        {
            if (_simulator == null) return;
            _simulator.Finished += OnFinished;
            _simulator.ResultsShown += OnResults;
        }

        private void OnDisable()
        {
            if (_simulator == null) return;
            _simulator.Finished -= OnFinished;
            _simulator.ResultsShown -= OnResults;
        }

        private void Start()
        {
            ResolveClips();
            PlayTheme();
        }

        private void Update()
        {
            // The track can change under the player when a scene is loaded, and the
            // clip has to follow it.
            if (_simulator == null || _simulator.Track == null) return;
            if (_simulator.Track.AssetName == _resolvedFor) return;

            _theme = null;
            ResolveClips();
            PlayTheme();
        }

        private void PlayTheme()
        {
            if (_source == null || _theme == null) return;

            _source.clip = _theme;
            _source.loop = true;
            _source.volume = _volume;
            _source.Play();
        }

        private void OnFinished()
        {
            if (_source == null || _finishStinger == null) return;

            _source.clip = _finishStinger;
            _source.loop = false;
            _source.Play();
        }

        private void OnResults()
        {
            if (_source == null || _resultLoop == null) return;

            _source.clip = _resultLoop;
            _source.loop = true;
            _source.Play();
        }

        /// <summary>
        /// The theme a track belongs to, which is the first word of its asset name:
        /// <c>village_R01</c> is a village track. That is the demo's own naming, and
        /// it is the same word its BGM archive is named after.
        /// </summary>
        public static string ThemeOf(string trackAssetName)
        {
            if (string.IsNullOrEmpty(trackAssetName)) return null;

            int underscore = trackAssetName.IndexOf('_');
            return underscore <= 0 ? trackAssetName : trackAssetName.Substring(0, underscore);
        }

        private void ResolveClips()
        {
#if UNITY_EDITOR
            const string directory = "Assets/_Project/Audio/Music";

            if (_finishStinger == null)
            {
                _finishStinger = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"{directory}/game_end.ogg");
            }
            if (_resultLoop == null)
            {
                _resultLoop = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"{directory}/game_result.ogg");
            }

            if (_theme == null && _simulator != null && _simulator.Track != null)
            {
                string theme = ThemeOf(_simulator.Track.AssetName);
                _resolvedFor = _simulator.Track.AssetName;
                if (!string.IsNullOrEmpty(theme))
                {
                    _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                        $"{directory}/{theme}_01.ogg");
                }
            }
#endif
            if (_simulator != null && _simulator.Track != null)
            {
                _resolvedFor = _simulator.Track.AssetName;
            }
        }
    }
}
