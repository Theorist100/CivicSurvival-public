/**
 * Strike-target naming for the War Room STRIKE view — the target card, the STRIKE OUTCOMES ledger,
 * the C2 bus and the arrival/intercept toasts.
 *
 * Replaces the old cosmetic `warRoomTargets.ts` flavor pool (replacement mandate: one path, no
 * parallel naming). A target's display name is intel-gated exactly like the rest of the STRIKE view:
 *   - L2 (or insider) → the REAL name, a stable localization key picked from the axis pool by the
 *     target's own id, so the same target always reads the same way and the ledger/toast name the
 *     target the card showed;
 *   - below L2       → the anonymised codename "TARGET #NNN" (no real identity leaked before intel).
 *
 * The axis pools are the existing UI_WARROOM_TARGET_* keys (the generator's starting name pool).
 *
 * A resolved outbound strike (ResolvedStrikeDto) carries the TargetId it actually resolved onto, so
 * the ledger names the SAME target the card showed; the launch-frozen `Seed` is only the fallback
 * identity for a strike that resolved onto nothing (TargetId = 65535).
 */

import { type TranslationKey } from "@locales";

export type StrikeAxis = "physical" | "digital" | "social";

const TARGET_POOLS: Record<StrikeAxis, TranslationKey[]> = {
    physical: [
        "UI_WARROOM_TARGET_PHYSICAL_0",
        "UI_WARROOM_TARGET_PHYSICAL_1",
        "UI_WARROOM_TARGET_PHYSICAL_2",
        "UI_WARROOM_TARGET_PHYSICAL_3",
        "UI_WARROOM_TARGET_PHYSICAL_4",
        "UI_WARROOM_TARGET_PHYSICAL_5",
    ],
    digital: [
        "UI_WARROOM_TARGET_DIGITAL_0",
        "UI_WARROOM_TARGET_DIGITAL_1",
        "UI_WARROOM_TARGET_DIGITAL_2",
        "UI_WARROOM_TARGET_DIGITAL_3",
        "UI_WARROOM_TARGET_DIGITAL_4",
        "UI_WARROOM_TARGET_DIGITAL_5",
    ],
    social: [
        "UI_WARROOM_TARGET_SOCIAL_0",
        "UI_WARROOM_TARGET_SOCIAL_1",
        "UI_WARROOM_TARGET_SOCIAL_2",
        "UI_WARROOM_TARGET_SOCIAL_3",
        "UI_WARROOM_TARGET_SOCIAL_4",
        "UI_WARROOM_TARGET_SOCIAL_5",
    ],
};

const AXIS_LABEL_KEY: Record<StrikeAxis, TranslationKey> = {
    physical: "UI_WARROOM_AXIS_PHYSICAL",
    digital: "UI_WARROOM_AXIS_DIGITAL",
    social: "UI_WARROOM_AXIS_SOCIAL",
};

// AttackCategory raw values shared with C# (Kinetic=0/Cyber=1/Psyops=2) — the axisRaw carried by the
// SetStrikeTarget trigger payload.
const AXIS_RAW: Record<StrikeAxis, number> = { physical: 0, digital: 1, social: 2 };

/** Coerce the wire axis string to a known axis (defaults to physical for anything unexpected). */
export const toStrikeAxis = (axis: string): StrikeAxis =>
    axis === "digital" ? "digital" : axis === "social" ? "social" : "physical";

export const axisLabelKey = (axis: StrikeAxis): TranslationKey => AXIS_LABEL_KEY[axis];

/** AttackCategory raw int for the axis — the first arg of the SetStrikeTarget trigger. */
export const axisRawOf = (axis: StrikeAxis): number => AXIS_RAW[axis];

/** Stable real-name (L2) key for a given axis + identity (target id, or a strike seed). */
export const targetNameKey = (axis: StrikeAxis, identity: number): TranslationKey => {
    const pool = TARGET_POOLS[axis];
    const index = Math.abs(Math.trunc(identity)) % pool.length;
    // index is always in range (modulo a non-empty pool); the axis label is a defensive fallback.
    return pool[index] ?? AXIS_LABEL_KEY[axis];
};

// Three-digit codename derived from the identity — anonymised readout below full intel.
// NOT a duration: these bound the "#NNN" codename to the 100..999 range.
const CODENAME_MODULO = 900;
const CODENAME_BASE = 100;
const codeNumber = (identity: number): number => (Math.abs(Math.trunc(identity)) % CODENAME_MODULO) + CODENAME_BASE;

/**
 * Intel-gated display name: the real localized target name at L2 (or with an insider), otherwise the
 * anonymised "TARGET #NNN" codename. `identity` is the target id (snapshot targets, the card) or, when
 * no id is available, the strike seed (the resolved-strike ledger).
 */
export const strikeTargetDisplayName = (
    t: (key: TranslationKey, ...args: (string | number)[]) => string,
    axis: StrikeAxis,
    identity: number,
    intelLevel: number,
): string =>
    intelLevel >= 2
        ? t(targetNameKey(axis, identity))
        : t("UI_WARROOM_TARGET_CODENAME", codeNumber(identity));

/**
 * Display name for an AA-site pick: "AA SITE #NNN" at every intel level. AA sites are defence
 * emplacements, not named objectives — naming one from the axis TARGET pool (power plants, bridges)
 * mislabelled a SAM battery as an offensive installation.
 */
export const aaSiteDisplayName = (
    t: (key: TranslationKey, ...args: (string | number)[]) => string,
    identity: number,
): string => t("UI_WARROOM_AA_CODENAME", codeNumber(identity));
