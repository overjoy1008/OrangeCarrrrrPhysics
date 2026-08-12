using System.Collections.Generic;
using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The race's music, from the demo's own <c>sound_bgm_*.rho</c> archives.
    ///
    /// Each theme ships one or more tracks — <c>village_01</c>, <c>village_02</c>,
    /// <c>forest_01</c> and so on. At the finish the theme stops and
    /// <c>game_end</c> plays once; nothing follows it.
    ///
    /// <b>What is recovered and what is not.</b> The files are the original's and
    /// the theme they belong to is in their names. Which of a theme's tracks the
    /// 2004 game picks for a given course is not traced, so this walks them in a
    /// fixed order instead: the theme's remix first, then its numbered tracks in
    /// order, then back to the remix. The position belongs to the theme rather
    /// than to the track, so racing a village course, replaying it and then
    /// picking another village course plays three different tunes in sequence,
    /// while moving to another theme starts that theme at its own remix.
    /// The demo's main archive also carries <c>game_result</c>, <c>game_win</c> and
    /// <c>game_lose</c> — the time challenge plays the last of those when the
    /// target time is missed — but this port deliberately ends on <c>game_end</c>
    /// alone.
    ///
    /// <b>Two sources.</b> <c>Audio/Music</c> holds the 2004 demo's own files and
    /// <c>Audio/Music/TCGames</c> the later client's, kept apart because the two
    /// ship different recordings under the same names — the demo's
    /// <c>village_01.ogg</c> and the TC Games one are not the same track.
    ///
    /// The race's music is drawn from the later set for every theme, which is a
    /// deliberate choice rather than a fallback: it covers all six themes where the
    /// demo covers four, and mixing the two would have some courses playing a 2004
    /// recording and others a later one. The demo's own files stay in the project
    /// and are still what <c>game_end</c> comes from.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceMusicPlayer : MonoBehaviour
    {
        public const float DefaultVolume = 0.45f;

        /// <summary>
        /// The subdirectory the later client's music lives in.
        ///
        /// Kept separate on disk rather than merged, because these are not the
        /// demo's files and the recovery has to stay able to tell the two apart.
        /// </summary>
        public const string TCGamesFolder = "TCGames";

        [SerializeField] private SimulatorRoot _simulator;

        [Tooltip("The theme loop now playing, drawn from the pool below.")]
        [SerializeField] private AudioClip _theme;

        [Tooltip("Every track of the current theme, remix first then numbered.")]
        [SerializeField] private List<AudioClip> _themePool = new List<AudioClip>();

        [Tooltip("Which of the pool is playing. Advances on every start.")]
        [SerializeField] private int _themeIndex = -1;

        /// <summary>The theme the pool and the position belong to.</summary>
        private string _themeName;

        [Tooltip("Where the resolved theme came from, for the record.")]
        [SerializeField] private string _themeSource;

        [SerializeField] private AudioClip _finishStinger;

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
            _simulator.Replayed += OnReplayed;
        }

        private void OnDisable()
        {
            if (_simulator == null) return;
            _simulator.Finished -= OnFinished;
            _simulator.Replayed -= OnReplayed;
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

            ResolveClips();
            PlayTheme();
        }

        /// <summary>
        /// The same track again: the next tune of the theme, so a replay moves the
        /// rotation on rather than repeating what was just playing.
        /// </summary>
        private void OnReplayed()
        {
            if (_source != null) _source.Stop();
            PlayTheme();
        }

        /// <summary>Starts the theme's next track.</summary>
        private void PlayTheme()
        {
            if (_source == null) return;

            _theme = Next();
            if (_theme == null) return;

            _source.clip = _theme;
            _source.loop = true;
            _source.volume = _volume;
            _source.Play();
        }

        /// <summary>
        /// The theme's next track, wrapping at the end.
        ///
        /// The pool is ordered remix first, so a theme entered fresh — where the
        /// position was reset to -1 — starts on the remix and only then works
        /// through the numbered tracks.
        /// </summary>
        private AudioClip Next()
        {
            if (_themePool.Count == 0) return null;

            _themeIndex = (_themeIndex + 1) % _themePool.Count;
            return _themePool[_themeIndex];
        }

        /// <summary>
        /// The theme gives way to <c>game_end</c>, which plays once and leaves the
        /// race silent behind the result panel.
        /// </summary>
        private void OnFinished()
        {
            if (_source == null) return;

            _source.Stop();
            if (_finishStinger == null) return;

            _source.clip = _finishStinger;
            _source.loop = false;
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
            if (_simulator != null && _simulator.Track != null)
            {
                string theme = ThemeOf(_simulator.Track.AssetName);
                _resolvedFor = _simulator.Track.AssetName;

                // Rebuilt only when the theme itself changes. Moving between two
                // courses of one theme keeps both the pool and the position, which
                // is what carries the rotation across tracks.
                if (!string.IsNullOrEmpty(theme) &&
                    !string.Equals(theme, _themeName, System.StringComparison.Ordinal))
                {
                    _themePool.Clear();
                    _themeIndex = -1;
                    _themeName = theme;

                    // Every theme draws from the later client's set, so the six
                    // themes sound like one another rather than like two eras.
                    Collect(directory + "/" + TCGamesFolder, theme);
                    _themeSource = _themePool.Count != 0 ? "TCGames" : null;
                }
            }
#endif
            if (_simulator != null && _simulator.Track != null)
            {
                _resolvedFor = _simulator.Track.AssetName;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Every clip of a theme in one directory: <c>village_01</c>, its remix
        /// <c>village_03_re</c> and anything else named after the theme. Sorted, so
        /// the pool is the same set whatever order the project hands them over.
        /// </summary>
        private void Collect(string directory, string theme)
        {
            string prefix = directory + "/";
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(
                         theme + "_ t:AudioClip", new[] { directory }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets matches loosely and searches below the directory, so
                // both the folder and the name are checked before a clip is taken.
                if (!path.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if (path.IndexOf('/', prefix.Length) >= 0) continue;
                if (!System.IO.Path.GetFileName(path).StartsWith(
                        theme + "_", System.StringComparison.Ordinal)) continue;

                var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null && !_themePool.Contains(clip)) _themePool.Add(clip);
            }

            // Remix first, then the numbered tracks in order. A remix is named
            // after the track it reworks - village_03_re - so sorting on the name
            // alone would bury it in the middle.
            _themePool.Sort((a, b) =>
            {
                bool remixA = a.name.EndsWith("_re", System.StringComparison.Ordinal);
                bool remixB = b.name.EndsWith("_re", System.StringComparison.Ordinal);
                if (remixA != remixB) return remixA ? -1 : 1;
                return string.CompareOrdinal(a.name, b.name);
            });
        }
#endif
    }
}
