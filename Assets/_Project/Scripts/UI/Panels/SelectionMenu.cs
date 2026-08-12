using System;
using System.Collections.Generic;
using OrangeCarrrrr.Core;
using OrangeCarrrrr.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrangeCarrrrr.UI
{
    /// <summary>
    /// The list <c>T</c> and <c>K</c> open: pick a track or a kart by name instead
    /// of stepping to the next one.
    ///
    /// The original has no such menu — it cycles, and the port cycled with it. A
    /// cycle is fine for two tracks and tedious for fourteen, and useless for
    /// twenty-six karts, so this is a port convenience rather than a recovered
    /// screen. It is deliberately built to look like the rest of the HUD: the same
    /// panel fill and border, the same font, the same dim/bright pair.
    ///
    /// It builds its own rows at runtime. The number of them is a property of the
    /// catalog rather than of the layout — fourteen tracks, twenty-six karts — so
    /// authoring them into the HUD prefab would only be a list to keep in step
    /// with a list.
    /// </summary>
    [AddComponentMenu("OrangeCarrrrr/HUD/Selection Menu")]
    public sealed class SelectionMenu : MonoBehaviour
    {
        public const float PanelWidth = 560f;
        public const float RowHeight = 22f;
        public const float Padding = 14f;

        /// <summary>Rows on screen at once; a longer list scrolls inside them.</summary>
        public const int VisibleRows = 12;

        private PanelBox _panel;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _hint;
        private readonly List<TextMeshProUGUI> _rows = new List<TextMeshProUGUI>();

        private IReadOnlyList<string> _entries;
        private Action<int> _chosen;
        private int _index;
        private int _scroll;
        private TMP_FontAsset _font;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// The simulator the lists act on. Set by the HUD; resolved from the scene
        /// if it was not.
        /// </summary>
        [SerializeField] private SimulatorRoot _simulator;

        /// <summary>
        /// A beat between the result times and this list opening.
        ///
        /// The race's own timings are recovered — the times come up three seconds
        /// after the finish and the original leaves its stage at eight — but where
        /// the original cuts to another screen this port opens a menu over the one
        /// that is already there, and landing it straight onto the result reads as
        /// an interruption. The pause is the port's, and it is here to be tuned.
        /// </summary>
        [Tooltip("Seconds between the race's exit cue and the track list opening.")]
        [SerializeField, Range(0f, 5f)] private float _raceExitDelaySeconds = 1.5f;

        /// <summary>Counts that beat down; negative when nothing is pending.</summary>
        private float _raceExitCountdown = -1f;

        public void Bind(SimulatorRoot simulator) => _simulator = simulator;

        /// <summary>
        /// <c>T</c> and <c>K</c> live here rather than with the other keys because
        /// the menu they open has to swallow the keyboard for as long as it is up,
        /// and the simulator's key handler stands down on
        /// <see cref="SimulatorRoot.MenuOpen"/> while it does.
        /// </summary>
        private void Update()
        {
            if (!Application.isPlaying) return;

            if (_simulator == null) _simulator = FindFirstObjectByType<SimulatorRoot>();
            if (_simulator == null) return;
            Subscribe(_simulator);
            StepRaceExit();

            if (Step())
            {
                _simulator.MenuOpen = true;
                return;
            }
            _simulator.MenuOpen = false;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.tKey.wasPressedThisFrame) OpenTracks();
            else if (keyboard.kKey.wasPressedThisFrame) OpenKarts();
        }

        /// <summary>
        /// The simulator whose race this menu is listening to. Resolved in
        /// <see cref="Update"/> rather than in a lifecycle method, because that is
        /// where the reference itself is resolved.
        /// </summary>
        private SimulatorRoot _subscribed;

        private void Subscribe(SimulatorRoot simulator)
        {
            if (ReferenceEquals(_subscribed, simulator)) return;

            if (_subscribed != null) _subscribed.RaceExitDue -= OnRaceExitDue;
            _subscribed = simulator;
            if (_subscribed != null) _subscribed.RaceExitDue += OnRaceExitDue;
        }

        private void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.RaceExitDue -= OnRaceExitDue;
            _subscribed = null;
        }

        /// <summary>
        /// Eight seconds after the finish.
        ///
        /// The original leaves for <c>SelectChallengeStage</c> here. This port has no
        /// challenge select, and the track list is the nearest thing it has: it is
        /// what the player would be picking from next either way.
        /// </summary>
        private void OnRaceExitDue()
        {
            if (IsOpen || _raceExitCountdown >= 0f) return;
            _raceExitCountdown = _raceExitDelaySeconds;
        }

        /// <summary>Opens the list once the beat after the exit cue has passed.</summary>
        private void StepRaceExit()
        {
            if (_raceExitCountdown < 0f) return;

            _raceExitCountdown -= Time.deltaTime;
            if (_raceExitCountdown > 0f) return;

            _raceExitCountdown = -1f;
            if (!IsOpen) OpenTracks();
        }

        private void OpenTracks()
        {
            TrackCatalog catalog = _simulator.Tracks;
            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogWarning("No track catalog to choose from.", this);
                return;
            }

            var names = new string[catalog.Count];
            int current = 0;
            for (int i = 0; i < catalog.Count; ++i)
            {
                TrackSpecAsset track = catalog.At(i);
                names[i] = track != null
                    ? $"{track.DisplayName}   ({track.AssetName})   {track.RaceMode}{Mark(track.Source)}"
                    : "-";
                if (track == _simulator.Track) current = i;
            }

            _simulator.MenuOpen = true;
            Open("SELECT TRACK", names, current, index => _simulator.LoadTrack(catalog.At(index)));
        }

        private void OpenKarts()
        {
            KartCatalog catalog = _simulator.Karts;
            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogWarning("No kart catalog to choose from.", this);
                return;
            }

            var names = new string[catalog.Count];
            int current = 0;
            for (int i = 0; i < catalog.Count; ++i)
            {
                KartSpecAsset kart = catalog.At(i);
                names[i] = kart != null
                    ? $"{kart.AssetName}   {kart.Width:F3} x {kart.Length:F3}{Mark(kart.Source)}"
                    : "-";
                if (kart == _simulator.Kart) current = i;
            }

            _simulator.MenuOpen = true;
            Open("SELECT KART", names, current, index => _simulator.SelectKart(catalog.At(index)));
        }

        /// <summary>
        /// The tag a row carries when it is not the demo's.
        ///
        /// The 2004 content is unmarked because it is the baseline; anything from
        /// the later client says so, so that picking one is a deliberate act and
        /// not something that happens by accident in a list of look-alikes.
        /// </summary>
        private static string Mark(KartAssetSource source)
            => source == KartAssetSource.Demo ? string.Empty : "   [TC]";

        /// <summary>
        /// Opens the list. <paramref name="chosen"/> is called with the picked
        /// index, and not at all when the menu is cancelled.
        /// </summary>
        public void Open(string title, IReadOnlyList<string> entries, int current, Action<int> chosen)
        {
            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning($"{title}: nothing to choose from.", this);
                return;
            }

            _entries = entries;
            _chosen = chosen;
            _index = Mathf.Clamp(current, 0, entries.Count - 1);
            _scroll = Mathf.Clamp(_index - VisibleRows / 2, 0, Mathf.Max(0, entries.Count - VisibleRows));

            // Primed from the keyboard as it is right now, so an arrow that was
            // already down — the player was steering when they hit T — is not read
            // as a fresh press and does not move the cursor off the current entry.
            Keyboard keyboard = Keyboard.current;
            _arrow = 0;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed) _arrow = -1;
                if (keyboard.downArrowKey.isPressed) _arrow = 1;
                if (keyboard.upArrowKey.isPressed && keyboard.downArrowKey.isPressed) _arrow = 0;
            }
            _repeat = RepeatDelaySeconds;

            Build();
            if (_title != null) _title.SetText(title);
            IsOpen = true;

            // Last sibling, so the list draws over everything else on the canvas.
            // It matters at the end of a race: the menu opens eight seconds after
            // the finish, while WINNER and the times are still up, and a canvas
            // draws its children in hierarchy order — whoever was created last
            // would otherwise sit on top.
            transform.SetAsLastSibling();

            Show(true);
            Paint();
        }

        public void Close()
        {
            IsOpen = false;
            _entries = null;
            _chosen = null;
            _arrow = 0;
            Show(false);
        }

        /// <summary>
        /// Hides the graphics rather than the object. The object carries the key
        /// handler that opens the menu, so deactivating it would close the menu
        /// once and never let it open again.
        /// </summary>
        private void Show(bool visible)
        {
            if (_panel != null) _panel.enabled = visible;
            if (_title != null) _title.enabled = visible;
            if (_hint != null) _hint.enabled = visible;
            foreach (TextMeshProUGUI row in _rows) row.enabled = visible;
        }

        /// <summary>
        /// One frame of menu input. Returns true while the menu owns the keyboard,
        /// so the caller can leave the driving keys alone.
        /// </summary>
        public bool Step()
        {
            if (!IsOpen) return false;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return true;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return true;
            }

            int steps = HeldArrowSteps(keyboard);
            for (int i = 0; i < steps; ++i) Move(_arrow);

            if (keyboard.pageUpKey.wasPressedThisFrame) Move(-VisibleRows);
            if (keyboard.pageDownKey.wasPressedThisFrame) Move(VisibleRows);

            if (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
            {
                Action<int> chosen = _chosen;
                int index = _index;
                Close();

                // After Close, so a callback that loads a scene does not come back
                // to a menu that is still holding the keyboard.
                chosen?.Invoke(index);
            }
            return true;
        }

        // -------------------------------------------------------------- moving

        /// <summary>
        /// How long an arrow has to be held before the list starts moving on its
        /// own, and how fast it moves after that. The pause is what makes a single
        /// tap a single step; without it a held key and a tap are the same thing.
        /// </summary>
        public const float RepeatDelaySeconds = 0.35f;

        public const float RepeatIntervalSeconds = 0.06f;

        private int _arrow;
        private float _repeat;

        /// <summary>
        /// The number of steps the held arrow owes this frame: one on the press,
        /// then one per interval once the delay is up.
        ///
        /// Counted rather than capped at one per frame so the speed is the same
        /// whatever the frame rate — at 200 fps the interval is several frames
        /// long, and at 20 it is shorter than one.
        /// </summary>
        private int HeldArrowSteps(Keyboard keyboard)
        {
            int held = 0;
            if (keyboard.upArrowKey.isPressed) held = -1;
            if (keyboard.downArrowKey.isPressed) held = 1;

            // Both directions at once, or a release: the list stops and the next
            // press starts the delay over.
            if (keyboard.upArrowKey.isPressed && keyboard.downArrowKey.isPressed) held = 0;

            if (held != _arrow)
            {
                _arrow = held;
                _repeat = RepeatDelaySeconds;
                return held != 0 ? 1 : 0;
            }
            if (held == 0) return 0;

            // Unscaled: the menu is not part of the simulation, and it holds the
            // kart still while it is up.
            _repeat -= Time.unscaledDeltaTime;
            if (_repeat > 0f) return 0;

            int steps = 0;
            while (_repeat <= 0f && steps < _entries.Count)
            {
                _repeat += RepeatIntervalSeconds;
                ++steps;
            }
            return steps;
        }

        private void Move(int by)
        {
            if (by == 0) return;

            int count = _entries.Count;

            // Wrapping on the single steps only: a page that ran off the end and
            // reappeared at the top would lose the reader's place.
            _index = Mathf.Abs(by) == 1
                ? (_index + by + count) % count
                : Mathf.Clamp(_index + by, 0, count - 1);

            _scroll = Mathf.Clamp(_scroll, Mathf.Max(0, _index - VisibleRows + 1), _index);
            _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, count - VisibleRows));
            Paint();
        }

        // ------------------------------------------------------------- drawing

        private void Paint()
        {
            for (int row = 0; row < _rows.Count; ++row)
            {
                int entry = _scroll + row;
                bool used = entry < _entries.Count;
                _rows[row].enabled = used;
                if (!used) continue;

                bool selected = entry == _index;
                _rows[row].SetText(selected ? $"> {_entries[entry]}" : $"  {_entries[entry]}");
                _rows[row].color = selected ? HudPalette.StatusText : HudPalette.StatusDim;
            }

            if (_hint == null) return;
            _hint.SetText(_entries.Count > VisibleRows
                ? $"{_index + 1}/{_entries.Count}   arrows: move   enter: select   esc: cancel"
                : "arrows: move   enter: select   esc: cancel");
        }

        private void Build()
        {
            if (_panel != null) return;

            // The HUD's own font, taken from whatever label is already on the
            // canvas: it carries the Korean fallback the track names need, and
            // matching it is the whole point of the look.
            var sample = GetComponentInParent<Canvas>() != null
                ? GetComponentInParent<Canvas>().GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)
                : null;
            _font = sample != null ? sample.font : null;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(
                PanelWidth, Padding * 2f + RowHeight * (VisibleRows + 2));

            _panel = gameObject.AddComponent<PanelBox>();
            _panel.color = HudPalette.TelemetryPanelFill;
            _panel.BorderColor = HudPalette.TelemetryPanelBorder;
            _panel.BorderWidth = 1f;
            _panel.raycastTarget = false;

            _title = AddLabel(0, HudPalette.StatusText, 15f);
            for (int row = 0; row < VisibleRows; ++row)
            {
                _rows.Add(AddLabel(row + 1, HudPalette.StatusDim, 14f));
            }
            _hint = AddLabel(VisibleRows + 1, HudPalette.StatusDim, 12f);
        }

        private TextMeshProUGUI AddLabel(int row, Color color, float size)
        {
            var holder = new GameObject($"Row{row:00}", typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(transform, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(Padding, 0f);
            rect.offsetMax = new Vector2(-Padding, 0f);
            rect.anchoredPosition = new Vector2(Padding, -(Padding + row * RowHeight));
            rect.sizeDelta = new Vector2(-Padding * 2f, RowHeight);

            var label = holder.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }
    }
}
