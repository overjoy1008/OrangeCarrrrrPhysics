using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The HUD's FPS reading, sampled the way <c>draw_scene</c> samples it: count
    /// frames and only publish a new figure once half a second of wall clock has
    /// gone by, so the number is readable instead of flickering.
    /// </summary>
    public sealed class FrameRateCounter
    {
        private const float SampleSeconds = 0.5f;

        private float _elapsed;
        private int _frames;

        public float FramesPerSecond { get; private set; }

        public void Tick(float deltaTime)
        {
            _elapsed += deltaTime;
            _frames += 1;
            if (_elapsed < SampleSeconds) return;

            FramesPerSecond = _frames / _elapsed;
            _elapsed = 0f;
            _frames = 0;
        }

        public void Reset()
        {
            _elapsed = 0f;
            _frames = 0;
            FramesPerSecond = 0f;
        }
    }
}
