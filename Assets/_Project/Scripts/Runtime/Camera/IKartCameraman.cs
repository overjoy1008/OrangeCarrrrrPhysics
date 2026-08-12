using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// One of the original's cameramen.
    ///
    /// The 2004 game does not switch a view flag: it installs a camera object —
    /// <c>KartReCameraman</c> on the grid, <c>ChaseCameraman</c> from the first
    /// countdown digit, <c>SurroundCameraman</c> at the finish — and that object
    /// owns the placement until the next one replaces it. Each of them keeps
    /// filtered state of its own, so which one is installed and when is part of how
    /// the game reads on screen rather than a presentation detail.
    ///
    /// A cameraman is pure view. It is handed the simulation state and never writes
    /// to it.
    /// </summary>
    public interface IKartCameraman
    {
        /// <summary>The Unity camera this cameraman places. Null when it has none.</summary>
        Camera Camera { get; }

        /// <summary>
        /// Installed as the live cameraman. Whatever filtered state it carries is
        /// its own business — the chase camera deliberately keeps its follow
        /// quaternion, since the reset that snaps it belongs to the race starting
        /// rather than to the camera being handed back.
        /// </summary>
        void Activate(KartSimulationState kart);

        /// <summary>Replaced by another cameraman.</summary>
        void Deactivate();

        /// <summary>
        /// Places the camera for one frame. <paramref name="elapsedMs"/> is the
        /// frame's own length, which the filters that run in milliseconds need.
        /// </summary>
        void Step(KartSimulationState kart, uint elapsedMs);
    }
}
