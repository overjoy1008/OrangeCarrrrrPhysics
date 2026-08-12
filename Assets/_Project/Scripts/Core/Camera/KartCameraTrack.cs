using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// What the track's timer does when it runs off the end, from
    /// <c>0x00492AB0</c>. The numbers are the original's, so a recovered curve can
    /// be written down with the value the binary stores.
    /// </summary>
    public enum KartTrackPlayMode
    {
        /// <summary>Wraps: <c>t = elapsed % duration</c>. The default.</summary>
        Loop = 0,

        /// <summary>Clamps to the duration and stops the timer.</summary>
        Once = 2,
    }

    /// <summary>One key: a time in milliseconds and the value at it.</summary>
    public readonly struct KartCameraKey
    {
        public readonly uint TimeMs;
        public readonly float Value;

        public KartCameraKey(uint timeMs, float value)
        {
            TimeMs = timeMs;
            Value = value;
        }
    }

    /// <summary>
    /// One animated scalar, ported from the demo's keyframe track — the class
    /// constructed at <c>0x004852C0</c> over the timer base <c>0x004928F0</c>, whose
    /// sampled value lives at <c>+0x5C</c>.
    ///
    /// The engine's key array is 12-byte elements of
    /// <c>{uint32 timeMs; uint32 elementSize; float value}</c>; the size field is
    /// written by the element constructor at <c>0x0048CC70</c> and read back as the
    /// array's stride, which is why the values sit at <c>+8</c> and not <c>+4</c>.
    /// Only the time and the value carry meaning, so that is all this holds.
    ///
    /// Interpolation is type 1, registered by <c>0x0048CBE0</c> and applied by
    /// <c>0x0048CCC0</c>:
    /// <code>out = t * to + (1 - t) * from</code>
    /// which is plain linear interpolation.
    ///
    /// The original drives the timer from the OS clock (<c>0x00478A48</c>) against a
    /// base captured on the first tick, not from the game clock — so the elapsed
    /// time handed to <see cref="Sample"/> is time since the track was started.
    /// </summary>
    public sealed class KartCameraTrack
    {
        private readonly KartCameraKey[] _keys;
        private readonly uint _durationMs;
        private readonly KartTrackPlayMode _mode;

        /// <summary>
        /// Which key the walk is past. Kept between updates the way the original
        /// keeps it at <c>+0x58</c>, and rewound when the time goes backwards —
        /// which is what a wrapped loop does every cycle.
        /// </summary>
        private int _cursor;

        private bool _running;

        /// <summary>The value of the last <see cref="Sample"/>.</summary>
        public float Value { get; private set; }

        public uint DurationMs => _durationMs;
        public KartTrackPlayMode Mode => _mode;
        public bool Running => _running;

        public KartCameraTrack(KartCameraKey[] keys, uint durationMs, KartTrackPlayMode mode)
        {
            if (keys == null || keys.Length == 0)
            {
                throw new ArgumentException("A track needs at least one key.", nameof(keys));
            }

            _keys = keys;
            _durationMs = durationMs;
            _mode = mode;
            Start();
        }

        /// <summary>Rewinds. <c>0x00485340</c> clears the cursor with the timer.</summary>
        public void Start()
        {
            _cursor = 0;
            _running = true;
            Value = _keys[0].Value;
        }

        /// <summary>
        /// Samples at <paramref name="elapsedMs"/> after the start.
        ///
        /// The wrap and the clamp are <c>0x00492AB0</c>'s; the key walk is
        /// <c>0x0048D1A0</c>'s, cursor and all.
        /// </summary>
        public float Sample(uint elapsedMs)
        {
            uint time = elapsedMs;

            if (_mode == KartTrackPlayMode.Once)
            {
                if (time >= _durationMs)
                {
                    time = _durationMs;
                    _running = false;
                }
            }
            else if (_durationMs != 0u)
            {
                time %= _durationMs;
            }
            else
            {
                time = 0u;
            }

            Value = Evaluate(time);
            return Value;
        }

        private float Evaluate(uint time)
        {
            if (_keys.Length == 1) return _keys[0].Value;

            if (time < _keys[_cursor].TimeMs) _cursor = 0;

            uint from = _keys[_cursor].TimeMs;
            uint to = from;

            int next = _cursor + 1;
            while (next <= _keys.Length - 1)
            {
                to = _keys[next].TimeMs;
                if (time <= to) break;

                ++_cursor;
                from = to;
                ++next;
            }

            // Past the last key the original holds the last value rather than
            // extrapolating.
            if (next >= _keys.Length) return _keys[_keys.Length - 1].Value;

            float span = to - from;
            float alpha = span > 0f ? (time - from) / span : 0f;
            return alpha * _keys[next].Value + (1f - alpha) * _keys[_cursor].Value;
        }
    }
}
