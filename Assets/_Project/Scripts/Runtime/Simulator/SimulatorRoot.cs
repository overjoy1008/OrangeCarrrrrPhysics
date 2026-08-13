using System;
using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// Owns the simulation state and drives the views. Everything else in the
    /// scene — the kart, the track, the HUD — reads from here and never from each
    /// other.
    ///
    /// Unity's own rigid-body solver is not used anywhere. The recovered engine
    /// integrates in its own units against a -58.8 gravity, cuts every frame into
    /// 5 ms substeps, and resolves the ground itself; handing any of that to
    /// PhysX would change the numbers the whole port exists to preserve.
    ///
    /// In edit mode the state is held at the reset pose so the scene view shows a
    /// parked kart; the stepper only runs in play mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SimulatorDriverInput))]
    public sealed class SimulatorRoot : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private KartSpecAsset _kart;

        [Tooltip(
            "Asset name of the kart every scene opens on, looked up in the catalog " +
            "at load. Empty keeps whatever the scene itself was authored with.")]
        [SerializeField] private string _openingKart = KartGuestData.Oiia;
        [SerializeField] private TrackSpecAsset _track;

        [Tooltip("Optional. With a track collision world the kart drives on the real scene; without it, on the flat plane.")]
        [SerializeField] private TrackCollisionWorld _trackCollision;

        [Tooltip("The T key's list. Left empty, it is resolved from the project.")]
        [SerializeField] private TrackCatalog _trackCatalog;

        [Tooltip("The K key's list. Left empty, it is resolved from the project.")]
        [SerializeField] private KartCatalog _kartCatalog;

        [Tooltip("The U key's engine sound presets. Left empty, it is resolved from the project.")]
        [SerializeField] private KartSoundCatalog _soundCatalog;

        [Header("Views")]
        [SerializeField] private ChaseCameraRig _chaseCamera;
        [SerializeField] private TopDownCameraRig _topDownCamera;

        [Tooltip("The finish orbit. Left empty, one is built from the chase camera on play.")]
        [SerializeField] private SurroundCameraRig _surroundCamera;

        [Tooltip("The pre-countdown sweep. Left empty, one is built from the chase camera on play.")]
        [SerializeField] private ReadyCameraRig _readyCamera;
        [SerializeField] private KartView _kartView;
        [SerializeField] private TestTrackView _trackView;

        [Header("Effects and sound")]
        [Tooltip("The twin rear-wheel marks. Optional.")]
        [SerializeField] private SkidMarkTrail _skidMarks;

        [Tooltip("The flat triangle behind the kart while boosting. Optional.")]
        [SerializeField] private BoostFlame _boostFlame;

        [Tooltip("Engine, drift, booster and countdown samples. Optional.")]
        [SerializeField] private KartSoundPlayer _sound;

        [Tooltip("N in the original: the course's checkpoint gates. Optional.")]
        [SerializeField] private CourseGateView _gateView;

        [Tooltip("B in the original: the kart model's bounding boxes. Optional.")]
        [SerializeField] private KartModelBoundsView _modelBounds;

        [Header("Input")]
        [Tooltip("Left empty, the component on this object is used.")]
        [SerializeField] private SimulatorDriverInput _driverInput;

        [Header("State")]
        [SerializeField] private SimulatorViewMode _viewMode = SimulatorViewMode.Chase;

        /// <summary>
        /// Overrides the track's own lap count. Zero uses the track's.
        ///
        /// The number itself is content, not code: the original reads it out of the
        /// theme archive's <c>track.xml</c> and hands it to the course's setter at
        /// <c>0x004247E0</c>. It is not the same for every track — the R courses run
        /// 2 laps and <c>village_R03</c> only 1 — so it is carried per track in
        /// <see cref="TrackSpecAsset.Laps"/> and this is only here for trying
        /// another number without editing the asset.
        /// </summary>
        [Tooltip("0 uses the track's own lap count. Anything else overrides it.")]
        [SerializeField] private uint _lapCountOverride;

        [Tooltip("F in the original: the ground-drag trigger, x4 on entry and x0.25 on exit.")]
        [SerializeField] private bool _dragTriggerActive;

        private readonly FrameRateCounter _fps = new FrameRateCounter();
        private readonly KartFlatGround _flatGround = new KartFlatGround();

        /// <summary>
        /// The drift gauge. A simulator-side layer rather than recovered code —
        /// see <see cref="KartGauge"/> — so it gates the item boost the way the
        /// original's bench does and nothing else.
        /// </summary>
        private readonly KartGauge _gauge = new KartGauge();

        /// <summary>
        /// The gearbox. Like the gauge it is a simulator-side experiment — see
        /// <see cref="KartGearbox"/> — and it reaches nothing but the engine note
        /// and its dial.
        /// </summary>
        private readonly KartGearbox _gearbox = new KartGearbox();

        /// <summary>Whether the held boost key has a charge behind it.</summary>
        private bool _boostPressAllowed;


        /// <summary>
        /// The race's state machine: the grid, the countdown, the run and the
        /// finish. It owns the countdown rather than duplicating it.
        /// </summary>
        private readonly KartRaceFlow _flow = new KartRaceFlow();

        /// <summary>
        /// Which cameraman is installed. The original swaps camera objects at
        /// <c>count_3</c> and at the finish, so the port switches the same way
        /// instead of holding a view flag.
        /// </summary>
        private readonly KartCameraDirector _cameras = new KartCameraDirector();

        private uint _raceClockMs;

        private KartSimulationState _state;
        private KartSimulationControls _controls;
        private bool _startBoostPending = true;

        private KartCourse _course;
        private TrackCourseAsset _courseBuiltFrom;
        private KartCourseProgress _progress;

        /// <summary>The lap <see cref="LapStarted"/> was last raised for.</summary>
        private uint _announcedLap;

        /// <summary>
        /// Where the kart was at the end of the previous step. The course walks
        /// the segment between two consecutive positions, so it needs the pair
        /// rather than the current pose.
        /// </summary>
        private KartVec3 _previousPosition;

        /// <summary>
        /// Seconds left before an armed respawn fires, or a negative value when
        /// nothing is armed. The original arms a timer and only moves the kart
        /// 500 ms later, for a fall and for its own reset command alike.
        /// </summary>
        private float _respawnDelay = -1f;

        /// <summary>The original's own delay, and the reason R is not instant.</summary>
        private const float RespawnDelaySeconds = 0.5f;

        /// <summary>
        /// Seconds left of the hold after a warp, during which the drive inputs
        /// are ignored — the same hold the countdown uses at the start line.
        /// </summary>
        private float _warpHold;

        /// <summary>
        /// How long the kart sits still at the top of ice_R01's lift.
        ///
        /// Not a recovered constant. Nothing in the track's course tables says how
        /// long the 2004 game holds the kart there, so this is a chosen value and
        /// the one number here that is safe to tune by eye.
        /// </summary>
        private const float WarpHoldSeconds = 1f;

        /// <summary>Raised after the state and the views have been advanced.</summary>
        public event Action Stepped;

        /// <summary>Raised on the frame the final gate closed the race.</summary>
        public event Action Finished;

        /// <summary>
        /// Raised when the track already being raced is picked again, which is a
        /// replay rather than a track change.
        /// </summary>
        public event Action Replayed;

        /// <summary>
        /// Raised on the crossing that starts a lap, with the lap now being driven.
        ///
        /// The original checks the same thing in its own stage loop: when the lap
        /// counter turns over and equals the course's lap count it plays the final
        /// lap cue and runs the <c>finallap</c> action.
        /// </summary>
        public event Action<uint> LapStarted;

        /// <summary>Raised three seconds later, when the result panel is due.</summary>
        public event Action ResultsShown;

        /// <summary>
        /// Raised eight seconds after the finish, where the original leaves for
        /// <c>SelectChallengeStage</c>.
        /// </summary>
        public event Action RaceExitDue;

        public KartSimulationState State
        {
            get
            {
                if (_state == null) ResetSimulation();
                return _state;
            }
        }

        public KartSpecAsset Kart => _kart;
        public TrackSpecAsset Track => _track;
        public float FramesPerSecond => _fps.FramesPerSecond;

        /// <summary>This frame's driver input, for the HUD.</summary>
        public KartSimulationControls Controls => _controls;

        public bool DragTriggerActive => _dragTriggerActive;

        /// <summary>
        /// Seconds left on the "fell through the track" notice, for the HUD.
        /// </summary>
        public float RespawnNoticeSeconds { get; private set; }

        /// <summary>Laid-down skid quads, as the original's <c>skids</c> read-out counts them.</summary>
        public int SkidMarkSegments => _skidMarks != null ? _skidMarks.SegmentCount : 0;

        /// <summary>The skid face being laid, as the HUD reads it.</summary>
        public string SkidStyleName => _skidMarks != null ? _skidMarks.StyleName : "none";

        /// <summary>
        /// The <c>I</c> key: the next skid face. Not the original's — the 2004 game
        /// lays one mark and has no way to change it — so it sits with the other
        /// port-only keys on the function row.
        /// </summary>
        public void NextSkidStyle()
        {
            if (_skidMarks == null) return;
            _skidMarks.NextStyle();
            _skidMarks.Clear();
        }

        /// <summary>The race-start countdown, as the HUD reads it.</summary>
        public KartCountdown Countdown => _flow.Countdown;

        /// <summary>The race's state machine, as the HUD and the cameras read it.</summary>
        public KartRaceFlow Race => _flow;

        /// <summary>
        /// Whether the finish ends the race or is only recorded.
        ///
        /// <see cref="KartRaceMode.Free"/> is what the simulator has always done and
        /// what the parameter window, the kart cycle and the track cycle assume;
        /// <see cref="KartRaceMode.Race"/> is the original's rule. Nothing acts on
        /// this yet — the finish itself is not detected until the course reports it.
        /// </summary>
        public KartRaceMode RaceMode
        {
            get => _flow.Mode;
            set => _flow.Mode = value;
        }

        public void ToggleRaceMode()
            => RaceMode = _flow.Mode == KartRaceMode.Race ? KartRaceMode.Free : KartRaceMode.Race;

        /// <summary>Milliseconds since the countdown was armed.</summary>
        public uint RaceClockMs => _raceClockMs;

        /// <summary>
        /// The track's checkpoint graph, or null on a track that has no course —
        /// which is only the synthetic flat one.
        /// </summary>
        public KartCourse Course => _course;

        /// <summary>The kart's place on that graph, as the HUD reads it.</summary>
        public KartCourseProgress Progress => _progress;

        public bool CourseReady => _course != null && _course.NodeCount != 0;

        /// <summary>
        /// Laps before the finish, as the HUD and the course read it: the track's
        /// own number unless the inspector overrides it.
        /// </summary>
        public uint LapCount => _lapCountOverride != 0u
            ? _lapCountOverride
            : (_track != null ? _track.Laps : 0u);

        /// <summary>
        /// True while a selection menu owns the keyboard. The drive inputs are
        /// blanked for as long as it does, since the menu moves on the same arrow
        /// keys the kart steers with.
        /// </summary>
        public bool MenuOpen { get; set; }

        /// <summary>The drift gauge, as the HUD reads it.</summary>
        public KartGauge Gauge => _gauge;

        /// <summary>The gearbox experiment, as the tachometer reads it.</summary>
        public KartGearbox Gearbox => _gearbox;

        /// <summary>The <c>E</c> key: one straight ramp, or four gear bands.</summary>
        public void ToggleGearMode() => _gearbox.ToggleMode();

        /// <summary>
        /// The engine note the sound driver is holding, for the tachometer. It
        /// only refreshes every 64 ms, and the dial shows exactly that; with no
        /// sound player the ramp's own base stands in.
        /// </summary>
        public float MotorPitch =>
            _sound != null ? _sound.MotorPitch : KartSoundConstants.MotorBase;

        public float MotorVolume => _sound != null ? _sound.MotorVolume : 0f;

        /// <summary>
        /// Replaces the running dynamics config. The <c>P</c> window writes
        /// through here so a change takes effect on the next step rather than
        /// waiting for a reset — which is the point of a live editor.
        /// </summary>
        public void SetDynamics(in KartDynamicsConfig config) => State.Config = config;

        /// <summary>The <c>1</c> key: the next charging hypothesis.</summary>
        public void NextGaugeModel() => _gauge.NextModel();

        /// <summary>
        /// The <c>2</c> key: whether a drift exit opens the accelerator window or
        /// banks a charge to spend later. Clearing the window with it, so the two
        /// models never overlap on the same drift.
        /// </summary>
        public void ToggleStoredInstantBoost()
        {
            KartSimulationState state = State;
            state.InstantBoost.StoredModel = !state.InstantBoost.StoredModel;
            state.InstantBoost.OpportunityTimer = 0f;
        }

        /// <summary>
        /// The <c>5</c> key: whether releasing the throttle ends a boost, or only
        /// pressing reverse does.
        /// </summary>
        public void ToggleBoostCutoffModel()
            => State.ReverseInputEndsBoost = !State.ReverseInputEndsBoost;

        /// <summary>The instant boost's model and banked charges, for the HUD.</summary>
        public bool StoredInstantBoost => State.InstantBoost.StoredModel;

        public uint StoredInstantBoostCount => State.InstantBoost.StoredCount;

        public bool ReverseInputEndsBoost => State.ReverseInputEndsBoost;

        /// <summary>
        /// Whether a held booster key re-arms itself, chaining one charge
        /// straight into the next. Off is the recovered behaviour.
        ///
        /// Two gates have to give way together, one on each side of the step: the
        /// gauge's one-charge-per-grant latch in <see cref="SpendGaugeCharge"/>,
        /// and the press edge the engine itself takes in
        /// <c>KartSimulation.SimulateMilliseconds</c>. Dropping only the first
        /// spends the charge and starts nothing.
        /// </summary>
        public bool NoDelayBoost => State.NoDelayBoost;

        /// <summary>The <c>4</c> key: chain boosters, or one press per booster.</summary>
        public void ToggleNoDelayBoost() => State.NoDelayBoost = !State.NoDelayBoost;

        /// <summary>The <c>3</c> key: booster storage capped by the kart, or not.</summary>
        public void ToggleUnlimitedBoosters()
        {
            _gauge.UnlimitedBoosters = !_gauge.UnlimitedBoosters;

            uint max = _kart != null ? _kart.ToSpec().MaxBoosters : KartDemoData.DefaultMaxBoosters;
            if (!_gauge.UnlimitedBoosters && _gauge.Boosters > max) _gauge.Boosters = max;
        }

        /// <summary>The ground the wheels ray-cast against.</summary>
        private IKartGroundQuery Ground => _trackCollision != null && _trackCollision.World != null
            ? (IKartGroundQuery)_trackCollision.World
            : _flatGround;

        /// <summary>The body-box world, absent on the flat reference track.</summary>
        private IKartBodyCollisionQuery BodyWorld =>
            _trackCollision != null ? _trackCollision.World : null;

        public SimulatorViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode == value) return;
                _viewMode = value;
                ApplyViewMode();
            }
        }

        /// <summary>The camera currently rendering, whichever cameraman is installed.</summary>
        public Camera ActiveCamera => _cameras.ActiveCamera;

        /// <summary>The <c>F3</c> key: draw the checkpoint gates over the track.</summary>
        public bool ShowCheckpoints
        {
            get => _gateView != null && _gateView.Show;
            set { if (_gateView != null) _gateView.Show = value; }
        }

        /// <summary>
        /// The <c>F4</c> key: the kart model's three bounding boxes.
        ///
        /// The original's B is the <em>model</em> bounds, not the track's. The
        /// track's AABB wall is drawn unconditionally by <c>draw_track</c> and was
        /// never on a key, which is why <see cref="TestTrackView"/> keeps drawing
        /// its own and no longer answers to this.
        /// </summary>
        public bool ShowBounds
        {
            get => _modelBounds != null && _modelBounds.Show;
            set { if (_modelBounds != null) _modelBounds.Show = value; }
        }

        /// <summary>Row of <c>colortable.xml</c>, or the shipped default.</summary>
        public int KartColourIndex => _kartView != null
            ? _kartView.ColourIndex
            : KartColorTable.SimulatorIndex;

        public string KartColourName => _kartView != null
            ? _kartView.ColourName
            : KartColorTable.NameAt(KartColorTable.SimulatorIndex);

        /// <summary>
        /// The paint the current kart opens on: the colour its series wears in
        /// the gallery, so a kart looks the same wherever it is met.
        /// </summary>
        public int DefaultKartColourIndex => _kart != null
            ? KartGalleryLayout.ColourOf(_kart.AssetName)
            : KartColorTable.SimulatorIndex;

        /// <summary>The <c>C</c> list: one of the ten paints, chosen outright.</summary>
        public void SetKartColour(int index)
        {
            if (_kartView != null) _kartView.ColourIndex = index;
        }

        /// <summary>The next of the ten paints. No key walks it any more.</summary>
        public void NextKartColour()
        {
            if (_kartView != null) _kartView.NextColour();
        }

        /// <summary>
        /// Puts the current kart into its series colour.
        ///
        /// Called when the kart changes rather than every frame, so a colour
        /// picked from the <c>C</c> list survives until the next kart is chosen.
        /// </summary>
        private void ApplyDefaultKartColour()
        {
            if (_kartView != null) _kartView.ColourIndex = DefaultKartColourIndex;
        }

        /// <summary>The <c>T</c> key's list, resolved from the project when unset.</summary>
        public TrackCatalog Tracks => ResolveTrackCatalog();

        /// <summary>The <c>K</c> key's list, resolved from the project when unset.</summary>
        public KartCatalog Karts => ResolveKartCatalog();

        /// <summary>
        /// The next track in the catalog. Kept for the cycle the port used before
        /// <c>T</c> opened a list.
        /// </summary>
        public void NextTrack()
        {
            TrackCatalog catalog = ResolveTrackCatalog();
            if (catalog == null || catalog.Count < 2)
            {
                Debug.LogWarning("No other track to switch to.", this);
                return;
            }
            LoadTrack(catalog.Next(_track));
        }

        /// <summary>
        /// Switches to a track.
        ///
        /// A track is a whole scene here, so switching loads one. The original
        /// swaps which embedded KTRK the renderer reads instead, but it is not
        /// carrying 375 GameObjects and a collision set per track.
        /// </summary>
        public void LoadTrack(TrackSpecAsset track)
        {
            if (track == null) return;

            // Picking the track already loaded is a replay: there is no scene to
            // load, so the race is simply put back on the grid.
            if (track == _track)
            {
                Replay();
                return;
            }

            if (string.IsNullOrWhiteSpace(track.SceneName))
            {
                Debug.LogWarning($"Track '{track.AssetName}' names no scene.", this);
                return;
            }

            if (!Application.isPlaying) return;
            SceneManager.LoadScene(track.SceneName);
        }

        /// <summary>
        /// Runs the same track again from the grid.
        ///
        /// The same reset <c>R</c> uses, plus a signal for the things that should
        /// treat it as a fresh race rather than a mid-race respawn — the music
        /// picks another theme track off it.
        /// </summary>
        public void Replay()
        {
            if (!Application.isPlaying) return;

            ResetSimulation();
            Replayed?.Invoke();
        }

        /// <summary>
        /// The next of the twenty-six karts. Kept for the cycle the port used
        /// before <c>K</c> opened a list.
        /// </summary>
        public void NextKart()
        {
            KartCatalog catalog = ResolveKartCatalog();
            if (catalog == null || catalog.Count < 2)
            {
                Debug.LogWarning("No other kart to switch to.", this);
                return;
            }
            SelectKart(catalog.Next(_kart));
        }

        /// <summary>
        /// Switches to a kart, without interrupting the run.
        ///
        /// Nothing is loaded and nothing is reset: the new body and dynamics are
        /// swapped in underneath, and the speed, the drift gauge, the stored
        /// boosters, the race clock and the lap all keep going. That is a bench
        /// choice, not the original's — the original puts the kart back on the
        /// line — and it is the whole point of the <c>K</c> list, which is there
        /// to compare karts through the same corner rather than one run each.
        ///
        /// <c>R</c> is still the way back to the line.
        /// </summary>
        public void SelectKart(KartSpecAsset kart)
        {
            if (kart == null || kart == _kart) return;

            _kart = kart;
            if (_kartView != null) _kartView.Kart = kart;
            if (_boostFlame != null) _boostFlame.Kart = kart;
            ApplyEngineSound();
            ApplyDefaultKartColour();

            // Swapped under the running simulation rather than resetting it: the
            // speed, the gauge, the stored boosters and the lap all carry over, so
            // two karts can be taken through the same corner back to back instead
            // of from the grid each time.
            if (_state != null)
            {
                KartSpec spec = kart.ToSpec();
                KartSimulation.Rekart(_state, Leaned(spec.Dynamics), spec.Geometry);

                // Storage is per kart, so a swap onto a kart that holds fewer has
                // to drop what no longer fits — the same trim the H key does.
                if (!_gauge.UnlimitedBoosters && _gauge.Boosters > spec.MaxBoosters)
                {
                    _gauge.Boosters = spec.MaxBoosters;
                }

                // The new body is a different size, so the view is placed from the
                // state now rather than a frame later.
                if (_kartView != null) _kartView.Apply(_state);
            }
            else
            {
                ResetSimulation();
            }
        }

        private KartCatalog ResolveKartCatalog()
        {
#if UNITY_EDITOR
            if (_kartCatalog == null)
            {
                _kartCatalog = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<KartCatalog>("Assets/_Project/Data/Karts/KartCatalog.asset");
            }
#endif
            return _kartCatalog;
        }

        private KartSoundCatalog ResolveSoundCatalog()
        {
#if UNITY_EDITOR
            if (_soundCatalog == null)
            {
                _soundCatalog = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<KartSoundCatalog>(
                        "Assets/_Project/Data/Audio/KartSoundCatalog.asset");
            }
#endif
            return _soundCatalog;
        }

        /// <summary>The engine preset now playing, for the HUD.</summary>
        public string EngineSoundPreset =>
            _sound != null && _sound.Sounds != null ? _sound.Sounds.Preset : "none";

        /// <summary>
        /// Puts the current kart's own engine on the sound player.
        ///
        /// The preset follows the kart rather than a key, because it is a property
        /// of the kart: the later client ships one engine set per generation and
        /// a paragon has never sounded like a cotten. This used to be the <c>U</c>
        /// key cycling all thirteen sets by hand.
        ///
        /// A preset the project has not imported leaves the player on whatever it
        /// already had, which is the demo's classic — the wrong timbre is a better
        /// outcome than a kart that goes silent.
        /// </summary>
        /// <summary>
        /// Puts the opening kart under the simulator, by name.
        ///
        /// By name rather than by reference because the kart is authored into
        /// every one of the sixteen scenes, and changing which one they open on
        /// would otherwise mean re-authoring all of them — and could not name a
        /// guest at all, whose spec asset is created by the catalog builder rather
        /// than committed. A name the catalog does not have leaves the scene's own
        /// choice alone, which is what happens before the builder has run.
        /// </summary>
        private void ApplyOpeningKart()
        {
            // Play only. This component runs in edit mode too, and writing to a
            // serialised field there would dirty every scene merely by opening
            // it — a scene would be re-authored as a side effect of being looked
            // at. In edit mode the scene keeps showing the kart it was saved with.
            if (!Application.isPlaying) return;
            if (string.IsNullOrEmpty(_openingKart)) return;

            KartCatalog catalog = ResolveKartCatalog();
            if (catalog == null) return;

            for (int i = 0; i < catalog.Count; ++i)
            {
                KartSpecAsset candidate = catalog.At(i);
                if (candidate == null ||
                    !string.Equals(candidate.AssetName, _openingKart, StringComparison.Ordinal))
                {
                    continue;
                }

                _kart = candidate;
                if (_kartView != null) _kartView.Kart = candidate;
                if (_boostFlame != null) _boostFlame.Kart = candidate;
                return;
            }
        }

        private void ApplyEngineSound()
        {
            if (_sound == null) return;

            // Set before the early return below, because it is per kart and the
            // preset is not: two guests can share the 9th's engine and still want
            // different boosters.
            _sound.BoosterOverride = _kart != null ? _kart.BoosterSound : null;
            _sound.BoosterOverrideStart = _kart != null ? _kart.BoosterSoundStart : 0f;
            _sound.BoosterOverrideSlowStart = _kart != null ? _kart.BoosterSoundSlowStart : 0f;

            string wanted = KartEnginePreset.For(_kart != null ? _kart.AssetName : null);
            if (_sound.Sounds != null && KartEnginePreset.Matches(_sound.Sounds.Preset, wanted)) return;

            KartSoundCatalog catalog = ResolveSoundCatalog();
            if (catalog == null) return;

            KartSoundSet set = catalog.Find(wanted);
            if (set != null) _sound.Sounds = set;
            else Debug.LogWarning($"No '{wanted}' engine sound set in the catalog.", this);
        }

        private TrackCatalog ResolveTrackCatalog()
        {
#if UNITY_EDITOR
            if (_trackCatalog == null)
            {
                _trackCatalog = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/Tracks/TrackCatalog.asset");
            }
#endif
            return _trackCatalog;
        }

        /// <summary>
        /// The frame-rate caps the <c>F6</c> key walks, in cycle order.
        ///
        /// Not something the original has. It is here because the port has to be
        /// frame-rate independent and that is otherwise untestable: the skid marks
        /// were laid one per rendered frame until running at 133 fps made the
        /// difference visible.
        /// </summary>
        private static readonly int[] FrameRateCaps = { 0, 144, 60, 40, 30 };

        // Static, not serialised: a track switch loads a new scene with a new
        // SimulatorRoot in it, and the cap belongs to the session rather than to
        // whichever scene happens to be open.
        private static int _frameRateCapIndex;

        /// <summary>Frames per second, or 0 for uncapped.</summary>
        public int FrameRateCap => FrameRateCaps[
            _frameRateCapIndex >= 0 && _frameRateCapIndex < FrameRateCaps.Length
                ? _frameRateCapIndex
                : 0];

        public string FrameRateCapName => FrameRateCap == 0 ? "off" : FrameRateCap.ToString();

        /// <summary>The <c>F6</c> key.</summary>
        public void CycleFrameRateCap()
        {
            _frameRateCapIndex = (_frameRateCapIndex + 1) % FrameRateCaps.Length;
            ApplyFrameRateCap();
        }

        private void ApplyFrameRateCap()
        {
            // targetFrameRate is ignored while vSync is on, so a cap has to turn
            // it off; uncapped puts it back the way the project ships.
            int cap = FrameRateCap;
            QualitySettings.vSyncCount = cap == 0 ? 1 : 0;
            Application.targetFrameRate = cap == 0 ? -1 : cap;
        }

        /// <summary>
        /// The driver input, resolved from this object when the reference was not
        /// set in the inspector. <see cref="RequireComponentAttribute"/> guarantees
        /// it exists, so an older scene picks it up without being re-authored.
        /// </summary>
        private SimulatorDriverInput DriverInput
        {
            get
            {
                if (_driverInput == null) _driverInput = GetComponent<SimulatorDriverInput>();
                return _driverInput;
            }
        }

        private void OnEnable()
        {
            EnsureEffects();
            EnsureRaceCameras();
            ApplyOpeningKart();

            // After EnsureEffects, which is what finds or creates the player the
            // preset goes on, and before the first frame is heard.
            ApplyEngineSound();
            ApplyDefaultKartColour();

            ResetSimulation();
            ApplyViewMode();

            // The cap survives a track switch because it is a property of the
            // player's session, not of the scene that happens to be loaded.
            if (Application.isPlaying) ApplyFrameRateCap();
        }

        /// <summary>
        /// Creates the effect and sound objects when the scene has none.
        ///
        /// All three are pure view: they hold no authored state, only what the
        /// simulation tells them each frame. Making them appear on their own means
        /// there is nothing to remember to drag in, and a reference set in the
        /// inspector still wins.
        /// </summary>
        private void EnsureEffects()
        {
            // Whatever the scene already has always wins, whether it was authored
            // or created by an earlier run.
            if (_skidMarks == null) _skidMarks = GetComponentInChildren<SkidMarkTrail>(includeInactive: true);
            if (_sound == null) _sound = GetComponentInChildren<KartSoundPlayer>(includeInactive: true);
            if (_gateView == null) _gateView = GetComponentInChildren<CourseGateView>(includeInactive: true);
            if (_modelBounds == null)
            {
                _modelBounds = GetComponentInChildren<KartModelBoundsView>(includeInactive: true);
            }
            if (_boostFlame == null && _kartView != null)
            {
                _boostFlame = _kartView.GetComponentInChildren<BoostFlame>(includeInactive: true);
            }

            // Assigned here rather than only on the created path, so an authored
            // one is bound in edit mode too.
            if (_modelBounds != null) _modelBounds.Kart = _kartView;

            // Only ever created while playing, so opening the scene never adds
            // objects to it and nothing is left behind to be saved by accident.
            if (!Application.isPlaying) return;

            if (_skidMarks == null) _skidMarks = Attach<SkidMarkTrail>("Skid marks", transform);
            if (_gateView == null) _gateView = Attach<CourseGateView>("Course gates", transform);
            if (_modelBounds == null)
            {
                _modelBounds = Attach<KartModelBoundsView>("Kart model bounds", transform);
            }
            if (_modelBounds != null) _modelBounds.Kart = _kartView;

            if (_boostFlame == null && _kartView != null)
            {
                // Parented to the kart: the triangle is built in the kart's own
                // frame, so it follows the model rather than being rebuilt every
                // frame in world space.
                _boostFlame = Attach<BoostFlame>("Boost flame", _kartView.transform);
            }

            if (_sound == null) _sound = gameObject.AddComponent<KartSoundPlayer>();

            // The race's music, on its own object so its AudioSource does not share
            // one with the engine loop.
            if (GetComponentInChildren<RaceMusicPlayer>(includeInactive: true) == null)
            {
                Attach<RaceMusicPlayer>("Race music", transform);
            }
        }

        /// <summary>
        /// Builds the finish camera from the chase camera when the scene has none.
        ///
        /// The thirteen track scenes were authored before there was a
        /// <c>SurroundCameraman</c> to author, and copying the chase camera is the
        /// only way to get its clear flags, culling mask and clip planes right
        /// without hand-editing every one of them. A rig dragged into the field
        /// still wins.
        /// </summary>
        private void EnsureRaceCameras()
        {
            if (_surroundCamera == null)
            {
                _surroundCamera = GetComponentInChildren<SurroundCameraRig>(includeInactive: true);
            }
            if (_readyCamera == null)
            {
                _readyCamera = GetComponentInChildren<ReadyCameraRig>(includeInactive: true);
            }

            // Only while playing, so opening a scene never adds a camera to it and
            // nothing is left behind to be saved by accident.
            if (!Application.isPlaying || _chaseCamera == null) return;

            if (_surroundCamera == null)
            {
                _surroundCamera = BuildCamera<SurroundCameraRig>("Surround camera");
            }
            if (_readyCamera == null)
            {
                _readyCamera = BuildCamera<ReadyCameraRig>("Ready camera");
            }
        }

        private T BuildCamera<T>(string name) where T : Component
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, worldPositionStays: false);

            Camera copy = holder.AddComponent<Camera>();
            copy.CopyFrom(_chaseCamera.Camera);

            T rig = holder.AddComponent<T>();
            holder.SetActive(false);
            return rig;
        }

        private static T Attach<T>(string name, Transform parent) where T : Component
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, worldPositionStays: false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            return holder.AddComponent<T>();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            ApplyViewMode();
        }

        public void ToggleViewMode()
            => ViewMode = _viewMode == SimulatorViewMode.Chase
                ? SimulatorViewMode.TopDown
                : SimulatorViewMode.Chase;

        /// <summary>
        /// How hard, and which way, the body rolls into a drift.
        ///
        /// Both halves of that matter and neither can be settled from the outside.
        /// The magnitude depends on how big the kart is — the recovered 0.07 suits
        /// a wide, flat 2004 body and throws a narrow cat from lock to lock — and
        /// the sign depends on which way round the model was built, since that is
        /// what decides which roll reads as leaning <em>into</em> the corner.
        ///
        /// So the key walks a fixed ladder rather than nudging a float: eight
        /// stops, four each way, which is enough to find the one that looks right
        /// and few enough to reach it in a couple of presses.
        /// </summary>
        public static readonly float[] DriftLeanSteps =
        {
            0.07f, 0.05f, 0.04f, 0.03f, -0.03f, -0.04f, -0.05f, -0.07f,
        };

        /// <summary>
        /// Which stop is chosen, or -1 while every kart is still on its own value.
        ///
        /// A session setting once set, like the boost-model toggles: it survives a
        /// kart swap, because the reason to be on a given stop is usually the
        /// comparison being made rather than the kart under it.
        /// </summary>
        private int _driftLeanStep = -1;

        /// <summary>The drift lean now in force, for the HUD.</summary>
        public float DriftLean => _state != null
            ? _state.Config.DriftLeanFactor
            : KartDynamicsConfig.Standard().DriftLeanFactor;

        /// <summary>
        /// The <c>L</c> key: the next stop down the ladder.
        ///
        /// The first press starts from the stop nearest the kart's own value, so
        /// the ladder is entered where the kart already is rather than jumping to
        /// its top.
        /// </summary>
        public void NextDriftLean()
        {
            if (_driftLeanStep < 0) _driftLeanStep = NearestDriftLeanStep(DriftLean);
            _driftLeanStep = (_driftLeanStep + 1) % DriftLeanSteps.Length;

            if (_state != null) _state.Config = Leaned(_state.Config);
        }

        private static int NearestDriftLeanStep(float value)
        {
            int nearest = 0;
            float best = float.MaxValue;

            for (int i = 0; i < DriftLeanSteps.Length; ++i)
            {
                float distance = Mathf.Abs(DriftLeanSteps[i] - value);
                if (distance >= best) continue;

                best = distance;
                nearest = i;
            }
            return nearest;
        }

        /// <summary>
        /// A kart's dynamics as the chosen stop wants them, or untouched while
        /// none has been chosen.
        ///
        /// The steer lean follows the drift lean's sign. It is a tenth the size
        /// and never needed a stop of its own, but a kart leaning into the corner
        /// under drift and out of it under steer reads as a bug, so the two agree.
        /// </summary>
        private KartDynamicsConfig Leaned(KartDynamicsConfig dynamics)
        {
            if (_driftLeanStep < 0) return dynamics;

            float lean = DriftLeanSteps[_driftLeanStep];
            dynamics.DriftLeanFactor = lean;
            dynamics.SteerLeanFactor = Mathf.Abs(dynamics.SteerLeanFactor) * Mathf.Sign(lean);
            return dynamics;
        }

        /// <summary>The <c>R</c> key: back to the reset pose with a snapped camera.</summary>
        public void ResetSimulation()
        {
            KartSpec kartSpec = _kart != null ? _kart.ToSpec() : KartDemoData.DefaultKart;
            TrackSpec trackSpec = _track != null ? _track.ToSpec() : KartDemoData.DefaultTrack;

            // The three bench choices survive the reset, the way the original's
            // reset_kart carries them across kart_simulation_init: they are
            // settings about what is being compared, not part of the kart.
            bool storedModel = _state != null && _state.InstantBoost.StoredModel;
            bool reverseEndsBoost = _state != null && _state.ReverseInputEndsBoost;
            bool noDelayBoost = _state != null && _state.NoDelayBoost;

            _state ??= new KartSimulationState();
            KartSimulation.Init(_state, Leaned(kartSpec.Dynamics), kartSpec.Geometry);
            _state.InstantBoost.StoredModel = storedModel;
            _state.ReverseInputEndsBoost = reverseEndsBoost;
            _state.NoDelayBoost = noDelayBoost;

            _flatGround.Height = trackSpec.Minimum.Z;
            BuildCourse();
            ApplyStartPose(trackSpec);
            _previousPosition = _state.Position;
            _respawnDelay = -1f;
            _warpHold = 0f;

            // The charging model and the storage toggle survive a reset — they are
            // what is being compared across runs — but the charge itself does not.
            _gauge.Reset();
            _boostPressAllowed = false;

            // The original arms the countdown on reset and backdates it so the
            // race clock starts at zero. The mode survives, the way the bench
            // choices above do: it is a setting about what is being run, not part
            // of the race.
            _raceClockMs = 0u;
            _announcedLap = 0u;
            _flow.Start(_raceClockMs);

            _dragTriggerActive = false;
            _startBoostPending = true;
            _controls = default;
            _fps.Reset();

            if (DriverInput != null) DriverInput.ResetSteering();
            if (_chaseCamera != null) _chaseCamera.ResetFollow();

            // The sweep is already installed when the race is reset from the grid,
            // so the director will not re-activate it and it has to be rewound here.
            if (_readyCamera != null) _readyCamera.Restart();
            if (_skidMarks != null) _skidMarks.Clear();
            if (_boostFlame != null)
            {
                _boostFlame.Kart = _kart;
                _boostFlame.SetVisible(false);
            }
            if (_sound != null) _sound.Restart();
            if (_kartView != null)
            {
                _kartView.Kart = _kart;
                _kartView.Apply(_state);
            }
            SettleOnGround();
        }

        /// <summary>
        /// Builds the track's checkpoint graph, once per course asset.
        ///
        /// Rebuilding is keyed on the asset rather than done every reset: the
        /// graph is thousands of gates and links, and R is pressed far more often
        /// than the track changes.
        /// </summary>
        private void BuildCourse()
        {
            TrackCourseAsset asset = _track != null ? _track.Course : null;
            if (asset != _courseBuiltFrom || (_course == null && asset != null))
            {
                _courseBuiltFrom = asset;
                _course = asset != null ? KartCourse.Build(asset.ToAsset()) : null;

                if (asset != null && _course == null)
                {
                    Debug.LogWarning($"{asset.name}'s course could not be built.", this);
                }
            }

            // Set every time rather than only on a rebuild, so changing the field
            // in the inspector takes effect on the next reset.
            _course?.SetLapCount(LapCount);

            if (_gateView != null) _gateView.Course = _course;
        }

        /// <summary>
        /// Puts the kart on the start grid.
        ///
        /// The course is the authority where a track has one: it fixes the pose
        /// exactly, including which way round the lap is driven — something the
        /// start-line mesh alone never said, and which eleven of the thirteen
        /// tracks were only assuming. The measured start line stays as the
        /// fallback for the synthetic track, which has no course.
        /// </summary>
        private void ApplyStartPose(TrackSpec track)
        {
            if (CourseReady)
            {
                // Slot 0 of the start grid, then the ground snap the original
                // follows it with: a ray from 10 above the placed position, 100
                // down.
                _course.StartPose(0, out KartVec3 gridPosition, out KartQuat gridOrientation);
                if (Ground.QueryGround(
                        new KartVec3(gridPosition.X, gridPosition.Y, gridPosition.Z + 10f),
                        new KartVec3(0f, 0f, -100f),
                        out KartGroundHit hit))
                {
                    gridPosition = hit.Point;
                }

                _state.Position = gridPosition;
                _state.Orientation = gridOrientation;
                _progress = KartCourseProgress.Init(_course, gridPosition);
                return;
            }

            _state.Orientation = KartTrackStart.Orientation(track);

            if (KartTrackStart.Position(track, out KartVec3 position))
            {
                // The scene is dropped so the start line's plane is z = 0, and the
                // kart is lifted clear of it so the suspension settles down onto
                // the road rather than starting inside it.
                _state.Position = new KartVec3(
                    position.X, position.Y, position.Z + _state.Geometry.SuspensionRange);
            }
            else
            {
                _state.Position = new KartVec3(0f, 0f, _flatGround.Height);
            }
        }

        /// <summary>
        /// The <c>F</c> key. The original's trigger callbacks multiply the ground
        /// drag by 4 on entry and 0.25 on exit, so toggling is exactly that pair.
        /// </summary>
        public void ToggleDragTrigger()
        {
            _dragTriggerActive = !_dragTriggerActive;
            KartSimulation.MultiplyGroundedDragScale(State, _dragTriggerActive ? 4f : 0.25f);
        }

        private void Update()
        {
            KartSimulationState state = State;

            if (!Application.isPlaying)
            {
                // Edit mode: hold the reset pose and only keep the views in step,
                // so opening the scene does not quietly integrate the kart away
                // from where it was authored.
                if (_kartView != null) _kartView.Apply(state);
                ApplyViewMode();
                _cameras.Step(state, (uint)Mathf.RoundToInt(SimulatorTickSeconds * 1000f));
                Stepped?.Invoke();
                return;
            }

            float deltaTime = Time.deltaTime;
            _fps.Tick(deltaTime);

            uint elapsedMs = (uint)Mathf.Max(1, Mathf.RoundToInt(deltaTime * 1000f));
            _raceClockMs += elapsedMs;

            _controls = DriverInput != null ? DriverInput.Sample() : default;
            SpendGaugeCharge();

            KartRaceFlowCues flow = _flow.Update(_raceClockMs);
            KartCountdownCues cues = flow.Countdown;
            if (_sound != null) _sound.PlayCountdown(cues);

            if (flow.ShowResults) ResultsShown?.Invoke();
            if (flow.ExitDue) RaceExitDue?.Invoke();

            // The original lets the player rev on the line: holding drift with the
            // throttle runs the booster idle loop until GO. Read before the line
            // release blanks the inputs below.
            if (_sound != null)
            {
                _sound.SetBoosterIdle(
                    !cues.Released && _controls.DriftInput && _controls.ForwardInput != 0f);
            }

            if (_warpHold > 0f) _warpHold = Mathf.Max(0f, _warpHold - deltaTime);

            if (flow.DriveHeld || _warpHold > 0f || MenuOpen)
            {
                // Held at the line: the original releases every kart on GO, so
                // until then the drive inputs are ignored and only the view keys
                // do anything. The race takes the wheel back at the finish through
                // the same flag. The same hold sets the kart down at the top of
                // ice_R01's lift, which is the one other place it arrives
                // somewhere it did not drive to, and holds it while a selection
                // menu has the arrow keys.
                _controls.ForwardInput = 0f;
                _controls.ReverseInput = 0f;
                _controls.DriftInput = false;
                _controls.BoostActive = false;

                // The original blanks the jump on the line with the throttle and
                // the boost, so nobody arrives at GO mid-crouch.
                _controls.JumpInput = false;
                _controls.DriveDisabled = true;
            }
            else if (_startBoostPending && _controls.ForwardInput != 0f)
            {
                _startBoostPending = false;
                if (_flow.Countdown.StartBoostGranted(_raceClockMs))
                {
                    // The line is not a moment to be slow at, and this goes
                    // through the same timed boost an item does.
                    if (_sound != null) _sound.ForceNextBoosterFast();

                    // The same call an item boost makes, so the start boost drives
                    // the booster sound and the wide field of view identically.
                    KartDynamics.TimedBoostStart(
                        ref state.TimedBoost, 1f, KartCountdown.StartBoostDurationMs);
                }
            }

            KartVec3 velocityBefore = state.LinearVelocity;
            KartSimulation.SimulateMilliseconds(state, _controls, Ground, BodyWorld, elapsedMs);

            float seconds = elapsedMs * 0.001f;
            state.Acceleration = seconds > 0f
                ? (state.LinearVelocity - velocityBefore) * (1f / seconds)
                : KartVec3.Zero;

            // The gauge integrates the same rear-axle slip the tire model uses, so
            // it needs the body-axis split of the velocity.
            state.GetBodyAxes(out KartVec3 bodyRight, out _, out _);
            _gauge.Step(
                state,
                KartVec3.Dot(state.LinearVelocity, bodyRight),
                KartGauge.DriftVisualActive(state),
                _kart != null ? _kart.ToSpec().MaxBoosters : KartDemoData.DefaultMaxBoosters,
                seconds);

            StepCourse(state);
            RespawnIfFallenThrough(state, deltaTime);

            if (_kartView != null) _kartView.Apply(state);

            // After the step and after the kart has moved, so a mark is laid at
            // the pose it is actually drawn at rather than the previous frame's.
            if (_skidMarks != null) _skidMarks.Step(state, Ground, elapsedMs);
            if (_boostFlame != null) _boostFlame.Step(state);
            if (_sound != null) _sound.Step(state, _raceClockMs);

            // After the sound, which is what chose the take.
            if (_kartView != null && _sound != null) _kartView.SetSpinSlow(_sound.BoosterSlow);

            // After the sound driver, the way the original orders it: the gearbox
            // only re-pitches the engine loop the driver already opened, so Single
            // leaves the recovered note untouched and Multi replaces it with its
            // own sawtooth.
            _gearbox.Step(state.Speed, seconds);
            if (_gearbox.Mode == KartGearMode.Multi && _sound != null)
            {
                _sound.OverrideMotorPitch(_gearbox.Pitch);
            }

            if (_chaseCamera != null)
            {
                // The chase camera widens its field of view on the kart's
                // booster-like flag, which is exactly "any boost active". Set
                // whether or not it is the installed cameraman, so it is already
                // right when the race hands it back.
                _chaseCamera.WideView =
                    KartDynamics.AnyBoostActive(state.TimedBoost, state.InstantBoost);
            }

            // The phase may have changed this frame, so the slot is chosen after
            // the step rather than before it.
            ApplyViewMode();
            _cameras.Step(state, elapsedMs);

            Stepped?.Invoke();
        }

        /// <summary>
        /// Gates the item boost on a gauge charge.
        ///
        /// Three conditions have to hold before a charge is spent, and all three
        /// are the original's:
        ///
        /// <list type="bullet">
        /// <item><c>!_boostPressAllowed</c> — one charge per grant, so holding the
        /// key does not drain the gauge.</item>
        /// <item><c>!TimedBoost.Active</c> — <b>one booster at a time</b>. Without
        /// this, pressing again while a boost is still running spends the next
        /// charge on top of it and two or three go at once.</item>
        /// <item><c>ForwardInput != 0</c> — the engine only starts an item boost
        /// while accelerating, so the charge is spent at the moment it can fire
        /// rather than on a press that cannot.</item>
        /// </list>
        ///
        /// Retried every frame the key is held rather than only on the press
        /// edge: a hold that could not fire yet — no charge, or off the throttle —
        /// fires as soon as it can.
        ///
        /// <see cref="NoDelayBoost"/> drops the first condition and only that one.
        /// The hold then re-arms itself, so the frame a boost's 3000 ms runs out
        /// the next charge is taken and the two run back to back for as long as
        /// there are charges left. <b>The one-at-a-time condition stays</b>: without
        /// it the retry fires every frame instead of every boost, and a full gauge
        /// empties in three frames with nothing to show for it.
        /// </summary>
        private void SpendGaugeCharge()
        {
            bool held = _controls.BoostActive;

            if (!held)
            {
                _boostPressAllowed = false;
            }
            else if ((NoDelayBoost || !_boostPressAllowed) &&
                     !State.TimedBoost.Active &&
                     _controls.ForwardInput != 0f)
            {
                _boostPressAllowed = _gauge.TakeBooster();
            }

            _controls.BoostActive = held && _boostPressAllowed;
        }

        /// <summary>
        /// Walks the kart's trail segment past the checkpoint gates.
        ///
        /// The original walks every segment of the kart's position trail; one
        /// simulation step is one segment of that trail. A "warpnext" plane is
        /// checked first and replaces the step: on the one track that has one
        /// (ice_R01's lift) the kart is moved rather than advanced, so feeding
        /// that jump to the gate test would read as a crossing of everything in
        /// between.
        /// </summary>
        private void StepCourse(KartSimulationState state)
        {
            if (!CourseReady)
            {
                _previousPosition = state.Position;
                return;
            }

            if (_course.WarpNext(_previousPosition, state.Position,
                                 out KartVec3 destination, out float yaw))
            {
                state.Position = destination;
                state.Orientation = PreRotateZ(state.Orientation, yaw);

                // The 2004 game sets the kart down stopped at the top of
                // ice_R01's lift and lets the player pull away again. The
                // reference C port does not: it carries the velocities through
                // the warp, turned by the same yaw. Stopping and holding is the
                // deliberate divergence — the course tables carry no duration, so
                // the second below is a chosen number rather than a recovered one.
                Stop(state);
                _warpHold = WarpHoldSeconds;
            }
            else
            {
                KartCourseProgress.Step(
                    _course, ref _progress, _previousPosition, state.Position,
                    state.Orientation, state.LinearVelocity, _raceClockMs);
            }

            _previousPosition = state.Position;
            AnnounceLap();
            FinishIfRaceIsRun();
        }

        /// <summary>
        /// Raises <see cref="LapStarted"/> once per lap, and plays the final lap
        /// cue with it.
        ///
        /// The cue is the original's: the lap branch of the time challenge's state
        /// machine opens the <c>etc</c> sound archive and plays <c>ufo_lab</c> when
        /// the lap counter reaches the course's lap count.
        ///
        /// A one-lap course has no final lap to announce. Its counter turns over
        /// to 1 on the crossing that starts the race, so the reached-the-last-lap
        /// test is true on the start line and the cue fires under the countdown —
        /// village_R03 and northeu_R01 both did this. The banner already declines
        /// the same way, in <c>RaceBannerDisplay.ShowLap</c>: there is no lap to
        /// call the last one when the whole race is one lap.
        /// </summary>
        private void AnnounceLap()
        {
            if (_progress.Lap == _announcedLap) return;

            _announcedLap = _progress.Lap;
            if (_progress.Lap == 0u) return;

            LapStarted?.Invoke(_progress.Lap);

            if (_flow.Phase == KartRacePhase.Running &&
                LapCount >= 2u && _progress.Lap == LapCount && _sound != null)
            {
                _sound.PlayFinalLap();
            }
        }

        /// <summary>
        /// The original's finish test, <c>0x00424800</c>: the kart's lap counter past
        /// the course's lap count.
        ///
        /// <code>return *(uint *)(course + 0x60) &lt; lap;</code>
        ///
        /// where <c>+0x60</c> is the lap count and the compared value is the kart's
        /// own record slot 7 — the same counter <see cref="KartCourseProgress.Lap"/>
        /// holds. It reads one ahead because the counter turns over to 1 on the
        /// crossing that starts the first lap, so a three-lap race finishes at 4.
        ///
        /// Checked every frame, as the stage's state machine checks it, rather than
        /// only on the frame a gate was crossed.
        /// </summary>
        private void FinishIfRaceIsRun()
        {
            if (_course == null || _course.LapCount == 0u) return;
            if (_progress.Lap <= _course.LapCount) return;

            if (_flow.Finish(_raceClockMs)) Finished?.Invoke();
        }

        /// <summary>The turn applied on the world side, so the kart's own spin is kept.</summary>
        private static KartQuat PreRotateZ(KartQuat value, float radians)
        {
            float half = radians * 0.5f;
            var turn = new KartQuat(Mathf.Cos(half), 0f, 0f, Mathf.Sin(half));
            return new KartQuat(
                turn.W * value.W - turn.X * value.X - turn.Y * value.Y - turn.Z * value.Z,
                turn.W * value.X + turn.X * value.W + turn.Y * value.Z - turn.Z * value.Y,
                turn.W * value.Y - turn.X * value.Z + turn.Y * value.W + turn.Z * value.X,
                turn.W * value.Z + turn.X * value.Y - turn.Y * value.X + turn.Z * value.W);
        }

        /// <summary>
        /// The <c>R</c> key. The original has no teleport-to-the-line key: its
        /// reset command and a fall out of the world both arm the same timer, and
        /// 500 ms later put the kart back on the node it is already in. Without a
        /// course to respawn onto, this falls back to the full reset.
        /// </summary>
        public void RequestRespawn()
        {
            if (!CourseReady)
            {
                ResetSimulation();
                return;
            }
            if (_respawnDelay >= 0f) return;

            _respawnDelay = RespawnDelaySeconds;
            RespawnNoticeSeconds = 2f;
        }

        /// <summary>
        /// Arms the respawn for a kart that has left the world, and fires whatever
        /// is already armed once its delay is up.
        /// </summary>
        private void RespawnIfFallenThrough(KartSimulationState state, float deltaTime)
        {
            if (RespawnNoticeSeconds > 0f)
            {
                RespawnNoticeSeconds = Mathf.Max(0f, RespawnNoticeSeconds - deltaTime);
            }

            TrackSpec trackSpec = _track != null ? _track.ToSpec() : KartDemoData.DefaultTrack;

            if (state.Position.Z < KartTrackStart.FallLimit(trackSpec) && _respawnDelay < 0f)
            {
                if (CourseReady)
                {
                    _respawnDelay = RespawnDelaySeconds;
                    RespawnNoticeSeconds = 2f;
                }
                else
                {
                    // No course to come back onto, so the whole simulation goes
                    // back to the start line instead.
                    ApplyStartPose(trackSpec);
                    Stop(state);
                    if (_skidMarks != null) _skidMarks.Clear();
                    RespawnNoticeSeconds = 1.5f;
                    _previousPosition = state.Position;
                }
            }

            if (_respawnDelay < 0f) return;

            _respawnDelay -= deltaTime;
            if (_respawnDelay > 0f) return;
            _respawnDelay = -1f;

            if (!_course.RespawnPose(_progress, out KartVec3 position, out KartQuat orientation))
            {
                return;
            }

            state.Position = position;
            state.Orientation = orientation;
            Stop(state);
            _previousPosition = position;

            if (_skidMarks != null) _skidMarks.Clear();
            if (_chaseCamera != null) _chaseCamera.ResetFollow();
        }

        /// <summary>
        /// The original writes the respawn pose and zeroes the velocities with it,
        /// which is why the kart lands stopped rather than carrying its fall into
        /// the road it reappears on.
        /// </summary>
        private static void Stop(KartSimulationState state)
        {
            state.LinearVelocity = KartVec3.Zero;
            state.AngularVelocity = KartVec3.Zero;
            state.Acceleration = KartVec3.Zero;
        }

        /// <summary>The original drives its window from a 16 ms timer.</summary>
        public const float SimulatorTickSeconds = 1f / 60f;

        /// <summary>
        /// Runs a second of simulation with no input so the suspension is already
        /// at its resting compression when the scene opens, instead of the kart
        /// visibly dropping onto the plane on the first play frame.
        /// </summary>
        private void SettleOnGround()
        {
            var idle = new KartSimulationControls();
            for (int i = 0; i < 60; ++i)
            {
                KartSimulation.SimulateMilliseconds(_state, idle, Ground, BodyWorld, 16u);
            }
            _state.LastStep = default;
            _state.Acceleration = KartVec3.Zero;
        }

        /// <summary>
        /// Puts the scene's rigs into the director's slots.
        ///
        /// Only two of the four are filled: the ready sweep and the surround orbit
        /// are not recovered yet, and an empty slot falls back to the chase camera,
        /// which is exactly what the port did before there was a director at all.
        /// </summary>
        private void InstallCameramen()
        {
            // Through Unity's own null comparison, so a rig that was destroyed or
            // never assigned empties the slot rather than filling it with an object
            // the director would then compare by reference.
            _cameras.Install(KartCameraSlot.Chase, _chaseCamera != null ? _chaseCamera : null);
            _cameras.Install(KartCameraSlot.TopDown, _topDownCamera != null ? _topDownCamera : null);
            _cameras.Install(KartCameraSlot.Surround, _surroundCamera != null ? _surroundCamera : null);
            _cameras.Install(KartCameraSlot.Ready, _readyCamera != null ? _readyCamera : null);
        }

        /// <summary>
        /// Installs the cameraman the race asks for, unless the debug top-down view
        /// is on, which overrides it.
        ///
        /// Called every frame: the phase changes under it, and selecting a slot that
        /// is already installed does nothing.
        /// </summary>
        private void ApplyViewMode()
        {
            InstallCameramen();

            // Only which camera is live. The clear mode, the skybox and the
            // lighting are URP's own defaults and are left alone.
            KartCameraSlot slot = _viewMode == SimulatorViewMode.TopDown
                ? KartCameraSlot.TopDown
                : KartCameraDirector.SlotFor(_flow.Phase);

            _cameras.Select(slot, _state);
        }
    }
}
