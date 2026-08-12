using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>Which cameraman is installed.</summary>
    public enum KartCameraSlot
    {
        /// <summary><c>KartReCameraman</c>: the opening sweep over the grid.</summary>
        Ready = 0,

        /// <summary><c>ChaseCameraman</c>: the recovered follow, live from <c>count_3</c>.</summary>
        Chase = 1,

        /// <summary><c>SurroundCameraman</c>: the orbit the finished kart is shown in.</summary>
        Surround = 2,

        /// <summary>
        /// The <c>F5</c> key's top-down projection. Not one of the original's
        /// cameramen — it is the port's own debug view, and it overrides whichever
        /// cameraman the race would otherwise have installed.
        /// </summary>
        TopDown = 3,
    }

    /// <summary>
    /// Installs one cameraman at a time, the way the original's stages do.
    ///
    /// A slot with nothing in it falls back to <see cref="KartCameraSlot.Chase"/>,
    /// which is what makes this safe to install before the cameramen it will one
    /// day switch to exist: a track whose scene has only a chase camera behaves
    /// exactly as it did, whatever phase the race is in.
    ///
    /// Not a <see cref="MonoBehaviour"/>. It holds no authored state — the rigs are
    /// the scene objects and this only decides which of them is awake — so it is
    /// owned by <see cref="SimulatorRoot"/> the way the gauge and the gearbox are,
    /// and no scene has to be re-authored to gain one.
    /// </summary>
    public sealed class KartCameraDirector
    {
        private const int SlotCount = 4;

        private readonly IKartCameraman[] _cameramen = new IKartCameraman[SlotCount];

        /// <summary>The slot asked for, which is not always the one installed.</summary>
        public KartCameraSlot Slot { get; private set; } = KartCameraSlot.Chase;

        /// <summary>The slot actually installed, after the fallback.</summary>
        public KartCameraSlot InstalledSlot { get; private set; } = KartCameraSlot.Chase;

        public IKartCameraman Active => _cameramen[(int)InstalledSlot];

        public Camera ActiveCamera => Active != null ? Active.Camera : null;

        /// <summary>Fills a slot. Passing null empties it, and it falls back again.</summary>
        public void Install(KartCameraSlot slot, IKartCameraman cameraman)
        {
            int index = (int)slot;
            if (index < 0 || index >= SlotCount) return;
            if (ReferenceEquals(_cameramen[index], cameraman)) return;

            _cameramen[index] = cameraman;

            // A cameraman arriving into the slot that is already selected has to be
            // put to work, and every other one has to be put away — including this
            // one, so a rig that was left enabled in the scene does not fight the
            // installed camera for the render.
            Apply(Slot, kart: null, force: true);
        }

        public IKartCameraman At(KartCameraSlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < SlotCount ? _cameramen[index] : null;
        }

        /// <summary>
        /// Selects a slot. Doing nothing when it is already selected is what lets
        /// the caller ask for the phase's slot every frame.
        /// </summary>
        public void Select(KartCameraSlot slot, KartSimulationState kart)
        {
            if (Slot == slot && Resolve(slot) == InstalledSlot) return;
            Apply(slot, kart, force: false);
        }

        private void Apply(KartCameraSlot slot, KartSimulationState kart, bool force)
        {
            Slot = slot;
            KartCameraSlot resolved = Resolve(slot);
            if (!force && resolved == InstalledSlot) return;

            InstalledSlot = resolved;

            for (int index = 0; index < SlotCount; ++index)
            {
                IKartCameraman cameraman = _cameramen[index];
                if (cameraman == null) continue;

                // Compared by reference rather than by index: the fallback means two
                // slots can hold the same rig, and putting it away for one of them
                // would put away the one that was just installed.
                if (ReferenceEquals(cameraman, _cameramen[(int)resolved])) continue;
                cameraman.Deactivate();
            }

            _cameramen[(int)resolved]?.Activate(kart);
        }

        /// <summary>Which cameraman actually takes the slot, once the empties are skipped.</summary>
        public KartCameraSlot Resolve(KartCameraSlot slot)
        {
            int index = (int)slot;
            if (index >= 0 && index < SlotCount && _cameramen[index] != null) return slot;
            return KartCameraSlot.Chase;
        }

        public void Step(KartSimulationState kart, uint elapsedMs)
            => Active?.Step(kart, elapsedMs);

        /// <summary>
        /// Which cameraman a phase asks for. The grid gets the ready sweep, the
        /// countdown and the race the chase, and the finished kart the surround —
        /// which is the order <c>count_3</c> and <c>finish</c> install them in.
        /// </summary>
        public static KartCameraSlot SlotFor(KartRacePhase phase) => phase switch
        {
            KartRacePhase.Ready => KartCameraSlot.Ready,
            KartRacePhase.Finished => KartCameraSlot.Surround,
            _ => KartCameraSlot.Chase,
        };
    }
}
