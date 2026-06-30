/**
 * Semantic z-index scale for the mod UI.
 *
 * Every value is strictly BELOW 9999 on purpose. Vanilla CS2 UI
 * (Cities2_Data/Content/Game/UI/index.css) reserves 9999 / 10000 / 10001 /
 * 99999 / 100000 for its own system layers — pause/escape menu, options
 * dialog, critical/loading overlays. The previous mod code used those exact
 * values (9999..100000), so on an equal z-index the DOM order (mod portals
 * mounted last in document.body) put the mod UI ON TOP of vanilla system
 * menus.
 *
 * Keeping the entire mod ceiling under 9999 guarantees vanilla system menus
 * always render above the mod UI. Do NOT reintroduce raw z-index numbers in
 * components — use a token from here. Internal ordering between mod layers is
 * expressed by the scale, not by an escalating "z-index war".
 */
export const Z_INDEX = {
    /** Slightly above panel content (sticky badges, selected rows). */
    raised: 100,
    /** Sticky headers / footers within a scrollable panel. */
    sticky: 1000,
    /** Modals (Help, scenario, preview, settings). */
    modal: 5000,
    /** Dropdowns / popovers portaled through getModRoot(). */
    dropdown: 6000,
    /** Hover tooltips portaled through getModRoot(). */
    tooltip: 6500,
    /** Transient toasts — above modals so confirmations stay visible. */
    toast: 7000,
    /** Mod-critical surface (ErrorBoundary). Highest mod layer, still < 9999. */
    critical: 8000,
} as const;

export type ZIndexToken = keyof typeof Z_INDEX;
