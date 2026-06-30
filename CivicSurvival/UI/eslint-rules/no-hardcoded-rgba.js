/**
 * CIVIC-UI-003: no-hardcoded-rgba
 *
 * Style values should use theme tokens (theme.colors.*, accents.*.accent)
 * instead of hardcoded hex/rgba colors. Hardcoded colors break when
 * switching themes and make the UI inconsistent.
 *
 * Excluded: themes/ (color definitions) and devtools/ (debug-only UI).
 *
 * Historical: ~10 bugs fixed (de9ef614, 8055ef70, cf014460).
 */

"use strict";

const { getStaticStringSegments } = require("./_ast");

// Matches color substrings inside style values.
const HEX_RE = /#[0-9a-fA-F]{3,8}\b/;
const RGB_RE = /\brgba?\([^)]+\)/;

// Paths where hardcoded colors are expected (use both slash styles for Windows)
const EXCLUDED_PATTERNS = [/[/\\]themes[/\\]/, /[/\\]devtools[/\\]/, /[/\\]radar\.ts$/, /[/\\]constants[/\\]/];

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Disallow hardcoded hex/rgba colors in styles (use theme tokens)",
        },
        messages: {
            noHardcodedColor:
                "Use theme.colors.* or accents.*.accent instead of hardcoded '{{value}}'.",
        },
        schema: [],
    },
    create(context) {
        const filename = context.filename || context.getFilename();
        if (EXCLUDED_PATTERNS.some((p) => p.test(filename))) {
            return {};
        }

        return {
            Property(node) {
                for (const segment of getStaticStringSegments(node.value)) {
                    const match = segment.match(HEX_RE) || segment.match(RGB_RE);
                    if (match) {
                        context.report({
                            node: node.value,
                            messageId: "noHardcodedColor",
                            data: { value: match[0] },
                        });
                        return;
                    }
                }
            },
        };
    },
};
