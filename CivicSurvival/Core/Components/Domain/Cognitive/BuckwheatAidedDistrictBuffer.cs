using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Buffer element listing districts currently under an active Buckwheat food-aid effect.
    /// Attached to the <see cref="BuckwheatSingleton"/> entity (EnsureShape), written each tick by
    /// <c>BuckwheatSystem</c> from its managed <c>m_DistrictAidExpiry</c> dictionary — the single
    /// owner of Buckwheat aid state (CIVIC175 untouched: this is a derived projection of that state,
    /// not a second writer).
    ///
    /// Read by <c>MentalHealthResolverSystem</c> (built into a district set on a worker thread,
    /// exactly like <c>InternetDisabledBuffer</c>) so the cognitive resolver can apply Buckwheat's
    /// per-stratum effect to household INFECTION — food calms the poor (strong defence) while aid
    /// parcels to the wealthy backfire (added pressure). Mirrors the internet-disabled district set.
    ///
    /// Derived / ephemeral — NOT serialized: rebuilt on the first post-load tick from the restored
    /// <c>m_DistrictAidExpiry</c> (which BuckwheatSystem does serialize).
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct BuckwheatAidedDistrictBuffer : IBufferElementData
    {
#pragma warning disable CIVIC262 // Session-local runtime district key; NOT persisted (derived buffer; persist rides DistrictKeyCodec via BuckwheatCodec)
        public int DistrictIndex;
#pragma warning restore CIVIC262
    }
}
