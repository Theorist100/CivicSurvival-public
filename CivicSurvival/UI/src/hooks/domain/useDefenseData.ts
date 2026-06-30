/**
 * Enriched defense command data.
 *
 * Cross-domain hook: AirDefense + Mobilization + FinanceContext. Keeps ammo,
 * manpower, and affordability logic out of DefenseContent.
 */

import { useMemo } from "react";
import { useAccents, useTheme } from "../../themes";
import { useMobilizationDomain } from "./useMobilization";
import { useAirDefense } from "./useThreatDomain";
import { bindingDataOrDefault } from "./useDtoBinding";
import { DEFAULT_AIR_DEFENSE_DTO, DEFAULT_MOBILIZATION_DTO, type AirDefenseDto } from "../../types/domainDtos";
import { AA_TYPE, type AATypeId } from "../../types/semantic";
import type { TranslationKey } from "../../locales";

export type AmmoUnit = "missiles" | "rounds";

/** One per-type ammo bar (display only — the resupply buttons are consolidated below). */
export type AaTypeRow = {
    type: AATypeId;
    /** Locale key for the type label, e.g. UI_DEFENSE_PATRIOT. */
    labelKey: TranslationKey;
    /** "missiles" for Patriot, "rounds" for gun systems. */
    unit: AmmoUnit;
    ammo: number;
    maxAmmo: number;
    percent: number;
    color: string;
    barColor: string;
    lowAmmo: boolean;
};

/** Patriot's own resupply button (dear flat cost, one resupply per wave, excluded from auto refill). */
export type PatriotResupply = {
    canRun: boolean;
    isNeeded: boolean;
    reasonId: string;
    cost: number;
};

/** The single "restock guns" button — all gun types (Bofors/Gepard/Heritage) at one summed cost. */
export type GunsResupply = {
    canRun: boolean;
    isNeeded: boolean;
    reasonId: string;
    cost: number;
};

function getAmmoColor(percent: number, accents: ReturnType<typeof useAccents>, theme: ReturnType<typeof useTheme>): string {
    if (percent < 20) return accents.crisis.accent;
    if (percent < 50) return accents.resilience.accent;
    return theme.colors.textPrimary;
}

function getAmmoBarColor(percent: number, accents: ReturnType<typeof useAccents>): string {
    if (percent < 20) return accents.crisis.accent;
    if (percent < 50) return accents.resilience.accent;
    return accents.schemes.accent;
}

function buildAaTypeRows(
    aa: AirDefenseDto,
    accents: ReturnType<typeof useAccents>,
    theme: ReturnType<typeof useTheme>,
): AaTypeRow[] {
    const defs: Array<{
        type: AATypeId;
        labelKey: TranslationKey;
        unit: AmmoUnit;
        ammo: number;
        maxAmmo: number;
    }> = [
        {
            type: AA_TYPE.PatriotSAM, labelKey: "UI_DEFENSE_PATRIOT", unit: "missiles",
            ammo: aa.PatriotAmmo, maxAmmo: aa.PatriotMaxAmmo,
        },
        {
            type: AA_TYPE.Bofors40mm, labelKey: "UI_DEFENSE_BOFORS", unit: "rounds",
            ammo: aa.BoforsAmmo, maxAmmo: aa.BoforsMaxAmmo,
        },
        {
            type: AA_TYPE.HeritageBofors, labelKey: "UI_DEFENSE_HERITAGE", unit: "rounds",
            ammo: aa.HeritageAmmo, maxAmmo: aa.HeritageMaxAmmo,
        },
        {
            type: AA_TYPE.Gepard, labelKey: "UI_DEFENSE_GEPARD", unit: "rounds",
            ammo: aa.GepardAmmo, maxAmmo: aa.GepardMaxAmmo,
        },
    ];

    // Only types that are actually deployed (MaxAmmo > 0) get a row.
    return defs
        .filter((d) => d.maxAmmo > 0)
        .map((d) => {
            const percent = d.maxAmmo > 0 ? Math.round((d.ammo / d.maxAmmo) * 100) : 0;
            return {
                type: d.type,
                labelKey: d.labelKey,
                unit: d.unit,
                ammo: d.ammo,
                maxAmmo: d.maxAmmo,
                percent,
                color: getAmmoColor(percent, accents, theme),
                barColor: getAmmoBarColor(percent, accents),
                lowAmmo: percent < 20,
            };
        });
}

export function useDefenseData() {
    const aaState = useAirDefense();
    const manpowerState = useMobilizationDomain();
    const theme = useTheme();
    const accents = useAccents();

    const aa = bindingDataOrDefault(aaState, DEFAULT_AIR_DEFENSE_DTO);
    const manpower = bindingDataOrDefault(manpowerState, DEFAULT_MOBILIZATION_DTO);

    return useMemo(() => {
        const manpowerColor = manpower.ManpowerPercent < 25 ? accents.crisis.accent
            : manpower.ManpowerPercent < 50 ? accents.resilience.accent
            : accents.schemes.accent;

        const boforsPrice = aa.BoforsPrice || 10000;
        const gepardPrice = aa.GepardPrice || 25000;
        const patriotPrice = aa.PatriotPrice || 100000;
        const gunsNeeded =
            (aa.BoforsMaxAmmo > 0 && aa.BoforsAmmo < aa.BoforsMaxAmmo) ||
            (aa.GepardMaxAmmo > 0 && aa.GepardAmmo < aa.GepardMaxAmmo) ||
            (aa.HeritageMaxAmmo > 0 && aa.HeritageAmmo < aa.HeritageMaxAmmo);
        return {
            aa,
            manpower,
            ammoRows: buildAaTypeRows(aa, accents, theme),
            patriotResupply: {
                canRun: aa.CanResupplyPatriot,
                isNeeded: aa.PatriotMaxAmmo > 0 && aa.PatriotAmmo < aa.PatriotMaxAmmo,
                reasonId: aa.ResupplyPatriotLockedReasonId,
                cost: aa.PatriotResupplyCost,
            } as PatriotResupply,
            gunsResupply: {
                canRun: aa.CanResupplyGuns,
                isNeeded: gunsNeeded,
                reasonId: aa.ResupplyGunsLockedReasonId,
                cost: aa.GunsResupplyCost,
            } as GunsResupply,
            manpowerColor,
            isAAActive: aa.AaStations > 0,
            boforsPrice,
            gepardPrice,
            patriotPrice,
            bofors: {
                canPlace: aa.CanPlacePaidBofors,
                reasonId: aa.PaidBoforsLockedReasonId,
                cost: boforsPrice,
            },
            gepard: {
                canPlace: aa.CanPlacePaidGepard,
                reasonId: aa.PaidGepardLockedReasonId,
                cost: gepardPrice,
            },
            patriot: {
                canPlace: aa.CanPlacePaidPatriot,
                reasonId: aa.PaidPatriotLockedReasonId,
                cost: patriotPrice,
            },
            callToArms: {
                canRun: manpower.CanCallToArms,
                hasCasualties: manpower.ManpowerCasualties > 0,
                reasonId: manpower.CallToArmsLockedReasonId,
            },
        };
    }, [aa, manpower, theme, accents]);
}
