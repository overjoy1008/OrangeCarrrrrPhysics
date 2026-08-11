using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using OrangeCarrrrr.Core;
using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The <c>P</c> key: a live editor for the running kart's
    /// <see cref="KartDynamicsConfig"/> and the drift gauge's own constants,
    /// ported from <c>kart_params_win32.h</c>.
    ///
    /// It writes straight into the simulation's config copy, so every field takes
    /// effect on the next step without a reset — which is the whole point of it,
    /// and why the numbers are worth reading against the recovered defaults
    /// rather than tuning blind.
    ///
    /// The original is a Win32 dialog of edit boxes. This is IMGUI, which is the
    /// same thing for the same reason: it is a developer tool, it needs thirty-odd
    /// text fields and three buttons, and building that in uGUI would mean
    /// authoring a panel nobody looks at except while tuning.
    ///
    /// The rows are found by reflection rather than transcribed, so a field added
    /// to the config appears here without anything else being edited. Only the
    /// labels are the original's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParameterWindow : MonoBehaviour
    {
        private const float Width = 460f;
        private const float Height = 560f;

        /// <summary>The original's own labels, by the field they name.</summary>
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "Mass", "mass" },
            { "AirFriction", "air friction" },
            { "DragFactor", "drag factor" },
            { "ForwardAccelForce", "forward force" },
            { "BackwardAccelForce", "reverse force" },
            { "GripBrakeForce", "grip brake" },
            { "SlipBrakeForce", "slip brake" },
            { "MaxSteerAngleDeg", "steer angle deg" },
            { "SteerConstraint", "steer constraint" },
            { "FrontGripFactor", "front grip" },
            { "RearGripFactor", "rear grip" },
            { "DriftTriggerFactor", "trigger factor" },
            { "DriftTriggerTime", "trigger time" },
            { "DriftSlipFactor", "drift slip" },
            { "DriftEscapeForce", "escape force" },
            { "CornerDrawFactor", "corner draw" },
            { "DriftLeanFactor", "drift lean" },
            { "SteerLeanFactor", "steer lean" },
            { "JumpSpringMillionPerM", "jump spring x million" },
            { "JumpMaxCrouchDistance", "jump crouch m" },
            { "JumpGaugeSweepTime", "jump sweep s" },
            { "JumpPushDuration", "jump push s" },
            { "JumpMinEfficiency", "jump min efficiency" },
            { "JumpMaxEfficiency", "jump max efficiency" },
            { "JumpVelocityDirectionBias", "jump velocity bias" },
            { "JumpBodyUpBlend", "jump up blend" },
            { "JumpTorqueScale", "jump torque" },
            { "JumpMaxSlopeDeg", "jump slope deg" },
            { "JumpLandingCooldown", "jump cooldown" },
            { "JumpLandingDamping", "jump landing damp" },
        };

        /// <summary>The gauge's four, which the original lists after the dynamics.</summary>
        private static readonly (string Field, string Label)[] GaugeRows =
        {
            ("ChargeFactor", "gauge Kg"),
            ("FullValue", "gauge full"),
            ("SuspensionGain", "gauge Ks"),
            ("SuspensionMax", "gauge Ks max"),
        };

        private SimulatorRoot _simulator;
        private FieldInfo[] _dynamicsFields;
        private string[] _text;
        private Vector2 _scroll;
        private Rect _window = new Rect(24f, 90f, Width, Height);

        public bool Show { get; private set; }

        /// <summary>Opens or closes the window, filling the boxes on the way in.</summary>
        public void Toggle()
        {
            Show = !Show;
            if (Show) Fill();
        }

        private void Awake()
        {
            _simulator = GetComponent<SimulatorRoot>();

            // Declaration order, which is what the original's offsetof table is.
            // GetFields does not promise it, so the token orders them explicitly.
            var fields = new List<FieldInfo>(
                typeof(KartDynamicsConfig).GetFields(BindingFlags.Public | BindingFlags.Instance));
            fields.RemoveAll(field => field.FieldType != typeof(float));
            fields.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

            _dynamicsFields = fields.ToArray();
            _text = new string[_dynamicsFields.Length + GaugeRows.Length];
        }

        /// <summary>Reads the running config back into the boxes.</summary>
        private void Fill()
        {
            if (_simulator == null) return;

            object config = _simulator.State.Config;
            for (int row = 0; row < _dynamicsFields.Length; ++row)
            {
                _text[row] = Format((float)_dynamicsFields[row].GetValue(config));
            }

            KartGauge gauge = _simulator.Gauge;
            for (int row = 0; row < GaugeRows.Length; ++row)
            {
                _text[_dynamicsFields.Length + row] = Format(GaugeValue(gauge, GaugeRows[row].Field));
            }
        }

        /// <summary>Writes the boxes into the running config. Unreadable text is left alone.</summary>
        private void Apply()
        {
            if (_simulator == null) return;

            object config = _simulator.State.Config;
            for (int row = 0; row < _dynamicsFields.Length; ++row)
            {
                if (TryRead(_text[row], out float value))
                {
                    _dynamicsFields[row].SetValue(config, value);
                }
            }
            _simulator.SetDynamics((KartDynamicsConfig)config);

            KartGauge gauge = _simulator.Gauge;
            for (int row = 0; row < GaugeRows.Length; ++row)
            {
                if (TryRead(_text[_dynamicsFields.Length + row], out float value))
                {
                    SetGaugeValue(gauge, GaugeRows[row].Field, value);
                }
            }
            Fill();
        }

        /// <summary>Puts the kart's own recovered numbers back.</summary>
        private void Defaults()
        {
            if (_simulator == null) return;

            KartSpec spec = _simulator.Kart != null
                ? _simulator.Kart.ToSpec()
                : KartDemoData.DefaultKart;

            _simulator.SetDynamics(spec.Dynamics);

            var fresh = new KartGauge();
            KartGauge gauge = _simulator.Gauge;
            gauge.ChargeFactor = fresh.ChargeFactor;
            gauge.FullValue = fresh.FullValue;
            gauge.SuspensionGain = fresh.SuspensionGain;
            gauge.SuspensionMax = fresh.SuspensionMax;

            Fill();
        }

        private void OnGUI()
        {
            if (!Show) return;
            _window = GUI.Window(GetInstanceID(), _window, DrawWindow, "PARAMETERS  (P closes)");
        }

        private void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            for (int row = 0; row < _dynamicsFields.Length; ++row)
            {
                Row(Label(_dynamicsFields[row].Name), row);
            }

            GUILayout.Space(8f);
            for (int row = 0; row < GaugeRows.Length; ++row)
            {
                Row(GaugeRows[row].Label, _dynamicsFields.Length + row);
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply")) Apply();
            if (GUILayout.Button("Reload")) Fill();
            if (GUILayout.Button("Defaults")) Defaults();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, Width, 20f));
        }

        private void Row(string label, int index)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(190f));
            _text[index] = GUILayout.TextField(_text[index] ?? string.Empty);
            GUILayout.EndHorizontal();
        }

        private static string Label(string field)
            => Labels.TryGetValue(field, out string label) ? label : field;

        private static string Format(float value)
            => value.ToString("0.#####", CultureInfo.InvariantCulture);

        private static bool TryRead(string text, out float value)
            => float.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static float GaugeValue(KartGauge gauge, string field) => field switch
        {
            "ChargeFactor" => gauge.ChargeFactor,
            "FullValue" => gauge.FullValue,
            "SuspensionGain" => gauge.SuspensionGain,
            _ => gauge.SuspensionMax,
        };

        private static void SetGaugeValue(KartGauge gauge, string field, float value)
        {
            switch (field)
            {
                case "ChargeFactor": gauge.ChargeFactor = value; break;
                case "FullValue": gauge.FullValue = value; break;
                case "SuspensionGain": gauge.SuspensionGain = value; break;
                default: gauge.SuspensionMax = value; break;
            }
        }
    }
}
