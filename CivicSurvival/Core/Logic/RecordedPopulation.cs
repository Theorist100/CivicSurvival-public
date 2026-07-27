using CivicSurvival.Core.Config;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Which counter a remembered population number was taken with. A recorded measurement is
    /// only comparable to a live one taken the same way, so every stored number carries the
    /// stamp of the counter that produced it.
    ///
    /// Two members is the whole point: the mod has changed measure once (a sampled resident
    /// model gave way to the vanilla city record), and the stamp is what lets a save written
    /// before the change say "this number is not on your measure" instead of silently handing
    /// over a value that no live reading can be divided by. A third measure would be added
    /// here, not worked around at the consumers.
    /// </summary>
    public enum PopulationMeasure : byte
    {
        /// <summary>
        /// Nothing was recorded. Not a measure — the absence of one. Every number restored
        /// from a save written before measurements carried a stamp lands here, which is what
        /// makes such a number refuse every decision instead of being mistaken for a fresh one.
        /// </summary>
        None = 0,

        /// <summary>
        /// Summed over the published household selection (<c>AliveCitizensInSelection</c>) —
        /// the resident model. Used where both sides of a fraction come out of one selection
        /// (the exodus day, the crisis percentage).
        /// </summary>
        ResidentSelection = 1,

        /// <summary>
        /// The vanilla city record, <c>Game.City.Population.m_Population</c>, read through
        /// <c>ICityPopulationReader</c>. The measure every scenario-level verdict is on.
        /// </summary>
        VanillaCityRecord = 2,
    }

    /// <summary>
    /// A population number that was measured at some earlier moment and kept — a peak, the size
    /// the city entered the war at, the baseline a crisis measures against — together with the
    /// two facts a bare <c>int</c> cannot carry: whether the measurement ever happened, and
    /// which counter took it.
    ///
    /// This type replaces the <c>PopulationBaseline</c> convention that said "0 means NOT
    /// CAPTURED YET, never an empty city". The convention was correct and documented, and it
    /// was still a convention: nothing stopped the next writer from storing a truthful zero, and
    /// nothing told a consumer that the number it just restored from a save was taken with a
    /// counter that no longer exists. Both are now properties of the type:
    ///
    /// <list type="bullet">
    /// <item>absence is <see cref="Measure"/> = <see cref="PopulationMeasure.None"/>, never a
    /// value — so a genuine zero ("nobody was in a blackout that day") is storable;</item>
    /// <item>a number stamped with another measure answers <see cref="TryGetOn"/> with
    /// <c>false</c>, i.e. it takes the already-designed "not captured" route rather than being
    /// converted. There is no honest conversion between the two counters — they differ by
    /// composition (tourists, commuters, not-moved-in households), not by a constant — so this
    /// type makes the incomparability expressible instead of inventing a factor.</item>
    /// </list>
    ///
    /// Reading rule, applied at every consumer: a <b>decision</b> asks <see cref="TryGetOn"/>
    /// with the measure it is about to divide by and withholds its verdict on refusal; a
    /// <b>display</b> asks <see cref="HasValue"/>/<see cref="Value"/> and shows the number it
    /// has. Ratio helpers below refuse on their own, so no caller needs a division guard and no
    /// caller may substitute a placeholder to "make the maths work".
    /// </summary>
    public readonly struct RecordedPopulation
    {
        private RecordedPopulation(int value, PopulationMeasure measure)
        {
            Value = value;
            Measure = measure;
        }

        /// <summary>
        /// The number as it was measured. Zero when nothing was recorded — but a caller must
        /// never read that zero as a measurement: presence is <see cref="HasValue"/>, and zero
        /// is also a perfectly legal recorded value.
        /// </summary>
        public int Value { get; }

        /// <summary>The counter that produced <see cref="Value"/>.</summary>
        public PopulationMeasure Measure { get; }

        /// <summary>
        /// Whether a measurement was taken at all, on any measure. This is the display-side
        /// question: a number from a superseded counter is still a real thing that happened and
        /// is still worth printing. Decisions ask <see cref="TryGetOn"/> instead.
        /// </summary>
        public bool HasValue => Measure != PopulationMeasure.None;

        /// <summary>Nothing was ever recorded here.</summary>
        public static RecordedPopulation NotRecorded => default;

        /// <summary>
        /// Stamp a freshly taken measurement. Zero is accepted: some measurements (the count of
        /// people affected by a blackout) are truthfully zero, and refusing them here would put
        /// the old zero-means-absent convention straight back.
        /// </summary>
        public static RecordedPopulation On(PopulationMeasure measure, int value)
        {
            if (measure == PopulationMeasure.None)
                return default;

            return new RecordedPopulation(math.max(0, value), measure);
        }

        /// <summary>
        /// Rebuild a measurement read back from a save. Saves written before measurements
        /// carried a stamp hold the bare number and the convention it was stored under: a
        /// positive value was a real measurement, a zero was the "not captured" sentinel. That
        /// is enough to migrate them without inventing anything —
        /// <paramref name="measureBeforeStampsExisted"/> names the counter that build actually
        /// used, so the restored number keeps its value (displays still show it) while
        /// answering "not on your measure" to every consumer that has since moved on.
        ///
        /// Deliberately not a conversion: the number is not scaled, and the reader never
        /// pretends it was taken any other way than it was.
        /// </summary>
        public static RecordedPopulation Restore(int value, PopulationMeasure stamp, PopulationMeasure measureBeforeStampsExisted)
        {
            if (stamp != PopulationMeasure.None)
                return On(stamp, value);

            return value > 0 ? On(measureBeforeStampsExisted, value) : default;
        }

        /// <summary>
        /// Drop the measurement. An explicit operation of the contract, because "forget this
        /// number" is a real step of the crisis baseline's life cycle — it is re-taken on every
        /// entry into the Crisis act — and expressing it as a write of a zero sentinel is how
        /// the convention this type replaces would come back through the side door.
        /// </summary>
        public static RecordedPopulation Invalidate() => default;

        /// <summary>Whether this measurement was taken with <paramref name="measure"/>.</summary>
        public bool IsOn(PopulationMeasure measure) => HasValue && Measure == measure;

        /// <summary>
        /// The number, but only to a caller measuring the same way. <c>false</c> covers both
        /// "never measured" and "measured with the other counter"; the two are deliberately one
        /// answer at the call site, because a consumer can do nothing different about them —
        /// either way it has no number it may compare against its live reading.
        /// </summary>
        public bool TryGetOn(PopulationMeasure measure, out int value)
        {
            if (!IsOn(measure))
            {
                value = 0;
                return false;
            }

            value = Value;
            return true;
        }

        /// <summary>
        /// Take the measurement on the first tick the city reports anyone, and keep it.
        /// Idempotent while the stored number is on <paramref name="measure"/>, so calling this
        /// from a per-second tick is free. Returns whether a usable measurement exists after the
        /// call.
        ///
        /// A number stamped with another measure is treated as absent and overwritten: this is
        /// an anchor for an ongoing measurement ("the city as the crisis found it"), not a
        /// historical maximum, so re-anchoring it on the current counter uses only a number that
        /// was really observed. The alternative — refusing forever — would leave every
        /// remaining-population threshold of that save permanently unanswerable.
        /// </summary>
        public static bool TryCapture(int currentPopulation, PopulationMeasure measure, ref RecordedPopulation recorded)
        {
            if (recorded.IsOn(measure) && recorded.Value > 0)
                return true;
            if (currentPopulation <= 0)
                return false;

            recorded = On(measure, currentPopulation);
            return true;
        }

        /// <summary>
        /// Raise a high-water mark with a live reading. A peak is a historical maximum, so
        /// unlike <see cref="TryCapture"/> it is never re-anchored downwards: a mark inherited
        /// from another measure keeps its value (the city really did reach it) and is only
        /// superseded once a reading on <paramref name="measure"/> actually exceeds it. Until
        /// then the mark answers "not on your measure" to every decision, while displays keep
        /// showing the number the city reached.
        ///
        /// Re-taking the peak from the live count instead would set the historical maximum to
        /// the present — instantly satisfying the retention victory condition and switching the
        /// ghost-town window off for good.
        /// </summary>
        public static bool TryRaise(int currentPopulation, PopulationMeasure measure, ref RecordedPopulation peak)
        {
            if (currentPopulation <= 0)
                return peak.IsOn(measure);

            if (!peak.HasValue || currentPopulation > peak.Value)
            {
                peak = On(measure, currentPopulation);
                return true;
            }

            return peak.IsOn(measure);
        }

        /// <summary>
        /// Fraction of the recorded number still living in the city (1 = nobody left).
        /// <c>false</c> when there is no measurement on <paramref name="measure"/> to divide by
        /// — the caller must hold its verdict rather than read the missing answer as "everyone
        /// is gone".
        /// </summary>
        public bool TryGetRemainingFraction(int currentPopulation, PopulationMeasure measure, out float remaining)
        {
            if (!TryGetOn(measure, out int baseline) || baseline <= 0)
            {
                remaining = 0f;
                return false;
            }

            remaining = math.max(0, currentPopulation) / (float)baseline;
            return true;
        }

        /// <summary>
        /// City-size scaling for a remaining-population threshold. A configured share such as
        /// "15% still here" is written for a city; on a hamlet the same share is decided by a
        /// handful of people — with a baseline of twenty, one departure moves the ratio by five
        /// percent. Below <c>Scenario.VillageMaxPop</c> the bar therefore drops in proportion to
        /// the baseline, so a smaller city has to lose a larger share before the same verdict is
        /// reached.
        ///
        /// Scaling, never a cutoff: every city size can still reach the threshold, and the
        /// configured value is returned unchanged from VillageMaxPop upwards.
        /// </summary>
        public static float ScaleThresholdForCitySize(float configuredThreshold, int baseline)
        {
            int villageMaxPop = BalanceConfig.Current.Scenario.VillageMaxPop;
            if (villageMaxPop <= 0 || baseline >= villageMaxPop)
                return configuredThreshold;

            return configuredThreshold * (math.max(0, baseline) / (float)villageMaxPop);
        }

        /// <summary>
        /// Remaining fraction together with the size-scaled threshold it must be compared
        /// against. Both numbers come from one call so entry and exit conditions on the same
        /// measurement cannot drift apart. Same refusal contract as
        /// <see cref="TryGetRemainingFraction"/>.
        /// </summary>
        public bool TryGetScaledRemaining(
            int currentPopulation,
            PopulationMeasure measure,
            float configuredThreshold,
            out float remaining,
            out float scaledThreshold)
        {
            if (!TryGetRemainingFraction(currentPopulation, measure, out remaining))
            {
                scaledThreshold = 0f;
                return false;
            }

            scaledThreshold = ScaleThresholdForCitySize(configuredThreshold, Value);
            return true;
        }

        /// <summary>
        /// Fraction of the recorded number that has left (0 = nobody left). Same refusal
        /// contract as <see cref="TryGetRemainingFraction"/>.
        /// </summary>
        public bool TryGetLostFraction(int currentPopulation, PopulationMeasure measure, out float lost)
        {
            if (!TryGetRemainingFraction(currentPopulation, measure, out float remaining))
            {
                lost = 0f;
                return false;
            }

            lost = math.max(0f, 1f - remaining);
            return true;
        }
    }
}
