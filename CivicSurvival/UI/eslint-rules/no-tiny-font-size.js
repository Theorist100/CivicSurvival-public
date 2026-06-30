/**
 * CIVIC-UI-004: no-tiny-font-size
 *
 * Coherent UI renders at game resolution — small font sizes become
 * unreadable, especially on 1080p monitors. Minimum readable size
 * in this engine is 10rem.
 *
 * fontSize "9rem" or below → practically invisible at 1080p.
 * fontSize "8rem" → completely unreadable.
 *
 * Historical: scenario tab with 9rem font was unusable.
 */

"use strict";

const MIN_FONT_SIZE = 10;

// Match "Nrem" where N is the numeric font size
const FONT_SIZE_REM = /^(\d+(?:\.\d+)?)rem$/;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow font sizes below " + MIN_FONT_SIZE + "rem (unreadable in Coherent UI)",
        },
        messages: {
            tinyFont:
                "fontSize '{{value}}' is too small for Coherent UI (min {{min}}rem). " +
                "Text will be unreadable at 1080p.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                const propName = node.key.name || node.key.value;
                if (propName !== "fontSize") return;

                if (
                    node.value &&
                    node.value.type === "Literal" &&
                    typeof node.value.value === "string"
                ) {
                    const match = node.value.value.match(FONT_SIZE_REM);
                    if (match) {
                        const size = parseFloat(match[1]);
                        if (size < MIN_FONT_SIZE) {
                            context.report({
                                node: node.value,
                                messageId: "tinyFont",
                                data: {
                                    value: node.value.value,
                                    min: String(MIN_FONT_SIZE),
                                },
                            });
                        }
                    }
                }
            },
        };
    },
};
