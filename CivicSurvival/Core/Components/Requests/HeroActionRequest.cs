using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Systems.Requests;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Type of hero action requested by the UI.
    /// </summary>
    public enum HeroActionType : byte
    {
        /// <summary>Deploy hero unit (costs budget).</summary>
        Deploy = 0,

        /// <summary>Recall deployed hero unit.</summary>
        Recall = 1,

        /// <summary>Change hero mode while deployed.</summary>
        SetMode = 2
        // NOTE: archetype switching is a synchronous UI-thread command (pause-safe live response)
        // via HeroDeploymentSystem.TrySetHeroArchetype — it does NOT route through this
        // request, so there is no SetArchetype action here.
    }

    /// <summary>
    /// Request to deploy, recall, or change hero unit mode.
    /// Ephemeral entity pattern - created by UI, processed by HeroDeploymentSystem
    /// ([HandlesRequestKind(RequestKind.HeroAction)], after the hero split out of CognitiveStateSystem).
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct HeroActionRequest : IComponentData, ICommandRequest, IEmptySerializable
    {

        /// <summary>Type of hero action.</summary>
        public HeroActionType Action;

        /// <summary>Desired hero mode (for Deploy/SetMode actions).</summary>
        public HeroStatus Mode;
    }
}
