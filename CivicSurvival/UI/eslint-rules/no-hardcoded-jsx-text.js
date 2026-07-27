/**
 * CIVIC-UI-025: no-hardcoded-jsx-text
 *
 * Disallow hardcoded user-visible text in JSX. All text must go through
 * the localization system (l.t("KEY")).
 *
 * Catches:
 *   <span>Shadow Cash</span>           — JSXText with 2+ letter chars
 *   <span>{"Shadow Cash"}</span>       — String literal inside JSXExpressionContainer
 *
 * Ignores:
 *   - Whitespace-only JSXText
 *   - Single-char separators (|, ·, —, /, etc.)
 *   - Numbers, symbols, emoji
 *   - JSX attribute values (key="foo", style={...})
 *   - Template literals (handled via l.t() interpolation patterns)
 *   - devtools/ directory (debug-only UI)
 *
 * Historical: M01 across all GridWarfare components (2026-02-23).
 */

"use strict";

const { unwrapTransparent } = require("./_ast");

// At least 2 consecutive ASCII letters = likely a word that needs translation.
const HAS_WORD = /[A-Za-z]{2,}/;

// Paths to exclude (debug panels, test files).
const EXCLUDED_PATH = /[\\/](devtools|__tests__|__mocks__)[\\/]/;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Disallow hardcoded text in JSX — use l.t() for localization",
        },
        messages: {
            noHardcodedText:
                'Hardcoded text "{{text}}" should use the localization system: l.t("KEY").',
        },
        schema: [],
    },
    create(context) {
        const filename = context.getFilename();
        if (EXCLUDED_PATH.test(filename)) return {};

        return {
            // <span>Some text</span>
            JSXText(node) {
                const trimmed = node.value.trim();
                if (!trimmed) return;
                if (!HAS_WORD.test(trimmed)) return;

                context.report({
                    node,
                    messageId: "noHardcodedText",
                    data: {
                        text: trimmed.length > 40
                            ? trimmed.slice(0, 37) + "..."
                            : trimmed,
                    },
                });
            },

            JSXExpressionContainer(node) {
                const parent = node.parent;
                if (!parent || parent.type === "JSXAttribute") return;
                if (parent.type !== "JSXElement" && parent.type !== "JSXFragment") return;
                for (const textNode of findHardcodedText(node.expression)) {
                    const text = textNode.type === "TemplateElement"
                        ? (textNode.value.cooked ?? textNode.value.raw)
                        : textNode.value;
                    if (!HAS_WORD.test(text)) continue;
                    const reportText = text.length > 40 ? text.slice(0, 37) + "..." : text;
                    context.report({
                        node: textNode,
                        messageId: "noHardcodedText",
                        data: { text: reportText },
                    });
                }
            },
        };
    },
};

function findHardcodedText(node) {
    const hits = [];
    const value = unwrapTransparent(node);
    if (!value) return hits;
    if (value.type === "Literal" && typeof value.value === "string") {
        hits.push(value);
        return hits;
    }
    if (value.type === "ConditionalExpression") {
        hits.push(...findHardcodedText(value.consequent));
        hits.push(...findHardcodedText(value.alternate));
        return hits;
    }
    if (value.type === "LogicalExpression") {
        hits.push(...findHardcodedText(value.left));
        hits.push(...findHardcodedText(value.right));
        return hits;
    }
    if (value.type === "TemplateLiteral") {
        for (const quasi of value.quasis) {
            const text = quasi.value.cooked ?? quasi.value.raw;
            if (HAS_WORD.test(text)) hits.push(quasi);
        }
    }
    return hits;
}
