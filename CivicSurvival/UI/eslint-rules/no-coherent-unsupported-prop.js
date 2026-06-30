/**
 * CIVIC-UI-031: no-coherent-unsupported-prop
 *
 * Blocklist of CSS properties that Coherent UI's old Chromium fork (~Chrome 49)
 * does NOT support. These are silently ignored at runtime — no error, no warning,
 * just missing visual behavior.
 *
 * Easy to extend: add the React camelCase property name to UNSUPPORTED.
 *
 * NOTE: `backdropFilter` is handled by its own rule (CIVIC-UI-027).
 *
 * Historical: C02/C03 in devtools audit (2026-02-23) — fontVariantNumeric
 * and accentColor silently ignored.
 */

"use strict";

/**
 * Map: React camelCase property → Chrome version required.
 * Coherent ships ~Chrome 49; anything above is unsupported.
 */
const UNSUPPORTED = new Map([
    // Typography
    ["fontVariantNumeric", 52],      // tabular-nums, lining-nums, etc.
    ["fontVariantCaps", 52],         // small-caps variant
    ["fontOpticalSizing", 79],       // optical sizing for variable fonts
    ["fontVariationSettings", 62],   // variable font axes

    // Form controls
    ["accentColor", 93],            // color of checkboxes/range inputs
    ["colorScheme", 81],            // light/dark preference

    // Scroll
    ["overscrollBehavior", 63],     // overscroll containment
    ["overscrollBehaviorX", 63],
    ["overscrollBehaviorY", 63],
    ["scrollBehavior", 61],         // smooth scrolling
    ["scrollSnapType", 69],         // scroll snapping
    ["scrollSnapAlign", 69],
    ["scrollMargin", 69],
    ["scrollPadding", 69],

    // Layout
    ["aspectRatio", 88],            // intrinsic aspect ratio
    ["contain", 52],                // CSS containment (partial in 52, full in 69+)
    ["contentVisibility", 85],      // skip rendering offscreen

    // Text decoration
    ["textDecorationSkipInk", 64],  // skip ink for underlines
    ["textDecorationThickness", 89],
    ["textUnderlineOffset", 87],

    // Misc
    ["caretColor", 57],             // text cursor color
    ["touchAction", 55],            // not relevant in Coherent (no touch)
]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow CSS properties unsupported in Coherent UI (Chrome 49)",
        },
        messages: {
            unsupported:
                "'{{prop}}' is not supported in Coherent UI (requires Chrome {{chrome}}+). " +
                "This property is silently ignored at runtime.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                const obj = node.parent;
                if (!obj || obj.type !== "ObjectExpression") return;

                // Skip config objects passed as function arguments:
                //   createBaseModalStyles({ accentColor: ... })
                // Real CSS lives in JSX style={{}}, useMemo(() => ({})), etc.
                // where the ObjectExpression is NOT a direct CallExpression arg.
                if (obj.parent && obj.parent.type === "CallExpression"
                    && obj.parent.arguments && obj.parent.arguments.includes(obj)) {
                    return;
                }

                const name = node.key.name || node.key.value;
                const chrome = UNSUPPORTED.get(name);
                if (chrome !== undefined) {
                    context.report({
                        node,
                        messageId: "unsupported",
                        data: { prop: name, chrome: String(chrome) },
                    });
                }
            },
        };
    },
};
