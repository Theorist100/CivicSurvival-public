/**
 * CIVIC-UI-055: no-italic-font-style
 *
 * CS2 ships no italic face at all (Content\Game\UI\Fonts holds Regular/Bold
 * only), and vanilla never sets font-style: italic anywhere in its own CSS.
 * Asking GameFace for italic makes it synthesize one, and the synthesized face
 * loses the glyph fallback chain: CJK text renders as tofu boxes while Latin
 * survives, so the breakage is invisible until someone plays in Chinese.
 *
 * Historical: the arena ladder showed "空缺" (UI_ARENA_VACANT) as two empty
 * squares on the zh-CN build (2026-07-22) — the vacant-holder style differed
 * from the working one by exactly this property.
 *
 * Use colour, opacity or weight to mark secondary text instead.
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow font-style: italic (CS2 ships no italic face; synthesized italic drops CJK glyphs)",
        },
        messages: {
            italic:
                "font-style: italic is not supported — CS2 ships no italic face, and the synthesized " +
                "one drops the glyph fallback chain, rendering CJK text as tofu boxes. Use colour, " +
                "opacity or fontWeight to mark secondary text.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                const name = node.key.name || node.key.value;
                if (name !== "fontStyle") return;

                // `fontStyle: "italic"` and `fontStyle: "italic" as const`
                const value = node.value.type === "TSAsExpression" ? node.value.expression : node.value;
                if (value.type !== "Literal" || typeof value.value !== "string") return;
                if (value.value.toLowerCase() !== "italic") return;

                context.report({ node: value, messageId: "italic" });
            },
        };
    },
};
