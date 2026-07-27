using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Mobilization
{
    /// <summary>
    /// Answer to "may this many people be committed right now". Three states, and the third is
    /// the whole reason the type exists: a refusal by the population owner is NOT a shortage of
    /// people, and folding it into either <see cref="Ok"/> or <see cref="InsufficientManpower"/>
    /// is what let an unmeasured city look like an exhausted one.
    ///
    /// Shape follows <c>BudgetResult</c> (<c>Services/City/CityBudgetService.cs</c>), which keeps
    /// <c>SystemUnavailable</c> apart from <c>InsufficientFunds</c> for the same reason: a caller
    /// can retry the first and must not retry the second.
    /// </summary>
    public enum ManpowerCheck
    {
        /// <summary>Default uninitialised value — never returned by an operation.</summary>
        None = 0,

        /// <summary>
        /// The pool was rebuilt on a city that answered, and it holds enough free heads.
        /// </summary>
        Ok = 1,

        /// <summary>
        /// The pool is trustworthy and the city genuinely does not have that many free heads.
        /// A legitimate denial: the player is told to find people.
        /// </summary>
        InsufficientManpower = 2,

        /// <summary>
        /// The population owner refused, so the manpower breakdown is frozen at whatever it held
        /// before and describes a city nobody has measured since. Nothing may be authorised off
        /// it, and nothing may be blamed on a shortage either — the correct caller response is to
        /// hold the action and retry, and to say "unknown" rather than "not enough".
        /// </summary>
        PoolUnmeasured = 3,
    }

    /// <summary>
    /// Read-side manpower contract for cross-domain placement validation.
    /// Implemented by MobilizationSystem so consumers do not read the
    /// Mobilization-owned singleton directly.
    ///
    /// Split by the display-versus-decision rule, the same split
    /// <c>ICityPopulationReader</c> and <c>RecordedPopulation</c> already carry:
    /// <list type="bullet">
    /// <item>a <b>display</b> asks <see cref="TryGetAvailableManpower"/> and draws a dash on
    /// <c>false</c> — never a zero, which reads as "everyone is committed";</item>
    /// <item>a <b>decision</b> asks <see cref="CanRecruit"/> and withholds itself on anything
    /// but <see cref="ManpowerCheck.Ok"/>, telling <see cref="ManpowerCheck.PoolUnmeasured"/>
    /// apart from <see cref="ManpowerCheck.InsufficientManpower"/> when it reports why.</item>
    /// </list>
    ///
    /// There is deliberately no raw <c>AvailableManpower</c> getter beside these. It existed, and
    /// every one of its five call sites read an unmeasured city as a plain number: the placement
    /// handler put it in a "not enough people" message, the crew assigner burned its one-shot
    /// notification on it, the intro shipped it on an event the air-defense quest sizes itself
    /// from. An answer that does not carry its own validity gets used without it — the lesson
    /// already recorded on <c>IPowerCapacitySnapshotReader.DispatchableMW</c>.
    /// </summary>
    [OwnedByFeatureId(FeatureIds.MobilizationName)]
    public interface IMobilizationManpowerReader
    {
        /// <summary>
        /// Free heads right now, for display. <c>false</c> means the pool could not be rebuilt
        /// because the city could not be measured — <paramref name="available"/> is zero and must
        /// not be shown as a count. A trustworthy zero is <c>true</c> with zero and means every
        /// head is committed.
        /// </summary>
        bool TryGetAvailableManpower(out int available);

        /// <summary>Whether conscription is currently active (the draft-fear amplifier).</summary>
        bool IsConscriptionActive { get; }

        /// <summary>
        /// Whether <paramref name="amount"/> heads may be committed. Non-positive amounts are
        /// <see cref="ManpowerCheck.Ok"/> — a crewless installation asks for nobody and is not a
        /// caller error.
        /// </summary>
        ManpowerCheck CanRecruit(int amount);
    }
}
