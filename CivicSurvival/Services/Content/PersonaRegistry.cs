using System.Collections.Generic;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Central registry for satirical character personas.
    /// Lives in Core - no domain dependencies.
    ///
    /// Usage:
    ///   if (PersonaRegistry.TryResolve("TECH_WORKER", out var persona)) { ... }
    /// </summary>
    public static class PersonaRegistry
    {
        private static readonly LogContext Log = new("PersonaRegistry");

        private static readonly object s_Lock = new();
#pragma warning disable CIVIC148 // Immutable hardcoded data populated once in static ctor — no runtime accumulation
        private static readonly Dictionary<string, PersonaProfile[]> s_Personas = new();
#pragma warning restore CIVIC148
        private static readonly Dictionary<string, int> s_LastVariantIndex = new();

        static PersonaRegistry()
        {
            RegisterBuiltInPersonas();
        }

        private static void RegisterBuiltInPersonas()
        {
            // Tech Workers - rotate between variants
            s_Personas["TECH_WORKER"] = new[]
            {
                new PersonaProfile("TECH_WORKER_PETRENKO", "@InzhenerPetrenko", "SATIRE_PETRENKO"),
                new PersonaProfile("TECH_WORKER_IT", "@ITSysAdmin", "SATIRE_IT")
            };

            // Citizens - rotate between variants
            s_Personas["CITIZEN"] = new[]
            {
                new PersonaProfile("CITIZEN_BABCYA", "@BabcyaZina", "SATIRE_BABCYA"),
                new PersonaProfile("CITIZEN_TAXI", "@TaxiDriver_Mykola", "SATIRE_TAXI")
            };

            // Single personas (no rotation)
            s_Personas["BABCYA"] = new[]
            {
                new PersonaProfile("BABCYA", "@BabcyaZina", "SATIRE_BABCYA")
            };

            s_Personas["KOTLETA"] = new[]
            {
                new PersonaProfile("KOTLETA", "@DeputatKotleta", "SATIRE_KOTLETA")
            };

            s_Personas["MARIANA"] = new[]
            {
                new PersonaProfile("MARIANA", "@MarianaPravda", "SATIRE_MARIANA")
            };

            s_Personas["VALERA"] = new[]
            {
                new PersonaProfile("VALERA", "@Valera_Spotter", "SATIRE_VALERA")
            };

            s_Personas["VOLUNTEER"] = new[]
            {
                new PersonaProfile("VOLUNTEER", "@Volunteer_Oksana", "SATIRE_VOLUNTEER")
            };

            s_Personas["VOLUNTEER_BUS"] = new[]
            {
                new PersonaProfile("VOLUNTEER_BUS", "@VolunteerBus", "SATIRE_VOLUNTEER")
            };

            s_Personas["CITY_ALERT"] = new[]
            {
                new PersonaProfile("CITY_ALERT", "@CityAlert", "SATIRE_ALERT")
            };

            s_Personas["CITY_EMERGENCY"] = new[]
            {
                new PersonaProfile("CITY_EMERGENCY", "@CityEmergency", "SATIRE_EMERGENCY")
            };

            s_Personas["ENERGY_MINISTRY"] = new[]
            {
                new PersonaProfile("ENERGY_MINISTRY", "@EnergyMinistry", "SATIRE_MINISTRY")
            };

            s_Personas["LOCAL_NEWS"] = new[]
            {
                new PersonaProfile("LOCAL_NEWS", "@LocalNewsDaily", "NEWS_LOCAL")
            };

            s_Personas["BBC_WORLD"] = new[]
            {
                new PersonaProfile("BBC_WORLD", "@BBCWorld", "NEWS_LOCAL")
            };

            s_Personas["CITY_MAYOR"] = new[]
            {
                new PersonaProfile("CITY_MAYOR", "@MayorOfficial", "NEWS_MAYOR")
            };

            s_Personas["LOCAL_MAYOR"] = new[]
            {
                new PersonaProfile("LOCAL_MAYOR", "@LocalMayor", "NEWS_MAYOR")
            };

            s_Personas["LOCAL_ADMIN"] = new[]
            {
                new PersonaProfile("LOCAL_ADMIN", "@LocalAdministration", "NEWS_MAYOR")
            };

            s_Personas["POWER_ENGINEER"] = new[]
            {
                new PersonaProfile("POWER_ENGINEER", "@GridEngineer", "SATIRE_ENGINEER")
            };

            s_Personas["HOSPITAL_WORKER"] = new[]
            {
                new PersonaProfile("HOSPITAL_WORKER", "@HospitalStaff", "CHIRP_HOSPITAL")
            };

            s_Personas["IT_WORKER"] = new[]
            {
                new PersonaProfile("IT_WORKER", "@ITSupport", "CHIRP_IT")
            };

            s_Personas["PETRENKO"] = new[]
            {
                new PersonaProfile("PETRENKO", "@InzhenerPetrenko", "SATIRE_PETRENKO")
            };

            s_Personas["TAXI"] = new[]
            {
                new PersonaProfile("TAXI", "@TaxiDriver_Mykola", "SATIRE_TAXI")
            };

            // ============================================
            // Official personas (NEWS feed)
            // ============================================

            s_Personas["DSNS"] = new[]
            {
                new PersonaProfile("DSNS", "@DSNS_Official", "NEWS_DSNS")
            };

            s_Personas["WATER_COMPANY"] = new[]
            {
                new PersonaProfile("WATER_COMPANY", "@WaterCompany", "NEWS_WATER")
            };

            s_Personas["UN_REFUGEE"] = new[]
            {
                new PersonaProfile("UN_REFUGEE", "@UN_Refugee", "NEWS_UN_REFUGEE")
            };

            s_Personas["UN_AID"] = new[]
            {
                new PersonaProfile("UN_AID", "@UN_Aid", "NEWS_UN_AID")
            };

            s_Personas["UN_COUNCIL"] = new[]
            {
                new PersonaProfile("UN_COUNCIL", "@UN_Council", "NEWS_UN_COUNCIL")
            };

            s_Personas["UN_OFFICIAL"] = new[]
            {
                new PersonaProfile("UN_OFFICIAL", "@UN_Official", "NEWS_UN_COUNCIL")
            };

            s_Personas["UN_OBSERVER"] = new[]
            {
                new PersonaProfile("UN_OBSERVER", "@UN_Observer", "NEWS_UN_COUNCIL")
            };

            s_Personas["NATO_SUPPORT"] = new[]
            {
                new PersonaProfile("NATO_SUPPORT", "@NATO_Support", "NEWS_NATO_SUPPORT")
            };

            s_Personas["NATO_DEFENSE"] = new[]
            {
                new PersonaProfile("NATO_DEFENSE", "@NATO_Defense", "NEWS_NATO_DEFENSE")
            };

            s_Personas["MILITARY_COMMAND"] = new[]
            {
                new PersonaProfile("MILITARY_COMMAND", "@MilitaryCommand", "NEWS_MILITARY")
            };

            s_Personas["TRO_COMMANDER"] = new[]
            {
                new PersonaProfile("TRO_COMMANDER", "@TRO_Commander", "NEWS_TRO")
            };

            s_Personas["MILITARY_ADVISOR"] = new[]
            {
                new PersonaProfile("MILITARY_ADVISOR", "@MilitaryAdvisor", "NEWS_MILITARY")
            };

            s_Personas["NEXTA"] = new[]
            {
                new PersonaProfile("NEXTA", "@NEXTA_Live", "NEWS_NEXTA")
            };

            s_Personas["PRIVATBANK"] = new[]
            {
                new PersonaProfile("PRIVATBANK", "@PrivatBank_UA", "NEWS_LOCAL")
            };

            s_Personas["OPPOSITION"] = new[]
            {
                new PersonaProfile("OPPOSITION", "@OppositionWatchdog", "SATIRE_MARIANA")
            };

            s_Personas["HISTORIAN"] = new[]
            {
                new PersonaProfile("HISTORIAN", "@WarHistorian", "NEWS_LOCAL")
            };

            s_Personas["ZELENSKYY"] = new[]
            {
                new PersonaProfile("ZELENSKYY", "@ZelenskyyUa", "NEWS_MILITARY")
            };

            s_Personas["EMERGENCY_SERVICES"] = new[]
            {
                new PersonaProfile("EMERGENCY_SERVICES", "@EmergencyServices", "SATIRE_EMERGENCY")
            };

            // ============================================
            // Citizen personas (CHIRPER feed)
            // ============================================

            s_Personas["REFUGEE"] = new[]
            {
                new PersonaProfile("REFUGEE_1", "@refugee_family", "CHIRP_REFUGEE"),
                new PersonaProfile("REFUGEE_2", "@displaced_person", "CHIRP_REFUGEE"),
                new PersonaProfile("REFUGEE_3", "@evacuee_kyiv", "CHIRP_REFUGEE")
            };

            s_Personas["LOCAL_MOTHER"] = new[]
            {
                new PersonaProfile("LOCAL_MOTHER", "@local_mother", "CHIRP_MOTHER")
            };

            s_Personas["ANGRY_DOCTOR"] = new[]
            {
                new PersonaProfile("ANGRY_DOCTOR", "@AngryDoctor", "CHIRP_DOCTOR")
            };

            s_Personas["LOCAL_RESIDENT"] = new[]
            {
                new PersonaProfile("LOCAL_RESIDENT_1", "@local_resident", "CHIRP_RESIDENT"),
                new PersonaProfile("LOCAL_RESIDENT_2", "@kyiv_citizen", "CHIRP_RESIDENT"),
                new PersonaProfile("LOCAL_RESIDENT_3", "@ordinary_person", "CHIRP_RESIDENT")
            };

            if (Log.IsDebugEnabled) Log.Debug($"Registered {s_Personas.Count} persona groups");
        }

        /// <summary>
        /// Try to resolve a persona by group ID. Unknown groups have no postable persona.
        /// </summary>
        public static bool TryResolve(string? groupId, out PersonaProfile persona)
        {
            persona = default;
            if (string.IsNullOrWhiteSpace(groupId))
                return false;

            string key = groupId!;
            lock (s_Lock)
            {
                if (s_Personas.TryGetValue(key, out var personas) && personas.Length > 0)
                {
                    persona = personas.Length == 1 ? personas[0] : PickRotating(key, personas);
                    return !string.IsNullOrEmpty(persona.Handle);
                }
            }

            Log.Warn($"Unknown persona group: {groupId}");
            return false;
        }

        /// <summary>
        /// Check if persona group exists.
        /// Thread-safe via lock.
        /// </summary>
        public static bool HasPersona(string groupId)
        {
            lock (s_Lock)
            {
                return s_Personas.ContainsKey(groupId);
            }
        }

        internal static void ResetRotation()
        {
            lock (s_Lock)
            {
                s_LastVariantIndex.Clear();
            }
        }

        private static PersonaProfile PickRotating(string groupId, PersonaProfile[] personas)
        {
            lock (s_Lock)
            {
                int length = personas.Length;
                int index;
                if (length <= 0)
                {
                    return default;
                }
                if (length == 1)
                {
                    index = 0;
                }
                else if (s_LastVariantIndex.TryGetValue(groupId, out int last))
                {
                    index = (last + 1 + ThreadSafeRandom.Next(length - 1)) % length;
                }
                else
                {
                    index = ThreadSafeRandom.Next(1, length + 1) - 1;
                }

                s_LastVariantIndex[groupId] = index;
                return personas[index];
            }
        }
    }
}
