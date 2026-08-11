using OrangeCarrrrr.Runtime;
using UnityEngine;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// Shared plumbing for the HUD panels: find the simulator, refresh once per
    /// step, and stay quiet in edit mode when there is nothing to read.
    ///
    /// The original redraws the whole window every 16 ms tick. Here each panel
    /// refreshes on <see cref="SimulatorRoot.Stepped"/> instead, so a panel that
    /// is switched off costs nothing.
    /// </summary>
    [ExecuteAlways]
    public abstract class HudWidget : MonoBehaviour
    {
        [SerializeField] private SimulatorRoot _simulator;

        protected SimulatorRoot Simulator => _simulator;

        public void Bind(SimulatorRoot simulator)
        {
            if (_simulator == simulator) return;
            Unsubscribe();
            _simulator = simulator;
            Subscribe();
        }

        protected virtual void OnEnable()
        {
            if (_simulator == null)
            {
#if UNITY_2023_1_OR_NEWER
                _simulator = FindFirstObjectByType<SimulatorRoot>();
#else
                _simulator = FindObjectOfType<SimulatorRoot>();
#endif
            }
            Subscribe();
            Refresh();
        }

        protected virtual void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_simulator != null) _simulator.Stepped += Refresh;
        }

        private void Unsubscribe()
        {
            if (_simulator != null) _simulator.Stepped -= Refresh;
        }

        /// <summary>Pulls the current state onto the widget's visuals.</summary>
        protected abstract void Refresh();
    }
}
