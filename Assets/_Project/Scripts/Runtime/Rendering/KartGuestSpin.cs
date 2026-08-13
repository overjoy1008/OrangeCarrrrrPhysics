using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// A guest model's second look, switched on while the booster is lit.
    ///
    /// The OIIA cat ships two shapes in one mesh — sitting, and its spin pose —
    /// and a spin to go with them. Nothing about that is recovered: the 2004
    /// engine has no morph targets and no kart that changes shape, so this is a
    /// guest's own behaviour and it hangs off the guest model rather than off
    /// anything in the simulation. The kart it is attached to drives exactly like
    /// any other; only its appearance knows about this.
    ///
    /// The trigger is the same one <see cref="BoostFlame"/> uses, so the spin, the
    /// flame and the booster sound all start on the same frame.
    ///
    /// Both the morph and the spin ride one ramp. Snapping the shape on the frame
    /// a boost starts reads as the model being swapped for a different one; taking
    /// a sixth of a second over it reads as the cat winding up.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/Kart Guest Spin")]
    public sealed class KartGuestSpin : MonoBehaviour
    {
        [Tooltip("Blend shape taken to 100 while boosting. Empty for a model that only spins.")]
        [SerializeField] private string _blendShape;

        [Tooltip("Degrees per second about the model's up axis at full ramp.")]
        [SerializeField] private float _degreesPerSecond = 1440f;

        [Tooltip("Seconds to wind fully in, and the same to wind out.")]
        [SerializeField] private float _rampSeconds = 0.15f;

        [Tooltip("Speed multiplier while the booster's slow take is playing.")]
        [SerializeField] private float _slowScale = 0.5f;

        /// <summary>
        /// Set by the simulator when the booster picked its slow take, so the
        /// turn matches what is being sung. Nothing here decides it: the sound
        /// rolls the dice and this follows.
        /// </summary>
        public bool Slow { get; set; }

        private SkinnedMeshRenderer _renderer;
        private int _blendShapeIndex = -1;
        private bool _resolved;

        private float _ramp;
        private float _angle;

        /// <summary>0 at rest, 1 at full spin. Ramped, so it lags the booster.</summary>
        public float Ramp => _ramp;

        /// <summary>Editor-time setup, for the importer that bakes this onto a prefab.</summary>
        public void Configure(
            string blendShape, float degreesPerSecond, float rampSeconds, float slowScale)
        {
            _blendShape = blendShape;
            _degreesPerSecond = degreesPerSecond;
            _rampSeconds = rampSeconds;
            _slowScale = slowScale > 0f ? slowScale : 1f;
            _resolved = false;
        }

        /// <summary>
        /// One frame of the effect. Driven by <see cref="KartView"/> rather than
        /// by Update, so it follows the simulation's own clock and a paused
        /// simulator holds the pose instead of spinning on.
        /// </summary>
        public void Step(KartSimulationState kart, float deltaTime)
        {
            Resolve();

            bool boosting = kart != null &&
                KartDynamics.AnyBoostActive(kart.TimedBoost, kart.InstantBoost);

            float step = _rampSeconds > 0f ? deltaTime / _rampSeconds : 1f;
            _ramp = Mathf.MoveTowards(_ramp, boosting ? 1f : 0f, step);

            if (_blendShapeIndex >= 0 && _renderer != null)
            {
                _renderer.SetBlendShapeWeight(_blendShapeIndex, _ramp * 100f);
            }

            if (_degreesPerSecond == 0f) return;

            // Wound back to zero as the ramp falls, so the cat always comes to
            // rest square with the kart rather than stopping mid-turn.
            float speed = _degreesPerSecond * (Slow ? _slowScale : 1f);
            _angle = _ramp <= 0f
                ? 0f
                : Mathf.Repeat(_angle + speed * _ramp * deltaTime, 360f);

            transform.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }

        /// <summary>Puts the model back to its resting shape and heading.</summary>
        public void Reset()
        {
            _ramp = 0f;
            _angle = 0f;

            Resolve();
            if (_blendShapeIndex >= 0 && _renderer != null)
            {
                _renderer.SetBlendShapeWeight(_blendShapeIndex, 0f);
            }
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Finds the blend shape once. By name rather than by index because the
        /// index is a property of whatever order the exporter wrote the shapes
        /// in, and a reimport is free to change it.
        /// </summary>
        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            _blendShapeIndex = -1;
            if (string.IsNullOrEmpty(_blendShape)) return;

            foreach (SkinnedMeshRenderer candidate in
                     GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                Mesh mesh = candidate.sharedMesh;
                if (mesh == null) continue;

                for (int i = 0; i < mesh.blendShapeCount; ++i)
                {
                    // Exporters qualify the name — Maya writes "body.Muchkin1" for
                    // a shape called "body" — so the stored name is a fragment to
                    // look for rather than the whole of it.
                    if (mesh.GetBlendShapeName(i).IndexOf(
                            _blendShape, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    _renderer = candidate;
                    _blendShapeIndex = i;
                    return;
                }
            }

            Debug.LogWarning(
                $"'{name}': no blend shape matching '{_blendShape}', so only the spin runs.",
                this);
        }
    }
}
