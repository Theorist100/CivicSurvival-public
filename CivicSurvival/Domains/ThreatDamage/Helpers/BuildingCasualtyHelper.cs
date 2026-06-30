using Game.Buildings;
using Game.Citizens;
using Game.Companies;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Utils;

// Alias to resolve ambiguity between Game.Buildings.Student and Game.Citizens.Student
using Student = Game.Buildings.Student;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Extracts citizen entities from buildings for casualty processing.
    /// Supports all building types: residential, commercial, industrial,
    /// schools, hospitals, and service buildings.
    ///
    /// Data flow by building type:
    /// - Residential: Building в†’ Renter в†’ Household в†’ HouseholdCitizen
    /// - Commercial/Industrial: Building в†’ Renter в†’ Company в†’ Employee
    /// - School: Building в†’ Student buffer + Employee buffer
    /// - Hospital: Building в†’ Patient buffer + Employee buffer
    /// - Service (Power/Water): Building в†’ Employee buffer (direct)
    /// </summary>
    public static class BuildingCasualtyHelper
    {
        /// <summary>
        /// Get citizen entities that should be killed from a building hit.
        /// Returns up to maxVictims citizens based on building type.
        /// </summary>
        /// <param name="building">The hit building entity</param>
        /// <param name="type">Building type classification</param>
        /// <param name="maxVictims">Maximum casualties to extract</param>
        /// <param name="renterLookup">Lookup for building renters (households/companies)</param>
        /// <param name="householdLookup">Lookup for household citizens</param>
        /// <param name="employeeLookup">Lookup for company/service employees</param>
        /// <param name="studentLookup">Lookup for school students</param>
        /// <param name="patientLookup">Lookup for hospital patients</param>
        /// <param name="companyLookup">Lookup to identify company entities</param>
        /// <param name="victims">Output list of citizen entities to delete</param>
        public static void GetVictims(
            Entity building,
            BuildingType type,
            int maxVictims,
            ref BufferLookup<Renter> renterLookup,
            ref BufferLookup<HouseholdCitizen> householdLookup,
            ref BufferLookup<Employee> employeeLookup,
            ref BufferLookup<Student> studentLookup,
            ref BufferLookup<Patient> patientLookup,
            ref ComponentLookup<CompanyData> companyLookup,
            ref ComponentLookup<PropertyRenter> propertyRenterLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeList<Entity> victims)
        {
            victims.Clear();

            if (maxVictims <= 0)
                return;

            var selectedVictims = new NativeHashSet<int>(maxVictims, Allocator.Temp);
            try
            {
                switch (type)
                {
                    case BuildingType.Residential:
                        AddHouseholdCitizens(building, maxVictims,
                            ref renterLookup, ref householdLookup, ref propertyRenterLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        break;

                    case BuildingType.School:
                        // Students + Teachers
                        AddStudents(building, maxVictims, ref studentLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        int remaining = maxVictims - victims.Length;
                        if (remaining > 0)
                            AddEmployees(building, remaining, ref employeeLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        break;

                    case BuildingType.Hospital:
                        // Patients + Staff
                        AddPatients(building, maxVictims, ref patientLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        remaining = maxVictims - victims.Length;
                        if (remaining > 0)
                            AddEmployees(building, remaining, ref employeeLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        break;

                    case BuildingType.Commercial:
                    case BuildingType.Industrial:
                        // Building в†’ Renter в†’ Company в†’ Employee
                        AddCompanyEmployees(building, maxVictims,
                            ref renterLookup, ref employeeLookup, ref companyLookup, ref propertyRenterLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        break;

                    default:
                        // Service buildings (power, water) в†’ Employee directly
                        AddEmployees(building, maxVictims, ref employeeLookup, ref deletedLookup, in processedVictims, selectedVictims, victims);
                        break;
                }
            }
            finally
            {
                if (selectedVictims.IsCreated)
                    selectedVictims.Dispose();
            }
        }

        private static bool TryAddFreshVictim(
            Entity victim,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (victim != Entity.Null
                && !deletedLookup.HasComponent(victim)
                && !processedVictims.Contains(victim.Index)
                && selectedVictims.Add(victim.Index))
            {
                victims.Add(victim);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract citizens from residential building households.
        /// Path: Building в†’ Renter buffer в†’ Household в†’ HouseholdCitizen buffer
        /// </summary>
        private static void AddHouseholdCitizens(
            Entity building,
            int maxVictims,
            ref BufferLookup<Renter> renterLookup,
            ref BufferLookup<HouseholdCitizen> householdLookup,
            ref ComponentLookup<PropertyRenter> propertyRenterLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (!renterLookup.TryGetBuffer(building, out var renters))
                return;

            int renterStart = renters.Length > 0 ? math.abs(building.Index) % renters.Length : 0;
            for (int ro = 0; ro < renters.Length; ro++)
            {
                if (victims.Length >= maxVictims) break;

                int r = (renterStart + ro) % renters.Length;
                Entity household = renters[r].m_Renter;
                if (propertyRenterLookup.TryGetComponent(household, out var propertyRenter) && propertyRenter.m_Property != building)
                    continue;
                if (!householdLookup.TryGetBuffer(household, out var citizens))
                    continue;

                int citizenStart = citizens.Length > 0 ? math.abs(building.Index + household.Index) % citizens.Length : 0;
                for (int co = 0; co < citizens.Length && victims.Length < maxVictims; co++)
                {
                    int c = (citizenStart + co) % citizens.Length;
                    Entity victim = citizens[c].m_Citizen;
                    _ = TryAddFreshVictim(victim, ref deletedLookup, in processedVictims, selectedVictims, victims);
                }
            }
        }

        /// <summary>
        /// Extract workers from commercial/industrial building companies.
        /// Path: Building в†’ Renter buffer в†’ Company (with CompanyData) в†’ Employee buffer
        /// </summary>
        private static void AddCompanyEmployees(
            Entity building,
            int maxVictims,
            ref BufferLookup<Renter> renterLookup,
            ref BufferLookup<Employee> employeeLookup,
            ref ComponentLookup<CompanyData> companyLookup,
            ref ComponentLookup<PropertyRenter> propertyRenterLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (!renterLookup.TryGetBuffer(building, out var renters))
                return;

            int renterStart = renters.Length > 0 ? math.abs(building.Index) % renters.Length : 0;
            for (int ro = 0; ro < renters.Length; ro++)
            {
                if (victims.Length >= maxVictims) break;

                int r = (renterStart + ro) % renters.Length;
                Entity renter = renters[r].m_Renter;

                // Only companies have employees (not households)
                if (!companyLookup.HasComponent(renter))
                    continue;
                if (propertyRenterLookup.TryGetComponent(renter, out var propertyRenter) && propertyRenter.m_Property != building)
                    continue;

                if (!employeeLookup.TryGetBuffer(renter, out var employees))
                    continue;

                int employeeStart = employees.Length > 0 ? math.abs(building.Index + renter.Index) % employees.Length : 0;
                for (int eo = 0; eo < employees.Length && victims.Length < maxVictims; eo++)
                {
                    int e = (employeeStart + eo) % employees.Length;
                    Entity victim = employees[e].m_Worker;
                    _ = TryAddFreshVictim(victim, ref deletedLookup, in processedVictims, selectedVictims, victims);
                }
            }
        }

        /// <summary>
        /// Extract employees directly from service buildings (power plants, etc).
        /// Path: Building в†’ Employee buffer (direct on building entity)
        /// </summary>
        private static void AddEmployees(
            Entity building,
            int maxVictims,
            ref BufferLookup<Employee> employeeLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (!employeeLookup.TryGetBuffer(building, out var employees))
                return;

            int employeeStart = employees.Length > 0 ? math.abs(building.Index) % employees.Length : 0;
            for (int eo = 0; eo < employees.Length && victims.Length < maxVictims; eo++)
            {
                int e = (employeeStart + eo) % employees.Length;
                Entity victim = employees[e].m_Worker;
                _ = TryAddFreshVictim(victim, ref deletedLookup, in processedVictims, selectedVictims, victims);
            }
        }

        /// <summary>
        /// Extract students from school buildings.
        /// Path: Building в†’ Student buffer (direct on building entity)
        /// </summary>
        private static void AddStudents(
            Entity building,
            int maxVictims,
            ref BufferLookup<Student> studentLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (!studentLookup.TryGetBuffer(building, out var students))
                return;

            int studentStart = students.Length > 0 ? math.abs(building.Index) % students.Length : 0;
            for (int so = 0; so < students.Length && victims.Length < maxVictims; so++)
            {
                int s = (studentStart + so) % students.Length;
                Entity victim = students[s].m_Student;
                _ = TryAddFreshVictim(victim, ref deletedLookup, in processedVictims, selectedVictims, victims);
            }
        }

        /// <summary>
        /// Extract patients from hospital buildings.
        /// Path: Building в†’ Patient buffer (direct on building entity)
        /// </summary>
        private static void AddPatients(
            Entity building,
            int maxVictims,
            ref BufferLookup<Patient> patientLookup,
            ref ComponentLookup<Deleted> deletedLookup,
            in NativeHashSet<int> processedVictims,
            NativeHashSet<int> selectedVictims,
            NativeList<Entity> victims)
        {
            if (!patientLookup.TryGetBuffer(building, out var patients))
                return;

            int patientStart = patients.Length > 0 ? math.abs(building.Index) % patients.Length : 0;
            for (int po = 0; po < patients.Length && victims.Length < maxVictims; po++)
            {
                int p = (patientStart + po) % patients.Length;
                Entity victim = patients[p].m_Patient;
                _ = TryAddFreshVictim(victim, ref deletedLookup, in processedVictims, selectedVictims, victims);
            }
        }
    }
}

