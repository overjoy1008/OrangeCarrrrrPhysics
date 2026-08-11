using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Which charging hypothesis the drift gauge is running.
    /// </summary>
    public enum KartGaugeModel
    {
        /// <summary>Any drift at all fills the gauge at once.</summary>
        Infinite = 0,

        /// <summary>Integrates the rear axle's sideways slide.</summary>
        Slip = 1,

        /// <summary>The same, weighted by where the suspension load has moved.</summary>
        Suspension = 2,
    }

    /// <summary>
    /// The drift gauge, ported from <c>kart_gauge.h</c>.
    ///
    /// This is a simulator-side layer, <em>not</em> recovered code, and the port
    /// keeps it that way. The original's charging function is not in the
    /// recovered set, so what this offers is a bench for the two standing
    /// hypotheses rather than a claim about the 2004 engine. Both integrate how
    /// fast the rear axle is sliding sideways, which the engine already computes
    /// for the rear tire:
    ///
    /// <code>
    ///   S_r = (-v_s + 0.5*w_z) / max(|v|, 5)
    ///   dG  = Kg * |v| * |S_r|
    /// </code>
    ///
    /// <see cref="KartGaugeModel.Slip"/> stops there.
    /// <see cref="KartGaugeModel.Suspension"/> multiplies by a contact weight
    /// built from the left/right compression difference, so a slide whose load
    /// has moved to the outside charges faster — holding the same speed and slip
    /// on flat ground and on a bank is what tells the two apart.
    ///
    /// In every model the full gauge is handed over as a booster when the drift
    /// <em>ends</em>, not the instant it fills, so it stays visibly full for the
    /// rest of the slide.
    /// </summary>
    public sealed class KartGauge
    {
        public const int ModelCount = 3;

        /// <summary>Gauge units per metre of rear-axle side travel.</summary>
        public float ChargeFactor = 4f;

        /// <summary>Gauge units that make one booster.</summary>
        public float FullValue = 200f;

        /// <summary>Ks and its clamp, for the suspension model's contact weight.</summary>
        public float SuspensionGain = 1f;

        public float SuspensionMax = 1f;

        public KartGaugeModel Model;
        public float Value;
        public uint Boosters;
        public bool UnlimitedBoosters;

        /// <summary>Last update's charge rate and contact weight, for the telemetry line.</summary>
        public float Rate { get; private set; }

        public float ContactWeight { get; private set; }

        public float Ratio => FullValue > 0f ? Math.Clamp(Value / FullValue, 0f, 1f) : 0f;

        public static string ModelName(KartGaugeModel model) => model switch
        {
            KartGaugeModel.Slip => "SLIP INTEGRAL",
            KartGaugeModel.Suspension => "SLIP x SUSPENSION",
            _ => "INFINITE BOOSTER",
        };

        public void NextModel()
            => Model = (KartGaugeModel)(((int)Model + 1) % ModelCount);

        public void Reset()
        {
            Value = 0f;
            Boosters = 0u;
            Rate = 0f;
            ContactWeight = 1f;
        }

        /// <summary>
        /// One frame, on the same clock the simulation used.
        /// <paramref name="driftActive"/> is the drift visual state; the gauge
        /// only charges while the kart is grounded and actually sliding.
        /// </summary>
        public void Step(
            KartSimulationState kart,
            float lateralSpeed,
            bool driftActive,
            uint maxBoosters,
            float deltaSeconds)
        {
            Rate = 0f;
            ContactWeight = 1f;

            float speed = kart.LinearVelocity.Magnitude;
            if (deltaSeconds <= 0f || !driftActive || !kart.Grounded || speed <= 5f)
            {
                // The booster is handed over when the drift ends, not the moment
                // the gauge fills. A part-charged gauge is kept as it is, and
                // filling with every slot already taken throws the booster away.
                if (Value < FullValue) return;

                Value = 0f;
                if (UnlimitedBoosters)
                {
                    if (Boosters != uint.MaxValue) Boosters += 1u;
                }
                else if (Boosters < maxBoosters)
                {
                    Boosters += 1u;
                }
                return;
            }

            float slip = (-lateralSpeed + 0.5f * kart.AngularVelocity.Z) / speed;
            float amount = speed * MathF.Abs(slip);

            if (Model == KartGaugeModel.Suspension)
            {
                ContactWeight = ContactWeightOf(kart, lateralSpeed);
                amount *= ContactWeight;
            }

            Rate = ChargeFactor * amount;
            if (Model == KartGaugeModel.Infinite) Value = FullValue;
            else Value += Rate * deltaSeconds;

            if (Value > FullValue) Value = FullValue;
        }

        /// <summary>
        /// Load moved to the outside of the slide, as a fraction. Wheel order is
        /// the simulation's: 0 front-right, 1 front-left, 2 rear-right, 3 rear-left.
        /// </summary>
        private float ContactWeightOf(KartSimulationState kart, float lateralSpeed)
        {
            float rightLoad = kart.Wheels.Compression0 + kart.Wheels.Compression2;
            float leftLoad = kart.Wheels.Compression1 + kart.Wheels.Compression3;
            float total = rightLoad + leftLoad;
            if (total <= 0.0001f) return 1f;

            // Sliding towards body right (v_s > 0) rolls the load onto the left.
            float favourable = (leftLoad - rightLoad) / total;
            if (lateralSpeed < 0f) favourable = -favourable;
            if (favourable < 0f) favourable = 0f;
            if (favourable > SuspensionMax) favourable = SuspensionMax;

            return 1f + SuspensionGain * favourable;
        }

        /// <summary>
        /// <c>drift_visual_active</c>: what the gauge and the HUD both mean by
        /// "drifting". The linger timer belongs in it — a slide that has stopped
        /// registering still counts while it runs out.
        /// </summary>
        public static bool DriftVisualActive(KartSimulationState kart)
            => kart.Drift.InputActive || kart.Drift.TriggerActive ||
               kart.Drift.SlipDetected || kart.Drift.LingerTimer > 0f;

        /// <summary>True when a press may start a booster, spending one charge.</summary>
        public bool TakeBooster()
        {
            if (Boosters == 0u) return false;
            Boosters -= 1u;
            return true;
        }
    }
}
