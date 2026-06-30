using System;

namespace CivicSurvival.Core.Interfaces
{
    /// <summary>
    /// Marker: this component is an AA transaction following the proven
    /// <c>AAPlacementIntent</c> doctrine (the realized vanilla Temp/ToolApplySystem
    /// analogue). The P5 analyzer asserts, for every <see cref="ITransactionLifecycle"/>
    /// component:
    ///   (1) a non-zero stable id field tagged <see cref="TxIdAttribute"/>;
    ///   (2) target identity is int Index + int Version (Axiom 11), tagged
    ///       <see cref="TxTargetAttribute"/>;
    ///   (3) if it crosses an async boundary it has a persisted
    ///       <c>*Resolved</c> + <c>*Succeeded</c> pair;
    ///   (4) any terminal "applied" field is <see cref="TxRuntimeGuardAttribute"/>:
    ///       absent from <c>Serialize</c>, skipped in <c>Deserialize</c>,
    ///       default-false after load;
    ///   (5) reserved resources have a refund field tagged
    ///       <see cref="TxRefundAttribute"/>.
    ///
    /// Conformance contract, NOT a base class (ECS components are structs).
    /// Runtime behavior is unchanged — the attributes are zero-cost markers the
    /// analyzer reads. <c>AAPlacementIntent</c> is annotated first because it
    /// already satisfies all five (it is the known-good template the contract
    /// describes).
    /// </summary>
#pragma warning disable CA1040 // Marker interface consumed by analyzers; components stay zero-cost structs.
    public interface ITransactionLifecycle { }
#pragma warning restore CA1040

    /// <summary>(1) Non-zero stable correlation id — survives save/load and
    /// disambiguates the transaction across re-issue / multiple pendings.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TxIdAttribute : Attribute { }

    /// <summary>(2) Target identity stored as int Index + int Version (Axiom 11) —
    /// never a raw Entity, never matched by ambiguous building identity.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class TxTargetAttribute : Attribute { }

    /// <summary>(4) Terminal "applied" guard. MUST be runtime-only: not written
    /// in Serialize, skipped in Deserialize, default-false after load. Set true
    /// the moment the outcome is decided, before the deferred ECB destroy plays
    /// back — makes processing idempotent per-transaction within a frame
    /// (barriers play back after all sim ticks; at 2×–3× the entity is still
    /// alive on later ticks). After load it is default-false by design, so a
    /// save-surviving transaction is reprocessed and the persisted
    /// *Resolved/*Succeeded (whose effect IS persistent) drives the outcome.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TxRuntimeGuardAttribute : Attribute { }

    /// <summary>(5) Field that drives return of a reserved resource on reject.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class TxRefundAttribute : Attribute { }
}
