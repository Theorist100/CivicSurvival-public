using System.Collections.Generic;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Cognitive.Ops.Countermeasures
{
    /// <summary>
    /// The hero's voice in the Chipper feed.
    ///
    /// The enemy already speaks there — every landed raid drops themed bot chatter
    /// (PsyOpsLaunchSystem). The speaker the player fields did not, so his whole job — blunting a
    /// contact by type — happened silently behind the numbers. He now posts on the moments that
    /// carry the mechanic:
    ///
    ///   ON AIR      — deployed: the city hears who is speaking for it.
    ///   BLUNTED     — his counter-type took the hit: the ONE post that makes his work visible.
    ///   WRONG VOICE — a contact landed clean while he was on air: he says plainly that this
    ///                 carrier is not his to answer. That is the rock-paper-scissors lesson,
    ///                 taught in the feed instead of a tooltip.
    ///   EXPOSED     — Arestovych's trust debt collapses when a deepfake catches him out.
    ///   OFF AIR     — recalled: the channel goes quiet again.
    ///
    /// Text lives in the localization files (EN + UA) behind the satire registry, like every
    /// other in-world voice; this class only picks the key and hands the post to the feed.
    /// </summary>
    public static class HeroVoice
    {
        private static readonly LogContext Log = new("HeroVoice");

        // One handle per archetype: the feed shows WHO is on air, and NewsAuthorRegistry maps
        // each handle to its own face (mic / brain / shield).
        private const string HandleVoice = "@holos_live";
        private const string HandleArestovych = "@calm_analysis";
        private const string HandlePatriot = "@patriot_stands";

        // Every archetype is named: an unknown value would post as the wrong speaker, so it is
        // logged and dropped rather than silently voiced by someone else.
        private static bool TryIdentify(HeroArchetype archetype, out string handle, out string tag)
        {
            switch (archetype)
            {
                case HeroArchetype.Voice:
                    handle = HandleVoice;
                    tag = "VOICE";
                    return true;
                case HeroArchetype.Arestovych:
                    handle = HandleArestovych;
                    tag = "ARESTOVYCH";
                    return true;
                case HeroArchetype.Patriot:
                    handle = HandlePatriot;
                    tag = "PATRIOT";
                    return true;
                default:
                    Log.Warn($"No feed voice for archetype {archetype} — staying silent");
                    handle = "";
                    tag = "";
                    return false;
            }
        }

        /// <summary>Deployed — the speaker opens his channel.</summary>
        public static void PostDeployed(IEventBus? bus, HeroArchetype archetype)
            => Post(bus, archetype, "ONAIR", SocialMood.Neutral);

        /// <summary>Recalled — the channel goes quiet.</summary>
        public static void PostRecalled(IEventBus? bus, HeroArchetype archetype)
            => Post(bus, archetype, "OFFAIR", SocialMood.Neutral);

        /// <summary>A landed contact was blunted by this archetype's counter — his work, made visible.</summary>
        public static void PostBlunted(IEventBus? bus, HeroArchetype archetype)
            => Post(bus, archetype, "BLUNTED", SocialMood.Neutral);

        /// <summary>A contact landed clean while he was on air — the wrong voice for this carrier.</summary>
        public static void PostWrongVoice(IEventBus? bus, HeroArchetype archetype)
            => Post(bus, archetype, "WRONGVOICE", SocialMood.Suffering);

        /// <summary>Arestovych caught out by a deepfake — his accrued trust collapses in public.</summary>
        public static void PostExposed(IEventBus? bus)
            => Post(bus, HeroArchetype.Arestovych, "EXPOSED", SocialMood.Suspicious);

        private static void Post(IEventBus? bus, HeroArchetype archetype, string moment, SocialMood mood)
        {
            if (bus == null)
                return;
            if (!TryIdentify(archetype, out var handle, out var tag))
                return;

            string satireKey = $"SATIRE_HERO_{tag}_{moment}";
            // Say nothing rather than shout a key: TryGetMessage fails both when the key is
            // unregistered and when its localization is missing, so no raw placeholder reaches the feed.
            if (!SatireRegistry.TryGetMessage(satireKey, out string message))
                return;

            bus.SafePublish(new SocialPostEvent(handle, message, mood), nameof(HeroVoice));
        }
    }

    /// <summary>
    /// Registers the hero's lines with the satire registry (one entry per archetype × moment).
    /// </summary>
    public class HeroSatireProvider : ISatireProvider
    {
        public string Domain => "Cognitive.Hero";

#pragma warning disable CIVIC148 // Immutable readonly dict — no stale data risk
        private static readonly Dictionary<string, SatireConfig> s_Configs = new()
        {
            // The Voice — counters Rumor.
            ["SATIRE_HERO_VOICE_ONAIR"] = new("SATIRE_HERO_VOICE_ONAIR", 2, "HERO_VOICE", SocialMood.Neutral),
            ["SATIRE_HERO_VOICE_BLUNTED"] = new("SATIRE_HERO_VOICE_BLUNTED", 2, "HERO_VOICE", SocialMood.Neutral),
            ["SATIRE_HERO_VOICE_WRONGVOICE"] = new("SATIRE_HERO_VOICE_WRONGVOICE", 2, "HERO_VOICE", SocialMood.Suffering),
            ["SATIRE_HERO_VOICE_OFFAIR"] = new("SATIRE_HERO_VOICE_OFFAIR", 2, "HERO_VOICE", SocialMood.Neutral),

            // Arestovych — counters FakeVideo, carries a trust debt.
            ["SATIRE_HERO_ARESTOVYCH_ONAIR"] = new("SATIRE_HERO_ARESTOVYCH_ONAIR", 2, "HERO_ARESTOVYCH", SocialMood.Neutral),
            ["SATIRE_HERO_ARESTOVYCH_BLUNTED"] = new("SATIRE_HERO_ARESTOVYCH_BLUNTED", 2, "HERO_ARESTOVYCH", SocialMood.Neutral),
            ["SATIRE_HERO_ARESTOVYCH_WRONGVOICE"] = new("SATIRE_HERO_ARESTOVYCH_WRONGVOICE", 2, "HERO_ARESTOVYCH", SocialMood.Suffering),
            ["SATIRE_HERO_ARESTOVYCH_OFFAIR"] = new("SATIRE_HERO_ARESTOVYCH_OFFAIR", 2, "HERO_ARESTOVYCH", SocialMood.Neutral),
            ["SATIRE_HERO_ARESTOVYCH_EXPOSED"] = new("SATIRE_HERO_ARESTOVYCH_EXPOSED", 2, "HERO_ARESTOVYCH", SocialMood.Suspicious),

            // The Patriot — counters Propaganda.
            ["SATIRE_HERO_PATRIOT_ONAIR"] = new("SATIRE_HERO_PATRIOT_ONAIR", 2, "HERO_PATRIOT", SocialMood.Neutral),
            ["SATIRE_HERO_PATRIOT_BLUNTED"] = new("SATIRE_HERO_PATRIOT_BLUNTED", 2, "HERO_PATRIOT", SocialMood.Neutral),
            ["SATIRE_HERO_PATRIOT_WRONGVOICE"] = new("SATIRE_HERO_PATRIOT_WRONGVOICE", 2, "HERO_PATRIOT", SocialMood.Suffering),
            ["SATIRE_HERO_PATRIOT_OFFAIR"] = new("SATIRE_HERO_PATRIOT_OFFAIR", 2, "HERO_PATRIOT", SocialMood.Neutral),
        };
#pragma warning restore CIVIC148

        public IReadOnlyDictionary<string, SatireConfig> GetConfigs() => s_Configs;
    }
}
