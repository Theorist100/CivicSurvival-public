using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Population
{
    /// <summary>
    /// The single owner of the vanilla city population record — <c>Game.City.Population</c> on
    /// the city entity. Named after the source, not after one measure: the record carries the
    /// resident count AND the city averages, and both are answered from one read of it, so a
    /// name like "IResidentCountReader" would invite a second reader for the averages and put
    /// the component back into general circulation.
    ///
    /// What the number is (decompile-verified, <c>CountHouseholdDataSystem</c>):
    /// <c>m_Population</c> = live citizens of moved-in households. Tourists, commuters and the
    /// dead are excluded; the homeless are included. One vanilla job writes it, every 16 ticks,
    /// and the value is serialized into the save and restored before the mod's first tick — so
    /// there is no "households are still streaming in" window and no readiness level here.
    ///
    /// Refusal is honest and two-valued only:
    /// <list type="bullet">
    /// <item><c>false</c> — the city entity does not exist, so the question has no answer.</item>
    /// <item><c>true</c> with zero — the city exists and nobody lives in it. Zero is the truth,
    /// not a missing measurement. "Died out" versus "just created" is a different question and
    /// is not answered here; it gets its own settled-city flag.</item>
    /// </list>
    ///
    /// Resolution shape: <c>ServiceRegistry.Instance.Require&lt;ICityPopulationReader&gt;()</c>
    /// through <c>??=</c> in <c>OnStartRunning</c>, and the same <c>??=</c> repeated inside any
    /// method reachable from <c>ValidateAfterLoad</c> — post-load validation runs before
    /// <c>OnStartRunning</c> (precedent: <c>MilestoneTutorialSystem.TryGetSettledCitizenCount</c>).
    /// Not a null-object contract: the generator refuses <c>out</c> parameters, and a throwing
    /// <c>Require</c> is the intended shape — the producer registers in its <c>OnCreate</c>,
    /// which precedes every consumer's post-load pass, so a failed resolve is a real wiring bug.
    ///
    /// No raw getter lives next to these two: every answer carries its own validity, or the
    /// first consumer in a hurry takes the unguarded one (the lesson of
    /// <c>IPowerCapacitySnapshotReader.DispatchableMW</c>).
    ///
    /// Both members are main-thread ECS reads (the same main-thread surface the vanilla
    /// infoview uses) and are safe to call from a system that is not the producer, from an
    /// event handler, and from non-ECS managed code on the main thread — including while the
    /// simulation is paused. They are not callable from a job or a background thread.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.PopulationName)]
    public interface ICityPopulationReader
    {
        /// <summary>
        /// People living in the city right now. <c>false</c> only when the city entity does not
        /// exist — in that state <paramref name="residents"/> is zero and must not be used; a
        /// caller that turns the refusal into a zero is deciding on an unmeasured city.
        /// </summary>
        bool TryGetResidentCount(out int residents);

        /// <summary>
        /// Average happiness and health as vanilla maintains them on the same record — the pair
        /// the player sees, on the 0..100 scale.
        ///
        /// On an empty city these are a neutral 50, not zero: vanilla writes that neutral
        /// explicitly, and it is a real reading. Do not fold it back to zero — that fold is the
        /// exact reason the managed <c>CountHouseholdDataSystem</c> accessors were rejected as a
        /// source. On refusal both values are zero precisely so that "no data" cannot be
        /// mistaken for the neutral reading.
        /// </summary>
        bool TryGetCityAverages(out int happiness, out int health);
    }
}
