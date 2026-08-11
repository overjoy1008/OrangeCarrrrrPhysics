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

        /// <summary>Laid-down skid quads, as the original's <c>skids</c> read-out counts them.</summary>
        public int SkidMarkSegments => _skidMarks != null ? _skidMarks.SegmentCount : 0;

        /// <summary>The race-start countdown, as the HUD reads it.</summary>
        public KartCountdown Countdown => _countdown;

        /// <summary>Milliseconds since the countdown was armed.</summary>
        public uint RaceClockMs => _raceClockMs;

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

        /// <summary>
        /// The <c>T</c> key: the next track in the catalog.
        ///
        /// A track is a whole scene here, so switching loads one. The original
        /// swaps which embedded KTRK the renderer reads instead, but it is not
        /// carrying 375 GameObjects and a collision set per track.
        ///
        /// With two tracks this walks between them; the original's fourteen-entry
        /// popup belongs with the other twelve, not before them.
        /// </summary>
        public void NextTrack()
        {
            TrackCatalog catalog = ResolveTrackCatalog();
            if (catalog == null || catalog.Count < 2)
            {
                Debug.LogWarning("No other track to switch to.", this);
                return;
            }

            TrackSpecAsset next = catalog.Next(_track);
            if (next == null || next == _track) return;

            if (string.IsNullOrWhiteSpace(next.SceneName))
            {
                Debug.LogWarning($"Track '{next.AssetName}' names no scene.", this);
                return;
            }

            if (!Application.isPlaying) return;
            SceneManager.LoadScene(next.SceneName);
        }

        /// <summary>
        /// The <c>K</c> key: the next of the twenty-six karts.
        ///
        /// A kart is not a scene, so nothing is loaded — but its geometry is, so
        /// the simulation has to be re-initialised rather than nudged. The
        /// original resets the kart to the line on a change too.
        /// </summary>
        public void NextKart()
        {
            KartCatalog catalog = ResolveKartCatalog();
            if (catalog == null || catalog.Count < 2)
            {
                Debug.LogWarning("No other kart to switch to.", this);
                return;
            }

            KartSpecAsset next = catalog.Next(_kart);
            if (next == null || next == _kart) return;

            _kart = next;
            if (_kartView != null) _kartView.Kart = next;
            if (_boostFlame != null) _boostFlame.Kart = next;

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
            if (_boostFlame == null && _kartView != null)
            {
                _boostFlame = _kartView.GetComponentInChildren<BoostFlame>(includeInactive: true);
            }

            // Only ever created while playing, so opening the scene never adds
            // objects to it and nothing is left behind to be saved by accident.
            if (!Application.isPlaying) return;

            if (_skidMarks == null) _skidMarks = Attach<SkidMarkTrail>("Skid marks", transform);

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
            ApplyStartPose(trackSpec);

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
        /// Puts the kart where the original puts it: on the start line's road
        /// quad, facing along the course. A track with no start line spawns at
        /// the bounds centre but keeps the same facing, which is what stops the
        /// flat reference track pointing the opposite way to all thirteen real
        /// ones.
        /// </summary>
        private void ApplyStartPose(TrackSpec track)
        {
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

            if (!cues.Released)
            {
                // Held at the line: the original releases every kart on GO, so
                // until then the drive inputs are ignored and only the view keys
                // do anything.
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
