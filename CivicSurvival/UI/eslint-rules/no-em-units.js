/**
 * CIVIC-UI-028: no-em-units
 *
 * Disallow `em` units in style values. The project convention is `rem`
 * for all sizing (per UI_COHERENT_BEST_PRACTICES.md). Using `em` produces
 * sizes relative to the parent font-size, which behaves unpredictably in
 * Coherent UI's scaled coordinate system and creates visual inconsistency
 * with the rest of the UI (which uses `rem`).
 *
 * Fix: replace `em` with `rem`. E.g. `letterSpacing: "0.1em"` → `"0.5rem"`.
 *
 * Historical: M2 in LeaderboardPanel/GridWarfareWindow (2026-02-23 audit).
 */

"use strict";

// Matches numeric value followed by `em` at word boundary (not `rem`)
const EM_PATTERN = /\d+\.?\d*em\b/;

// Properties where `em` is commonly misused
const STYLE_PROPERTIES = new Set([
    "letterSpacing", "fontSize", "lineHeight", "margin", "marginTop",
    "marginRight", "marginBottom", "marginLeft", "padding", "paddingTop",
    "paddingRight", "paddingBottom", "paddingLeft", "width", "height",
    "minWidth", "minHeight", "maxWidth", "maxHeight", "top", "left",
    "right", "bottom", "gap", "rowGap", "columnGap", "borderRadius",
    "border", "borderTop", "borderRight", "borderBottom", "borderLeft",
    "borderWidth", "outline", "textIndent", "wordSpacing",
]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow em units in styles (use rem for Coherent UI)",
        },
        messages: {
            noEm:
                "Use 'rem' instead of 'em' in Coherent UI. Found: '{{value}}'. " +
                "The project convention is rem for all sizing.",
        },
        schema: [],
    },
    create(context) {
        /** Report if value string contains `em` units */
        function checkString(node, value, propName) {
            if (typeof value !== "string") return;
            if (!EM_PATTERN.test(value)) return;
            // Only flag known CSS style properties to avoid false positives
            if (propName && !STYLE_PROPERTIES.has(propName)) return;

            context.report({
                node,
                messageId: "noEm",
                data: { value },
            });
        }

        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const propName = node.key.name || node.key.value;

                // String literal: letterSpacing: "0.1em"
                if (
                    node.value &&
                    node.value.type === "Literal" &&
                    typeof node.value.value === "string"
                ) {
                    checkString(node.value, node.value.value, propName);
                }

                // Template literal: letterSpacing: `${x}em`
                if (node.value && node.value.type === "TemplateLiteral") {
                    for (const quasi of node.value.quasis) {
                        checkString(quasi, quasi.value.raw, propName);
                    }
                }
            },
        };
    },
};
