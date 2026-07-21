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
import type { AaPlacementOptionEntry } from "../../types/domainDtos.generated";
import { AMMO_KIND, type AmmoKind } from "../../types/semantic";
import type { TranslationKey } from "../../locales";

export type AmmoUnit = "missiles" | "rounds";

/**
 * One barrel as the panel shows it: a tile, plus every source it can be fielded through. Built by
 * grouping the backend roster on Prefab — rows sharing a prefab are the same barrel acquired
 * differently (the reserve grant vs. buying one), which is exactly the choice the tile offers.
 *
 * Nothing here names an AA. Add one to AAPlacementCatalog on the C# side and it appears.
 */
export type AaTile = {
    prefab: string;
    /** Headline row — the tile's label, icon and range come from it. */
    headline: AaPlacementOptionEntry;
    sources: AaPlacementOptionEntry[];
    deployed: number;
};

function buildRoster(aa: AirDefenseDto): AaTile[] {
    const tiles: AaTile[] = [];
    const byPrefab = new Map<string, AaTile>();
    // Catalogue order is the display order; grouping must not reshuffle it.
    for (const row of aa.AaRosterJson) {
        const existing = byPrefab.get(row.Prefab);
        if (existing) {
            existing.sources.push(row);
            existing.deployed = Math.max(existing.deployed, row.Deployed);
            continue;
        }
        const tile: AaTile = { prefab: row.Prefab, headline: row, sources: [row], deployed: row.Deployed };
        byPrefab.set(row.Prefab, tile);
        tiles.push(tile);
    }
    // Prefer a bought source as the headline: it is the barrel's standard form, while a grant is a
    // worn-out or donated special case.
    for (const tile of tiles) {
        const paid = tile.sources.find((r) => r.CreditsLeft < 0);
        if (paid) tile.headline = paid;
    }
    return tiles;
}

/**
 * One ammo bar, keyed by munition kind — the axis the player decides on, and the axis the two
 * resupply buttons below have always been priced on. Rockets are dear and gate-limited; shells are
 * cheap and bought in bulk. Between a Bofors and a Gepard there is no decision, so they share a bar.
 */
export type MunitionRow = {
    kind: AmmoKind;
    /** Locale key for the kind label. */
    labelKey: TranslationKey;
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

function buildMunitionRows(
    aa: AirDefenseDto,
    accents: ReturnType<typeof useAccents>,
    theme: ReturnType<typeof useTheme>,
): MunitionRow[] {
    const defs: Array<{
        kind: AmmoKind;
        labelKey: TranslationKey;
        unit: AmmoUnit;
        ammo: number;
        maxAmmo: number;
    }> = [
        {
            kind: AMMO_KIND.Rockets, labelKey: "UI_DEFENSE_ROCKETS", unit: "missiles",
            ammo: aa.PatriotAmmo, maxAmmo: aa.PatriotMaxAmmo,
        },
        {
            kind: AMMO_KIND.Shells, labelKey: "UI_DEFENSE_SHELLS", unit: "rounds",
            ammo: aa.GunsAmmo, maxAmmo: aa.GunsMaxAmmo,
        },
    ];

    // Only kinds actually fielded (MaxAmmo > 0) get a row.
    return defs
        .filter((d) => d.maxAmmo > 0)
        .map((d) => {
            const percent = d.maxAmmo > 0 ? Math.round((d.ammo / d.maxAmmo) * 100) : 0;
            return {
                kind: d.kind,
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

        const gunsNeeded = aa.GunsMaxAmmo > 0 && aa.GunsAmmo < aa.GunsMaxAmmo;
        return {
            aa,
            manpower,
            ammoRows: buildMunitionRows(aa, accents, theme),
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
            roster: buildRoster(aa),
            callToArms: {
                canRun: manpower.CanCallToArms,
                hasCasualties: manpower.ManpowerCasualties > 0,
                reasonId: manpower.CallToArmsLockedReasonId,
            },
        };
    }, [aa, manpower, theme, accents]);
}
