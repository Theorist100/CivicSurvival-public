using CivicSurvival.Core.Serialization;
using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Constants;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class PowerPlantDamageLog
    {
        public static readonly LogContext Log = new("PowerPlantDamage");
    }

    internal static class AirDefenseInstallationLog
    {
        public static readonly LogContext Log = new("AirDefenseInstallation");
    }

    internal static class AirDefenseCooldownLog
    {
        public static readonly LogContext Log = new("AirDefenseCooldown");
    }

    internal static class ShahedLog
    {
        public static readonly LogContext Log = new("Shahed");
    }

    internal static class BallisticLog
    {
        public static readonly LogContext Log = new("Ballistic");
    }

    /// <summary>
    /// Shahed drone - slow, audible, interceptable.
    /// The main threat type. Flies toward power infrastructure.
    ///
    /// PERSISTENT (C1): survives save/load. The gameplay fields below are serialized so an
    /// interrupted Attack wave resumes on load; render-state (MeshColor/MeshBatch/
    /// TransformFrame) and lifecycle tags (ActiveThreat/PendingDestruction) are
    /// reinitialized on load by ThreatLoadRenderReinitSystem.
    ///
    /// Written by: ThreatSpawnSystem
    /// Read by: ThreatMovementSystem, AirDefenseSystem, ThreatDamageSystem
    /// </summary>
    public struct Shahed : IComponentData, ISerializable
    {
        private const float FALLBACK_SPEED = 40f;
        /// <summary>Below this squared waypoint length the avoidance target is effectively zero.</summary>
        private const float MIN_AVOIDANCE_WAYPOINT_LENGTH_SQ = 0.0001f;
        /// <summary>Where drone appeared (map edge).</summary>
        public float3 SpawnPosition;
        /// <summary>Where drone is heading (power plant).</summary>
        public float3 TargetPosition;
        /// <summary>Target building reference (typed Index+Version).</summary>
        public BuildingRef TargetBuilding;
        /// <summary>Flight speed in m/s (40 default).</summary>
        public float Speed;
        /// <summary>Distance traveled so far.</summary>
        public float CurrentDistance;
        /// <summary>Total distance to target.</summary>
        public float TotalDistance;
        /// <summary>True if drone has arrived at target (prevents duplicate processing on 3x speed).</summary>
        public bool IsArrived;
        /// <summary>Position where drone was intercepted (for debris spawn).</summary>
        public float3 InterceptPosition;
        /// <summary>Target category for AA prioritization.</summary>
        public TargetCategory TargetCategory;

        /// <summary>Intermediate waypoint to avoid obstacles (zero = none).</summary>
        public float3 AvoidanceWaypoint;
        /// <summary>True if currently flying to avoidance waypoint.</summary>
        public bool IsAvoiding;
        /// <summary>Obstacle currently being avoided; prevents same-obstacle replan churn.</summary>
        public BuildingRef AvoidanceObstacle;
        /// <summary>Previous obstacle in the active avoidance session; prevents A/B replan ping-pong.</summary>
        public BuildingRef PreviousAvoidanceObstacle;
        /// <summary>Cooldown seconds after avoidance (prevents re-detecting same obstacle).</summary>
        public float AvoidanceCooldown;

        /// <summary>Last position checkpoint for stuck/oscillation detection.</summary>
        public float3 LastCheckpointPos;
        /// <summary>Seconds since last checkpoint update.</summary>
        public float TimeSinceCheckpoint;

        /// <summary>Current flight direction (smoothed). Zero = uninitialized (fallback to target-pos).</summary>
        public float3 CurrentDirection;

        /// <summary>Visual-only bank angle. Not serialized — recomputed by movement.</summary>
        public float BankAngle;

        /// <summary>
        /// Threat generation this threat was spawned under (ThreatGenerationClock.Current
        /// at spawn). 0 = unstamped/invalid. Rides the pipeline so impact consumers
        /// drop pre-load zombies while allowing in-flight act transitions. NOT serialized:
        /// ThreatLoadRenderReinitSystem re-stamps ThreatGenerationClock.Current on load so a
        /// restored drone is adopted into the loaded world instead of dropped as a zombie (C1).
        /// </summary>
        public int ThreatGeneration;

        private const byte SAVE_VERSION = 2;

        public void SetDefaults()
        {
            this = default;
            Speed = FALLBACK_SPEED;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 29);
                KeyedSerializer.WriteField(writer, "spX", SpawnPosition.x);
                KeyedSerializer.WriteField(writer, "spY", SpawnPosition.y);
                KeyedSerializer.WriteField(writer, "spZ", SpawnPosition.z);
                KeyedSerializer.WriteField(writer, "tpX", TargetPosition.x);
                KeyedSerializer.WriteField(writer, "tpY", TargetPosition.y);
                KeyedSerializer.WriteField(writer, "tpZ", TargetPosition.z);
                KeyedSerializer.WriteEntityField(writer, "tgtB", TargetBuilding.ToEntity());
                KeyedSerializer.WriteField(writer, "spd", Speed);
                KeyedSerializer.WriteField(writer, "cdst", CurrentDistance);
                KeyedSerializer.WriteField(writer, "tdst", TotalDistance);
                KeyedSerializer.WriteField(writer, "arr", IsArrived);
                KeyedSerializer.WriteField(writer, "ipX", InterceptPosition.x);
                KeyedSerializer.WriteField(writer, "ipY", InterceptPosition.y);
                KeyedSerializer.WriteField(writer, "ipZ", InterceptPosition.z);
                KeyedSerializer.WriteEnumByteField(writer, "cat", (byte)TargetCategory);
                KeyedSerializer.WriteField(writer, "cdX", CurrentDirection.x);
                KeyedSerializer.WriteField(writer, "cdY", CurrentDirection.y);
                KeyedSerializer.WriteField(writer, "cdZ", CurrentDirection.z);
                KeyedSerializer.WriteField(writer, "awX", AvoidanceWaypoint.x);
                KeyedSerializer.WriteField(writer, "awY", AvoidanceWaypoint.y);
                KeyedSerializer.WriteField(writer, "awZ", AvoidanceWaypoint.z);
                KeyedSerializer.WriteField(writer, "avd", IsAvoiding);
                KeyedSerializer.WriteEntityField(writer, "avOb", AvoidanceObstacle.ToEntity());
                KeyedSerializer.WriteEntityField(writer, "pvOb", PreviousAvoidanceObstacle.ToEntity());
                KeyedSerializer.WriteField(writer, "avCd", AvoidanceCooldown);
                KeyedSerializer.WriteField(writer, "cpX", LastCheckpointPos.x);
                KeyedSerializer.WriteField(writer, "cpY", LastCheckpointPos.y);
                KeyedSerializer.WriteField(writer, "cpZ", LastCheckpointPos.z);
                KeyedSerializer.WriteField(writer, "cpT", TimeSinceCheckpoint);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(Shahed)))
            { return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "spX": SpawnPosition.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spX", 0f); break;
                            case "spY": SpawnPosition.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spY", 0f); break;
                            case "spZ": SpawnPosition.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spZ", 0f); break;
                            case "tpX": TargetPosition.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpX", 0f); break;
                            case "tpY": TargetPosition.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpY", 0f); break;
                            case "tpZ": TargetPosition.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpZ", 0f); break;
                            case "tgtB": TargetBuilding = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "tgtB")); break;
                            case "spd": Speed = KeyedSerializer.ReadSafeFloat(reader, tag, "spd", 0f, 5000f, FALLBACK_SPEED); break;
                            case "cdst": CurrentDistance = KeyedSerializer.ReadSafeFloat(reader, tag, "cdst", 0f); break;
                            case "tdst": TotalDistance = KeyedSerializer.ReadSafeFloat(reader, tag, "tdst", 0f); break;
                            case "arr": IsArrived = KeyedSerializer.ReadBool(reader, tag, "arr"); break;
                            case "ipX": InterceptPosition.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "ipX", 0f); break;
                            case "ipY": InterceptPosition.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "ipY", 0f); break;
                            case "ipZ": InterceptPosition.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "ipZ", 0f); break;
                            case "cat": TargetCategory = KeyedSerializer.ReadEnumByte<TReader, TargetCategory>(reader, tag, "cat", TargetCategory.Civilian); break;
                            case "cdX": CurrentDirection.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cdX", 0f); break;
                            case "cdY": CurrentDirection.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cdY", 0f); break;
                            case "cdZ": CurrentDirection.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cdZ", 0f); break;
                            case "awX": AvoidanceWaypoint.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "awX", 0f); break;
                            case "awY": AvoidanceWaypoint.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "awY", 0f); break;
                            case "awZ": AvoidanceWaypoint.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "awZ", 0f); break;
                            case "avd": IsAvoiding = KeyedSerializer.ReadBool(reader, tag, "avd"); break;
                            case "avOb": AvoidanceObstacle = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "avOb")); break;
                            case "pvOb": PreviousAvoidanceObstacle = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "pvOb")); break;
                            case "avCd": AvoidanceCooldown = KeyedSerializer.ReadSafeFloat(reader, tag, "avCd", 0f); break;
                            case "cpX": LastCheckpointPos.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cpX", 0f); break;
                            case "cpY": LastCheckpointPos.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cpY", 0f); break;
                            case "cpZ": LastCheckpointPos.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cpZ", 0f); break;
                            case "cpT": TimeSinceCheckpoint = KeyedSerializer.ReadSafeFloat(reader, tag, "cpT", 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                    SanitizeAvoidanceStateAfterLoad();
                }
            }
            catch (System.Exception ex)
            {
                ShahedLog.Log.Error($"Deserialize {nameof(Shahed)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }

        private void SanitizeAvoidanceStateAfterLoad()
        {
            AvoidanceCooldown = SanitizeNonNegativeFinite(AvoidanceCooldown);
            TimeSinceCheckpoint = SanitizeNonNegativeFinite(TimeSinceCheckpoint);

            if (!IsFinite(AvoidanceWaypoint))
                AvoidanceWaypoint = float3.zero;
            if (!IsFinite(LastCheckpointPos))
                LastCheckpointPos = float3.zero;

            if (!IsAvoiding || math.lengthsq(AvoidanceWaypoint) <= MIN_AVOIDANCE_WAYPOINT_LENGTH_SQ)
                ClearAvoidanceSession();
        }

        private void ClearAvoidanceSession()
        {
            IsAvoiding = false;
            AvoidanceWaypoint = float3.zero;
            AvoidanceObstacle = default;
            PreviousAvoidanceObstacle = default;
            AvoidanceCooldown = 0f;
        }

        private static float SanitizeNonNegativeFinite(float value)
            => math.isfinite(value) && value > 0f ? value : 0f;

        private static bool IsFinite(float3 value)
            => math.all(math.isfinite(value));
    }

    /// <summary>
    /// Ballistic missile - fast, silent, nearly impossible to intercept.
    /// Endgame threat. Massive damage radius.
    ///
    /// PERSISTENT (C1): survives save/load (see Shahed doc). Gameplay fields serialized;
    /// render-state and lifecycle tags reinitialized on load by ThreatLoadRenderReinitSystem.
    ///
    /// Written by: ThreatSpawnSystem
    /// Read by: ThreatMovementSystem, AirDefenseSystem, ThreatDamageSystem
    /// </summary>
    public struct Ballistic : IComponentData, ISerializable
    {
        private const float FALLBACK_SPEED = 400f;
        private const float DEFAULT_IMPACT_RADIUS = 200f;

        /// <summary>Launch position (map edge).</summary>
        public float3 SpawnPosition;
        /// <summary>Target position (power infrastructure).</summary>
        public float3 TargetPosition;
        /// <summary>Target building reference (typed Index+Version).</summary>
        public BuildingRef TargetBuilding;
        /// <summary>Flight speed in m/s (400 default).</summary>
        public float Speed;
        /// <summary>Damage radius in meters (200 default).</summary>
        public float ImpactRadius;
        /// <summary>Damage severity multiplier used by ballistic impact damage.</summary>
        public float DamageSeverity;
        /// <summary>True if missile has arrived at target (prevents duplicate processing on 3x speed).</summary>
        public bool IsArrived;
        /// <summary>
        /// Runtime-only exhausted/stuck marker. Not serialized; terminal restored
        /// ballistics are purged by ThreatLoadRenderReinitSystem using IsArrived.
        /// </summary>
        public bool IsExhausted;
        /// <summary>
        /// Runtime-only guard for malformed ballistic data. Exhausted missiles
        /// normally still produce their ballistic blast when they crash; invalid
        /// input sanitization uses this flag to remove the threat without turning
        /// garbage kinematics into an area strike.
        /// </summary>
        public bool SuppressExhaustedImpact;

        /// <summary>
        /// Threat generation this missile was spawned under (ThreatGenerationClock.Current
        /// at spawn). 0 = unstamped/invalid. Rides the pipeline so impact consumers
        /// drop pre-load zombies while allowing in-flight act transitions. NOT serialized:
        /// ThreatLoadRenderReinitSystem re-stamps ThreatGenerationClock.Current on load (C1).
        /// </summary>
        public int ThreatGeneration;

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
            Speed = FALLBACK_SPEED;
            ImpactRadius = DEFAULT_IMPACT_RADIUS;
            DamageSeverity = ThreatConstants.BALLISTIC_IMPACT_SEVERITY;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 11);
                KeyedSerializer.WriteField(writer, "spX", SpawnPosition.x);
                KeyedSerializer.WriteField(writer, "spY", SpawnPosition.y);
                KeyedSerializer.WriteField(writer, "spZ", SpawnPosition.z);
                KeyedSerializer.WriteField(writer, "tpX", TargetPosition.x);
                KeyedSerializer.WriteField(writer, "tpY", TargetPosition.y);
                KeyedSerializer.WriteField(writer, "tpZ", TargetPosition.z);
                KeyedSerializer.WriteEntityField(writer, "tgtB", TargetBuilding.ToEntity());
                KeyedSerializer.WriteField(writer, "spd", Speed);
                KeyedSerializer.WriteField(writer, "rad", ImpactRadius);
                KeyedSerializer.WriteField(writer, "sev", DamageSeverity);
                KeyedSerializer.WriteField(writer, "arr", IsArrived);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            SetDefaults();
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(Ballistic)))
            { return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "spX": SpawnPosition.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spX", 0f); break;
                            case "spY": SpawnPosition.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spY", 0f); break;
                            case "spZ": SpawnPosition.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "spZ", 0f); break;
                            case "tpX": TargetPosition.x = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpX", 0f); break;
                            case "tpY": TargetPosition.y = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpY", 0f); break;
                            case "tpZ": TargetPosition.z = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "tpZ", 0f); break;
                            case "tgtB": TargetBuilding = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "tgtB")); break;
                            case "spd": Speed = KeyedSerializer.ReadSafeFloat(reader, tag, "spd", 0f, 20000f, FALLBACK_SPEED); break;
                            case "rad": ImpactRadius = KeyedSerializer.ReadSafeFloat(reader, tag, "rad", 0f, 10000f, DEFAULT_IMPACT_RADIUS); break;
                            case "sev": DamageSeverity = KeyedSerializer.ReadSafeFloat(reader, tag, "sev", 0f, 10f, ThreatConstants.BALLISTIC_IMPACT_SEVERITY); break;
                            case "arr": IsArrived = KeyedSerializer.ReadBool(reader, tag, "arr"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                BallisticLog.Log.Error($"Deserialize {nameof(Ballistic)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Tag component for active threats (not yet hit or intercepted).
    /// Enables efficient querying.
    ///
    /// Added by: ThreatSpawnSystem
    /// Removed by: ThreatMovementSystem (on intercept/hit)
    /// Queried by: AirDefenseSystem, ThreatTargetSystem
    /// </summary>
    public struct ActiveThreat : IComponentData, IEnableableComponent { }

    /// <summary>
    /// Tag component for threats pending destruction (cleanup at end of frame).
    /// Prevents mid-frame race conditions between systems.
    ///
    /// Added by: threat lifecycle producers together with Deleted.
    /// Removed by: vanilla cleanup when Deleted is collected.
    /// </summary>
    public struct PendingDestruction : IComponentData, IEnableableComponent { }

    /// <summary>
    /// Renderless gameplay timer for delayed debris impact.
    /// Lives on a separate mod-only entity, never on Shahed/Ballistic render entities.
    /// </summary>
    public struct FallingDebris : IComponentData
    {
        /// <summary>Position where debris will land.</summary>
        public float3 FallPosition;
        /// <summary>Seconds until ground impact.</summary>
        public float TimeToImpact;
        /// <summary>
        /// Threat generation copied from the SOURCE threat at intercept time (NOT
        /// current-at-intercept). Debris keeps the source generation so post-load
        /// leftovers are dropped if they land in a later loaded-world generation.
        /// 0 = unstamped/invalid. Not serialized; no PrefabRef/serializer-root
        /// component is added to the renderless timer archetype.
        /// </summary>
        public int ThreatGeneration;

        /// <summary>Burst-safe factory — requires the source threat generation (no default arg ⇒ cannot re-introduce 0).</summary>
        public static FallingDebris FromThreat(float3 fallPosition, float timeToImpact, int threatGeneration) =>
            new() { FallPosition = fallPosition, TimeToImpact = timeToImpact, ThreatGeneration = threatGeneration };
    }

    /// <summary>
    /// Operational damage to power plants.
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid save corruption (P0-200).
    /// References building via BuildingIndex/BuildingVersion.
    ///
    /// Written by: OperationalDamageSystem (via ThreatDamageSystem calls)
    /// Read by: OperationalDamageSystem.SyncDamageToModifiers → PowerCapacityModifiers
    /// </summary>
    public struct PowerPlantDamage : IComponentData, ISerializable
    {
        /// <summary>Reference to vanilla building (typed Index+Version).</summary>
        public BuildingRef Building;
        /// <summary>Original capacity before any damage (kW).</summary>
        public int OriginalCapacity;
        /// <summary>Damage percentage (0.0 = undamaged, 1.0 = destroyed).</summary>
        public float DamagePercent;
        /// <summary>Total game hours when last hit (for repair cooldown).</summary>
        public double LastDamageGameHour;

        /// <summary>Reconstruct vanilla building Entity from typed ref.</summary>
        public readonly Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "cap", OriginalCapacity);
                KeyedSerializer.WriteField(writer, "dmg", DamagePercent);
                KeyedSerializer.WriteField(writer, "lastH", LastDamageGameHour);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PowerPlantDamage)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "cap": OriginalCapacity = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, SerializationGuard.MaxPlantCapacityKW, 0); break;
                            case "dmg": DamagePercent = KeyedSerializer.ReadSafeFloat(reader, tag, "dmg", 0f, 1f, 0f); break;
                            case "lastH": LastDamageGameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "lastH", 0.0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PowerPlantDamageLog.Log.Error($"Deserialize {nameof(PowerPlantDamage)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Air Defense Installation — AA state on OUR SEPARATE entity (not on the placed AA object).
    /// References the placed AA object via Index+Version to avoid Entity field issues.
    /// Serialized to save.
    ///
    /// The placed AA is a StaticObjectPrefab prop (AA_40mm_Bofors / MIM104_SAM), NOT a
    /// vanilla building — it has no Building/DestructibleObjectData component. Method/field
    /// names below say "building" for historical reasons only.
    ///
    /// FIX: Moved off the placed object onto a separate entity to prevent homeless spike.
    /// Writing components onto the placed object would trigger vanilla change detection.
    ///
    /// SSOT: No cached Position. Use GetBuildingEntity() + ComponentLookup&lt;Transform&gt;
    /// to resolve the AA object's position at runtime (always current after relocate).
    ///
    /// Written by: AAInstallationDetectorSystem (creates separate entity)
    /// Read by: AirDefenseOrchestrator (targeting, intercept), BallisticDefenseSystem
    /// </summary>
    public struct AirDefenseInstallation : IComponentData, ISerializable, IBuildingLinked
    {
        private const float DEFAULT_RANGE = 1000f;
        private const float MAX_RANGE = 10000f;
        private const float DEFAULT_COOLDOWN_DURATION = 1f;
        private const float MAX_COOLDOWN_DURATION = 60f;

        /// <summary>Reference to the placed AA object — a StaticObjectPrefab prop, not a
        /// vanilla building (for lifecycle tracking). Field name is historical.</summary>
        public BuildingRef Building;

        /// <summary>AA system type (Bofors40mm, PatriotSAM, etc.).</summary>
        public AAType Type;
        /// <summary>Detection/engagement range in meters.</summary>
        public float Range;
        /// <summary>Base intercept chance vs drones (0.0-1.0).</summary>
        public float InterceptChanceShahed;
        /// <summary>Base intercept chance vs missiles (0.0-1.0).</summary>
        public float InterceptChanceBallistic;
        /// <summary>Current ammunition count.</summary>
        public int CurrentAmmo;
        /// <summary>Maximum ammo capacity.</summary>
        public int MaxAmmo;
        /// <summary>Time between shots in seconds.</summary>
        public float CooldownDuration;
        /// <summary>Crew count (0 = unmanned, >0 = manned).</summary>
        public int CrewAssigned;
        /// <summary>Crew count this installation requires to be manned. Persisted so the
        /// authored/placement value survives load — post-load retag reads this instead of
        /// reconstructing it from an AAType→config heuristic that can diverge from the
        /// value actually requested at placement. 0 = legacy save without the field.</summary>
        public int CrewRequired;

        /// <summary>Budget actually charged at placement (AAPlacementIntent.Cost when
        /// RequiresBudget). 0 for credit/Heritage/free placements — those refund no cash on
        /// demolition, which closes the credit→cash money-printing hole. Refund base for the
        /// player-demolition path (AAPlayerDemolitionSystem). 0 = legacy save without the
        /// field → no refund (safe).</summary>
        public int PaidBudget;

        /// <summary>TotalGameHours at placement — refund decay base (vanilla "Recent"
        /// equivalent). Persisted as TotalGameHours, NOT SystemAPI.Time.ElapsedTime (which
        /// resets on load). 0 on legacy saves → elapsed is huge → past the last refund window
        /// → 0 refund (safe).</summary>
        public float PlacedGameHours;

        public Entity GetBuildingEntity() => Building.ToEntity();

        // Keyed format: new fields (PaidBudget/PlacedGameHours) are added by field key, not a
        // version bump — old saves simply lack the keys and read defaults. SAVE_VERSION stays 1
        // (CIVIC521: the keyed format evolves via keys, not the version gate).
        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
            Range = DEFAULT_RANGE;
            CooldownDuration = DEFAULT_COOLDOWN_DURATION;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            if (AirDefenseInstallationLog.Log.IsDebugEnabled)
                AirDefenseInstallationLog.Log.Debug($"Serialize: building={Building.Index}:{Building.Version} type={Type} crew={CrewAssigned} ammo={CurrentAmmo}/{MaxAmmo}");
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 12);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "type", (byte)Type);
                KeyedSerializer.WriteField(writer, "rng", Range);
                KeyedSerializer.WriteField(writer, "icS", InterceptChanceShahed);
                KeyedSerializer.WriteField(writer, "icB", InterceptChanceBallistic);
                KeyedSerializer.WriteField(writer, "ammo", CurrentAmmo);
                KeyedSerializer.WriteField(writer, "mxAm", MaxAmmo);
                KeyedSerializer.WriteField(writer, "cdDur", CooldownDuration);
                KeyedSerializer.WriteField(writer, "crew", CrewAssigned);
                KeyedSerializer.WriteField(writer, "creq", CrewRequired);
                KeyedSerializer.WriteField(writer, "paid", PaidBudget);
                KeyedSerializer.WriteField(writer, "plcH", PlacedGameHours);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AirDefenseInstallation)))
            {
                AirDefenseInstallationLog.Log.Warn($"Deserialize: TryBeginBlock FAILED (expected v{SAVE_VERSION}) -> SetDefaults, building=0:0");
                SetDefaults();
                return;
            }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    if (AirDefenseInstallationLog.Log.IsDebugEnabled)
                        AirDefenseInstallationLog.Log.Debug($"Deserialize START: v{version} fc={fc}");
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "type": Type = KeyedSerializer.ReadEnumByte<TReader, AAType>(reader, tag, "type", AAType.HeritageBofors); break;
                            case "rng": Range = KeyedSerializer.ReadSafeFloat(reader, tag, "rng", 1f, MAX_RANGE, DEFAULT_RANGE); break;
                            case "icS": InterceptChanceShahed = KeyedSerializer.ReadSafeFloat(reader, tag, "icS", 0f, 1f, 0f); break;
                            case "icB": InterceptChanceBallistic = KeyedSerializer.ReadSafeFloat(reader, tag, "icB", 0f, 1f, 0f); break;
                            case "ammo": CurrentAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "ammo", 0, 10000, 0); break;
                            case "mxAm": MaxAmmo = KeyedSerializer.ReadBoundedInt(reader, tag, "mxAm", 0, 10000, 0); break;
                            case "cdDur": CooldownDuration = KeyedSerializer.ReadSafeFloat(reader, tag, "cdDur", 0.1f, MAX_COOLDOWN_DURATION, DEFAULT_COOLDOWN_DURATION); break;
                            case "crew": CrewAssigned = KeyedSerializer.ReadBoundedInt(reader, tag, "crew", 0, 100, 0); break;
                            case "creq": CrewRequired = KeyedSerializer.ReadBoundedInt(reader, tag, "creq", 0, 100, 0); break;
                            case "paid": PaidBudget = KeyedSerializer.ReadBoundedInt(reader, tag, "paid", 0, int.MaxValue, 0); break;
                            case "plcH": PlacedGameHours = KeyedSerializer.ReadSafeFloat(reader, tag, "plcH", 0f, float.MaxValue, 0f); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                    if (AirDefenseInstallationLog.Log.IsDebugEnabled)
                        AirDefenseInstallationLog.Log.Debug($"Deserialize DONE: building={Building.Index}:{Building.Version} type={Type} crew={CrewAssigned} ammo={CurrentAmmo}/{MaxAmmo} (v{version} fc={fc})");
                }
                else
                {
                    AirDefenseInstallationLog.Log.Warn($"Deserialize: unexpected version {version} (expected >=1) -> fields skipped, building={Building.Index}:{Building.Version}");
                }
            }
            catch (System.Exception ex)
            {
                AirDefenseInstallationLog.Log.Error($"Deserialize {nameof(AirDefenseInstallation)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Passive ready timestamp for an AA installation, split out of AirDefenseInstallation.
    /// Stores absolute persisted game seconds so cooldown survives save/load and freezes
    /// naturally while game time is paused.
    ///
    /// Written by: FireControlExecutor, BallisticDefenseSystem (set on fire)
    /// Read by: AirDefenseOrchestrator, FireControlExecutor, BallisticDefenseSystem
    /// </summary>
    public struct AirDefenseCooldown : IComponentData, ISerializable
    {
        private const double MAX_READY_AT_GAME_SECONDS = 100.0 * 365.0 * GameRate.SECONDS_PER_DAY;
        /// <summary>Absolute game seconds when next shot is available. 0 = ready immediately.</summary>
        public double ReadyAtGameSeconds;
        private const byte SAVE_VERSION = 1;

        public void SetDefaults() { this = default; }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "rdy", ReadyAtGameSeconds);
            }
            finally { SerializationGuard.EndBlock(writer, block); }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AirDefenseCooldown)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "rdy": ReadyAtGameSeconds = KeyedSerializer.ReadSafeDouble(reader, tag, "rdy", 0.0, MAX_READY_AT_GAME_SECONDS, 0.0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                AirDefenseCooldownLog.Log.Error($"Deserialize {nameof(AirDefenseCooldown)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Threat type for radar visualization.
    /// </summary>
    public enum RadarThreatType : byte
    {
        Shahed = 0,
        Ballistic = 1
    }

    /// <summary>
    /// Buffer element for radar visualization data.
    /// Written by ThreatMovementSystem during movement processing.
    /// Read by ThreatRadarSystem for UI (zero queries needed).
    ///
    /// Stored on RadarDataSingleton entity.
    /// </summary>
    [InternalBufferCapacity(64)]
    public struct RadarThreatBuffer : IBufferElementData
    {
        public int EntityIndex;
        public int EntityVersion;
        public float3 Position;
        public float3 TargetPosition;
        public float Speed;
        public RadarThreatType Type;
        public int MissedShotsCount;
        public bool IsIdentified;
    }

    /// <summary>
    /// Singleton tag for radar data entity.
    /// Entity holds DynamicBuffer of RadarThreatBuffer.
    /// </summary>
    public struct RadarDataSingleton : IComponentData { }

    /// <summary>
    /// Identification state for player engagement pipeline.
    /// Click drone → follow cam → progress bar → "Unknown" → "Confirmed" → +20% AA accuracy.
    ///
    /// EPHEMERAL: Destroyed with drone entity, no serialization needed.
    ///
    /// Written by: ThreatIdentifySystem (progress tick)
    /// Read by: AirDefenseOrchestrator (+20% bonus), ThreatUISystem (UI overlay)
    /// </summary>
    public struct IdentifiedTarget : IComponentData
    {
        /// <summary>Progress 0→1 over ~2.5 seconds while camera tracks.</summary>
        public float IdentifyProgress;
        /// <summary>True once identification complete.</summary>
        public bool Identified;
    }

    /// <summary>
    /// Tag: player is Focus-holding this target. AA systems redirect (score +500).
    /// Only one entity has PriorityTarget at a time — managed by ThreatIdentifySystem.
    ///
    /// EPHEMERAL: Removed when camera detaches or drone destroyed.
    ///
    /// Added by: ThreatIdentifySystem (via ECB)
    /// Read by: EngagementScoringJob (+500 score boost)
    /// Removed by: ThreatIdentifySystem (via ECB, when camera detaches)
    /// </summary>
    public struct PriorityTarget : IComponentData { }

    /// <summary>
    /// Tag component signaling that AA installation needs crew assignment.
    /// Fire-and-forget pattern for cross-domain communication.
    ///
    /// Written by: AAInstallationDetectorSystem, HeritageGrantSystem (AirDefense domain)
    /// Read and removed by: AACrewAssignmentSystem (Mobilization domain)
    ///
    /// Decouples AirDefense from Mobilization - no direct service calls.
    /// AACrewAssignmentSystem uses RequireForUpdate for zero overhead when no requests.
    /// </summary>
    public struct RequestCrewTag : IComponentData
    {
        /// <summary>Number of crew members required for this AA installation.</summary>
        public int CrewRequired;
    }
}


