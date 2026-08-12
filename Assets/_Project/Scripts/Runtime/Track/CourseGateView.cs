using System.Collections.Generic;
using OrangeCarrrrr.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The <c>F3</c> key: every gate of the course graph drawn as the quad it is.
    ///
    /// Ported from <c>draw_course_gates</c>, which outlines the two triangles
    /// <c>kart_course_gate_crossing</c> tests the trail segment against, edge by
    /// edge, and draws the final gate — the one the lap counter checks — apart
    /// from the rest. Drawing the triangles rather than a marker is the point:
    /// a checkpoint here is not a radius around a point, and the only way to see
    /// why a pass counted or did not is to see the surface it was tested against.
    ///
    /// Only the gates near the camera are built. A course is a few hundred gates
    /// and the far ones are a pixel wide, so the whole graph every frame would
    /// cost more than the rest of the scene.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ScreenLineRenderer))]
    public sealed class CourseGateView : MonoBehaviour
    {
        /// <summary>How far from the camera a gate is still drawn.</summary>
        public const float DrawRadius = 260f;

        [Header("Appearance (RGB values from draw_course_gates)")]
        [SerializeField] private Color _gateColor = new Color32(90, 220, 255, 255);
        [SerializeField] private Color _finalColor = new Color32(255, 210, 90, 255);
        [SerializeField, Min(0.5f)] private float _widthPixels = 2f;

        [SerializeField] private bool _show;

        private ScreenLineRenderer _lines;
        private readonly List<Vector3> _loop = new List<Vector3>(3);

        /// <summary>The graph to draw. Set by the simulator each time it rebuilds one.</summary>
        public KartCourse Course { get; set; }

        public bool Show
        {
            get => _show;
            set => _show = value;
        }

        private void OnEnable()
        {
            _lines = GetComponent<ScreenLineRenderer>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
            => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
            => Render(camera);

        public void Render(Camera camera)
        {
            if (_lines == null) _lines = GetComponent<ScreenLineRenderer>();
            if (_lines == null || camera == null) return;

            ScreenLineBatch batch = _lines.Batch;
            batch.Clear();

            if (_show && Course != null)
            {
                KartVec3 eye = KartSpace.ToKart(camera.transform.position);
                float radiusSquared = DrawRadius * DrawRadius;

                for (int index = 0; index < Course.Gates.Length; ++index)
                {
                    KartCourseGate gate = Course.Gates[index];
                    KartVec3 offset = gate.First.A - eye;
                    if (KartVec3.Dot(offset, offset) > radiusSquared) continue;

                    Color color = gate.IsFinal ? _finalColor : _gateColor;
                    AddTriangle(batch, gate.First, color);
                    AddTriangle(batch, gate.Second, color);
                }
            }

            _lines.Rebuild(camera);
        }

        private void AddTriangle(ScreenLineBatch batch, in KartCourseTriangle triangle, Color color)
        {
            _loop.Clear();
            _loop.Add(KartSpace.ToUnity(triangle.A));
            _loop.Add(KartSpace.ToUnity(triangle.B));
            _loop.Add(KartSpace.ToUnity(triangle.C));
            batch.AddLoop(_loop, color, _widthPixels);
        }
    }
}
