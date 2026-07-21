using System;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Types
{
    /// <summary>Absolute simulation-hour stamp.</summary>
    public readonly struct GameHourStamp
    {
        private GameHourStamp(double value) => Value = value;
        public double Value { get; }

        public static bool TryCreate(double value, out GameHourStamp stamp, bool allowPreWar = false)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (!allowPreWar && value < 0.0))
            {
                stamp = default;
                return false;
            }

            stamp = new GameHourStamp(value);
            return true;
        }

        public static GameHourStamp FromSnapshot(in GameTimeSnapshot snapshot)
            => TryCreate(snapshot.TotalGameHours, out var stamp, allowPreWar: true) ? stamp : default;

        public double ToSeconds() => GameRate.HoursToSeconds(Value);
        public GameHourDeadline Add(GameDurationHours duration) => new(Value + duration.Value);
    }

    /// <summary>Absolute simulation-hour deadline.</summary>
    public readonly struct GameHourDeadline
    {
        public GameHourDeadline(double value) => Value = value;
        public double Value { get; }
    }

    /// <summary>Normalized hour within a game day, [0, 24).</summary>
    public readonly struct GameHourOfDay
    {
        private GameHourOfDay(float value) => Value = value;
        public float Value { get; }

        public static bool TryNormalize(float value, out GameHourOfDay hour)
        {
            if (!math.isfinite(value))
            {
                hour = default;
                return false;
            }

            float normalized = value % GameRate.HOURS_PER_DAY;
            if (normalized < 0f)
                normalized += GameRate.HOURS_PER_DAY;

            hour = new GameHourOfDay(normalized);
            return true;
        }
    }

    /// <summary>Positive simulation duration in game hours.</summary>
    public readonly struct GameDurationHours
    {
        private GameDurationHours(float value) => Value = value;
        public float Value { get; }

        public static bool TryCreate(float value, out GameDurationHours duration, float maxHours = float.MaxValue)
        {
            if (!math.isfinite(value) || value <= 0f || value > maxHours)
            {
                duration = default;
                return false;
            }

            duration = new GameDurationHours(value);
            return true;
        }
    }

    /// <summary>Absolute non-negative game-day stamp.</summary>
    public readonly struct GameDayStamp
    {
        private GameDayStamp(int value) => Value = value;
        public int Value { get; }

        public static bool TryCreate(int value, out GameDayStamp stamp)
        {
            if (value < 0)
            {
                stamp = default;
                return false;
            }

            stamp = new GameDayStamp(value);
            return true;
        }
    }

    /// <summary>War-relative day. HasValue=false represents pre-war/unset.</summary>
    public readonly struct WarDayStamp
    {
        private WarDayStamp(int value)
        {
            HasValue = true;
            Value = value;
        }

        public bool HasValue { get; }
        public int Value { get; }
        public static WarDayStamp None => default;

        public static bool TryCreate(int value, out WarDayStamp stamp)
        {
            if (value < 0)
            {
                stamp = default;
                return false;
            }

            stamp = new WarDayStamp(value);
            return true;
        }
    }

    /// <summary>Unity/wall-clock seconds.</summary>
    public readonly struct RealtimeSeconds
    {
        private RealtimeSeconds(float value) => Value = value;
        public float Value { get; }
        public static RealtimeSeconds Zero => new(0f);

        public static bool TryCreate(float value, out RealtimeSeconds seconds, bool clampNegative = false)
        {
            if (!math.isfinite(value))
            {
                seconds = default;
                return false;
            }

            if (value < 0f)
            {
                if (!clampNegative)
                {
                    seconds = default;
                    return false;
                }
                value = 0f;
            }

            seconds = new RealtimeSeconds(value);
            return true;
        }

        public RealtimeSeconds Add(RealtimeSeconds other) => new(Value + other.Value);
    }

    /// <summary>Frame pacing only; must not be persisted as a domain deadline.</summary>
    public readonly struct FrameThrottle
    {
        public FrameThrottle(int interval, int phase)
        {
            Interval = math.max(1, interval);
            Phase = phase;
        }

        public int Interval { get; }
        public int Phase { get; }
    }

    /// <summary>Durable identity for a scheduled intent.</summary>
    public readonly struct SchedulerToken : IEquatable<SchedulerToken>
    {
        public SchedulerToken(int value) => Value = value;
        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(SchedulerToken other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is SchedulerToken other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(SchedulerToken left, SchedulerToken right) => left.Equals(right);
        public static bool operator !=(SchedulerToken left, SchedulerToken right) => !left.Equals(right);
    }

    public enum CatchUpPolicyKind : byte
    {
        None = 0,
        Once = 1,
        Bounded = 2,
        Full = 3,
        OwnerGate = 4
    }

    public readonly struct CatchUpPolicy
    {
        private CatchUpPolicy(CatchUpPolicyKind kind, float boundHours)
        {
            Kind = kind;
            BoundHours = boundHours;
        }

        public CatchUpPolicyKind Kind { get; }
        public float BoundHours { get; }

        public static CatchUpPolicy None => new(CatchUpPolicyKind.None, 0f);
        public static CatchUpPolicy Once => new(CatchUpPolicyKind.Once, 0f);
        public static CatchUpPolicy Full => new(CatchUpPolicyKind.Full, 0f);
        public static CatchUpPolicy OwnerGate => new(CatchUpPolicyKind.OwnerGate, 0f);

        public static CatchUpPolicy Bounded(GameDurationHours duration)
            => new(CatchUpPolicyKind.Bounded, duration.Value);
    }
}
