/**
 * Shapes shared by every arena ladder (defense, shadow, and whatever ranks next).
 *
 * A ladder always has the same skeleton — tiers with a holder or VACANT, entries
 * with a position and a nickname — and differs only in what it counts. The
 * counted fields live on the board's own entry type; everything here is the part
 * the frame renders without knowing which board it is looking at.
 */

export type RankIconId = "rank1" | "rank2" | "rank3" | "rank4" | "rank5";

export const isRankIconId = (value: string): value is RankIconId =>
    value === "rank1" || value === "rank2" || value === "rank3" || value === "rank4" || value === "rank5";

/** A tier definition as the config delivers it, before holders are resolved. */
export interface RankTierDef {
    name: string;
    minScore: number;
    icon: RankIconId;
}

/** What the frame needs from any entry; boards add their own scored fields. */
export interface LadderEntryBase {
    id: string;
    position: number;
    nickname: string;
}

/** A tier with the player currently holding it, or null for VACANT. */
export interface LadderTier<THolder> {
    name: string;
    minScore: number;
    icon: RankIconId;
    holder: THolder | null;
}
