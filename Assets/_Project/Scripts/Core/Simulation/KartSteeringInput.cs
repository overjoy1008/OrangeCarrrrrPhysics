namespace OrangeCarrrrr.Core
{
    public enum KartSteeringKey
    {
        Left = -1,
        Right = 1,
    }

    /// <summary>
    /// Recovered steering ownership, ported from <c>kart_input.c</c>.
    ///
    /// A new direction press overwrites the steering value; releasing a direction
    /// clears it only while that direction still owns it. Holding both keys
    /// therefore steers the way the most recent press says rather than cancelling
    /// to zero — which is what lets the original's new-cut input straighten a
    /// kart mid-drift with the opposite key.
    ///
    /// Unity's 1D Axis composite cancels instead, so this is used in its place.
    /// </summary>
    public struct KartSteeringInput
    {
        public bool LeftDown;
        public bool RightDown;
        public float Value;

        /// <summary>Returns true when the press or release changed the state.</summary>
        public bool KeyEvent(KartSteeringKey key, bool pressed)
        {
            float value = (float)key;

            if (key == KartSteeringKey.Left)
            {
                if (LeftDown == pressed) return false;
                LeftDown = pressed;
            }
            else
            {
                if (RightDown == pressed) return false;
                RightDown = pressed;
            }

            if (pressed) Value = value;
            else if (Value == value) Value = 0f;
            return true;
        }

        public void Reset()
        {
            LeftDown = false;
            RightDown = false;
            Value = 0f;
        }
    }
}
