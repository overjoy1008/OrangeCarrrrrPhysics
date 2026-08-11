namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The two views the <c>C</c> key switches between. Both run the same
    /// simulation state; only the projection differs.
    /// </summary>
    public enum SimulatorViewMode
    {
        /// <summary>The recovered chase camera. The label the HUD prints is "CHASE".</summary>
        Chase = 0,

        /// <summary>The orthographic X-Y projection. The HUD prints "TOP-DOWN".</summary>
        TopDown = 1,
    }
}
