/**
 * CIVIC-UI-051: no-unknown-font-family
 *
 * GameFace only resolves fonts shipped in Content\Game\UI\Fonts. Any other
 * family ('Segoe UI', 'JetBrains Mono', 'Courier New', …) silently falls back
 * to an engine font with incomplete glyph coverage — em dash (U+2014) and
 * Cyrillic render as tofu boxes.
 *
 * Historical: tooltip tofu in PowerPlantTable saturation badge (2026-06-10) —
 * themes declared 'Segoe UI' which never existed in the game.
 */

"use strict";

const FONT_FAMILY_PROPERTIES = new Set(["fontFamily", "fontFamilyMono"]);

// Shipped game fonts (Content\Game\UI\Fonts) + CSS generic families.
// Matching is case-insensitive, as in CSS.
const ALLOWED_FAMILIES = new Set([
    "noto sans",
    "noto sans kr",
    "noto sans jp",
    "noto sans sc",
    "noto sans tc",
    "noto sans thai",
    "noto sans hebrew",
    "noto sans arabic",
    "noto sans bengali",
    "noto sans devanagari",
    "overpass",
    "open sans",
    "source sans pro",
    "perfect dos vga 437",
    "sans-serif",
    "serif",
    "monospace",
]);

const stripQuotes = (family) => family.trim().replace(/^['"]|['"]$/gu, "");

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow font families that are not shipped with CS2 (silent engine fallback with missing glyphs)",
        },
        messages: {
            unknownFamily:
                "Font family '{{family}}' is not shipped with CS2 — GameFace silently substitutes " +
                "an engine font with missing glyphs (em dash, Cyrillic). Use a font from " +
                "Content\\Game\\UI\\Fonts ('Noto Sans', Overpass, 'Open Sans', 'Source Sans Pro', " +
                "'Perfect DOS VGA 437') or a CSS generic family.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (!FONT_FAMILY_PROPERTIES.has(name)) return;

                if (node.value.type !== "Literal" || typeof node.value.value !== "string") return;

                const families = node.value.value.split(",").map(stripQuotes).filter(Boolean);
                for (const family of families) {
                    if (!ALLOWED_FAMILIES.has(family.toLowerCase())) {
                        context.report({
                            node: node.value,
                            messageId: "unknownFamily",
                            data: { family },
                        });
                    }
                }
            },
        };
    },
};
