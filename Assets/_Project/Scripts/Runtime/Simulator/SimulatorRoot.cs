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
        [SerializeField] private TrackSpecAsset _track;

        [Tooltip("Optional. With a track collision world the kart drives on the real scene; without it, on the flat plane.")]
        [SerializeField] private TrackCollisionWorld _trackCollision;

        [Tooltip("The T key's list. Left empty, it is resolved from the project.")]
        [SerializeField] private TrackCatalog _trackCatalog;

        [Tooltip("The K key's list. Left empty, it is resolved from the project.")]
        [SerializeField] private KartCatalog _kartCatalog;

        [Header("Views")]
        [SerializeField] private ChaseCameraRig _chaseCamera;
        [SerializeField] private TopDownCameraRig _topDownCamera;
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

        [Header("Input")]
        [Tooltip("Left empty, the component on this object is used.")]
        [SerializeField] private SimulatorDriverInput _driverInput;

        [Header("State")]
        [SerializeField] private SimulatorViewMode _viewMode = SimulatorViewMode.Chase;

        [Tooltip("F in the original: the ground-drag trigger, x4 on entry and x0.25 on exit.")]
        [SerializeField] private bool _dragTriggerActive;

        private readonly FrameRateCounter _fps = new FrameRateCounter();
        private readonly KartFlatGround _flatGround = new KartFlatGround();

        private KartCountdown _countdown;
        private uint _raceClockMs;

        private KartSimulationState _state;
        private KartSimulationControls _controls;
        private bool _startBoostPending = true;

        private KartCourse _course;
        private TrackCourseAsset _courseBuiltFrom;
        private KartCourseProgress _progress;

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

        /// <summary>The race-start countdown, as the HUD reads it.</summary>
        public KartCountdown Countdown => _countdown;

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
        /// True while a selection menu owns the keyboard. The drive inputs are
        /// blanked for as long as it does, since the menu moves on the same arrow
        /// keys the kart steers with.
        /// </summary>
        public bool MenuOpen { get; set; }

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

        /// <summary>The camera currently rendering, whichever view is active.</summary>
        public Camera ActiveCamera => _viewMode == SimulatorViewMode.TopDown
            ? (_topDownCamera != null ? _topDownCamera.Camera : null)
            : (_chaseCamera != null ? _chaseCamera.Camera : null);

        /// <summary>The <c>N</c> key: draw the checkpoint gates over the track.</summary>
        public bool ShowCheckpoints
        {
            get => _gateView != null && _gateView.Show;
            set { if (_gateView != null) _gateView.Show = value; }
        }

        public bool ShowBounds
        {
            get => _trackView != null && _trackView.ShowBounds;
            set { if (_trackView != null) _trackView.ShowBounds = value; }
        }

        /// <summary>Row of <c>colortable.xml</c>, or the shipped default.</summary>
        public int KartColourIndex => _kartView != null
            ? _kartView.ColourIndex
            : KartColorTable.DefaultIndex;

        public string KartColourName => _kartView != null
            ? _kartView.ColourName
            : KartColorTable.NameAt(KartColorTable.DefaultIndex);

        /// <summary>The <c>L</c> key: the next of the ten paints.</summary>
        public void NextKartColour()
        {
            if (_kartView != null) _kartView.NextColour();
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
            if (track == null || track == _track) return;

            if (string.IsNullOrWhiteSpace(track.SceneName))
            {
                Debug.LogWarning($"Track '{track.AssetName}' names no scene.", this);
                return;
            }

            if (!Application.isPlaying) return;
            SceneManager.LoadScene(track.SceneName);
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
        /// Switches to a kart.
        ///
        /// A kart is not a scene, so nothing is loaded — but its geometry is, so
        /// the simulation has to be re-initialised rather than nudged. The
        /// original resets the kart to the line on a change too.
        /// </summary>
        public void SelectKart(KartSpecAsset kart)
        {
            if (kart == null || kart == _kart) return;

            _kart = kart;
            if (_kartView != null) _kartView.Kart = kart;
            if (_boostFlame != null) _boostFlame.Kart = kart;

            // Half width and half length feed the suspension and the body box, so
            // the state has to be rebuilt from the new geometry.
            ResetSimulation();
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
        /// The frame-rate caps the <c>F1</c> key walks, in cycle order.
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

        /// <summary>The <c>F1</c> key.</summary>
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
            if (_boostFlame == null && _kartView != null)
            {
                _boostFlame = _kartView.GetComponentInChildren<BoostFlame>(includeInactive: true);
            }

            // Only ever created while playing, so opening the scene never adds
            // objects to it and nothing is left behind to be saved by accident.
            if (!Application.isPlaying) return;

            if (_skidMarks == null) _skidMarks = Attach<SkidMarkTrail>("Skid marks", transform);
            if (_gateView == null) _gateView = Attach<CourseGateView>("Course gates", transform);

            if (_boostFlame == null && _kartView != null)
            {
                // Parented to the kart: the triangle is built in the kart's own
                // frame, so it follows the model rather than being rebuilt every
                // frame in world space.
                _boostFlame = Attach<BoostFlame>("Boost flame", _kartView.transform);
            }

            if (_sound == null) _sound = gameObject.AddComponent<KartSoundPlayer>();
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

        /// <summary>The <c>R</c> key: back to the reset pose with a snapped camera.</summary>
        public void ResetSimulation()
        {
            KartSpec kartSpec = _kart != null ? _kart.ToSpec() : KartDemoData.DefaultKart;
            TrackSpec trackSpec = _track != null ? _track.ToSpec() : KartDemoData.DefaultTrack;

            _state ??= new KartSimulationState();
            KartSimulation.Init(_state, kartSpec.Dynamics, kartSpec.Geometry);

            _flatGround.Height = trackSpec.Minimum.Z;
            BuildCourse();
            ApplyStartPose(trackSpec);
            _previousPosition = _state.Position;
            _respawnDelay = -1f;
            _warpHold = 0f;

            // The original arms the countdown on reset and backdates it so the
            // race clock starts at zero.
            _raceClockMs = 0u;
            _countdown = default;
            _countdown.Start(_raceClockMs);

            _dragTriggerActive = false;
            _startBoostPending = true;
            _controls = default;
            _fps.Reset();

            if (DriverInput != null) DriverInput.ResetSteering();
            if (_chaseCamera != null) _chaseCamera.ResetFollow();
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
                if (_chaseCamera != null && _viewMode == SimulatorViewMode.Chase)
                {
                    _chaseCamera.Step(state, (uint)Mathf.RoundToInt(SimulatorTickSeconds * 1000f));
                }
                else if (_topDownCamera != null)
                {
                    _topDownCamera.Step(state);
                }
                Stepped?.Invoke();
                return;
            }

            float deltaTime = Time.deltaTime;
            _fps.Tick(deltaTime);

            uint elapsedMs = (uint)Mathf.Max(1, Mathf.RoundToInt(deltaTime * 1000f));
            _raceClockMs += elapsedMs;

            _controls = DriverInput != null ? DriverInput.Sample() : default;

            KartCountdownCues cues = _countdown.Update(_raceClockMs);
            if (_sound != null) _sound.PlayCountdown(cues);

            // The original lets the player rev on the line: holding drift with the
            // throttle runs the booster idle loop until GO. Read before the line
            // release blanks the inputs below.
            if (_sound != null)
            {
                _sound.SetBoosterIdle(
                    !cues.Released && _controls.DriftInput && _controls.ForwardInput != 0f);
            }

            if (_warpHold > 0f) _warpHold = Mathf.Max(0f, _warpHold - deltaTime);

            if (!cues.Released || _warpHold > 0f || MenuOpen)
            {
                // Held at the line: the original releases every kart on GO, so
                // until then the drive inputs are ignored and only the view keys
                // do anything. The same hold sets the kart down at the top of
                // ice_R01's lift, which is the one other place it arrives
                // somewhere it did not drive to, and holds it while a selection
                // menu has the arrow keys.
                _controls.ForwardInput = 0f;
                _controls.ReverseInput = 0f;
                _controls.DriftInput = false;
                _controls.BoostActive = false;
                _controls.DriveDisabled = true;
            }
            else if (_startBoostPending && _controls.ForwardInput != 0f)
            {
                _startBoostPending = false;
                if (_countdown.StartBoostGranted(_raceClockMs))
                {
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

            StepCourse(state);
            RespawnIfFallenThrough(state, deltaTime);

            if (_kartView != null) _kartView.Apply(state);

            // After the step and after the kart has moved, so a mark is laid at
            // the pose it is actually drawn at rather than the previous frame's.
            if (_skidMarks != null) _skidMarks.Step(state, Ground, elapsedMs);
            if (_boostFlame != null) _boostFlame.Step(state);
            if (_sound != null) _sound.Step(state, _raceClockMs);

            if (_viewMode == SimulatorViewMode.TopDown)
            {
                if (_topDownCamera != null) _topDownCamera.Step(state);
            }
            else if (_chaseCamera != null)
            {
                // The chase camera widens its field of view on the kart's
                // booster-like flag, which is exactly "any boost active".
                _chaseCamera.WideView =
                    KartDynamics.AnyBoostActive(state.TimedBoost, state.InstantBoost);
                _chaseCamera.Step(state, elapsedMs);
            }

            Stepped?.Invoke();
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

        private void ApplyViewMode()
        {
            // Only which camera is live. The clear mode, the skybox and the
            // lighting are URP's own defaults and are left alone.
            bool topDown = _viewMode == SimulatorViewMode.TopDown;

            if (_chaseCamera != null) _chaseCamera.gameObject.SetActive(!topDown);
            if (_topDownCamera != null) _topDownCamera.gameObject.SetActive(topDown);
        }
    }
}
