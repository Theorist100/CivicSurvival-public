/**
 * CIVIC-UI-006: no-transition-all
 *
 * Coherent UI crashes or behaves erratically with `transition: "all ..."` or
 * implicit all (e.g. `transition: "0.15s ease"` without a property name).
 * Always specify an explicit CSS property: `transition: "opacity 0.15s ease"`.
 *
 * Historical: dashboard panels flickered/crashed when transition targeted all properties.
 */

"use strict";

const { unwrapTransparent } = require("./_ast");

/**
 * Check if a transition string starts with a CSS property-shaped token.
 * e.g. "opacity 0.3s ease" → true, "0.3s ease" → false, "all 0.3s" → false
 */
function startsWithPropertyName(value) {
    const trimmed = value.trim().toLowerCase();
    if (trimmed.startsWith("all ") || trimmed === "all") return false;
    if (trimmed === "none" || trimmed === "inherit") return true;

    const firstToken = trimmed.match(/^[a-z_][a-z0-9_-]*|^--[a-z0-9_-]+/u);
    if (!firstToken) return false;

    const token = firstToken[0];
    if (token === "all") return false;
    return token.length === trimmed.length || /\s/u.test(trimmed[token.length]);
}

function getTemplateSegments(value) {
    const segments = [];
    let current = "";

    value.quasis.forEach((quasi, index) => {
        current += quasi.value.cooked ?? quasi.value.raw;
        if (index < value.expressions.length) {
            current += "${}";
        }
    });

    for (const segment of current.split(",")) {
        segments.push(segment);
    }

    return segments;
}

function templateSegmentStartsWithExpression(segment) {
    return segment.trimStart().startsWith("${}");
}

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow implicit 'all' in CSS transition (Coherent UI crash risk)",
        },
        messages: {
            noTransitionAll:
                "Transition '{{value}}' has no explicit property name — defaults to 'all', " +
                "which crashes Coherent UI. Use e.g. 'opacity 0.15s ease'.",
            noTransitionBareToken:
                "Transition value is a variable/expression ({{value}}) — likely a bare " +
                "duration+easing without a CSS property name. Use a template literal: " +
                "`opacity ${...}` or `background ${...}, border-color ${...}`.",
            noTransitionTemplateMissingProp:
                "Transition template literal does not start with a CSS property name. " +
                "Use e.g. `opacity ${duration}` instead of `${duration}`.",
        },
        schema: [],
    },
    create(context) {
        return {
            Property(node) {
                if (node.parent && node.parent.type !== "ObjectExpression") return;

                const name = node.key.name || node.key.value;
                if (name !== "transition") return;
                const value = unwrapTransparent(node.value);

                // String literal: transition: "0.3s ease"
                if (
                    value &&
                    value.type === "Literal" &&
                    typeof value.value === "string"
                ) {
                    // Check each comma-separated part (shorthand can list multiple)
                    const parts = value.value.split(",");
                    for (const part of parts) {
                        if (!startsWithPropertyName(part)) {
                            context.report({
                                node: value,
                                messageId: "noTransitionAll",
                                data: { value: value.value },
                            });
                            return;
                        }
                    }
                }

                // MemberExpression or Identifier: likely a bare theme token
                // e.g. transition: theme.effects.transitionFast → "0.15s ease" (no property)
                if (
                    value &&
                    (value.type === "MemberExpression" || value.type === "Identifier")
                ) {
                    const src = context.sourceCode
                        ? context.sourceCode.getText(value)
                        : context.getSourceCode().getText(value);
                    context.report({
                        node: value,
                        messageId: "noTransitionBareToken",
                        data: { value: src },
                    });
                    return;
                }

                // TemplateLiteral: every comma-separated segment must start with a CSS property
                // Good: `background ${fast}` — quasis[0] = "background "
                // Bad:  `${fast}` or `opacity ${fast}, ${slow}` — segment starts with an expression
                if (value && value.type === "TemplateLiteral") {
                    for (const segment of getTemplateSegments(value)) {
                        if (
                            templateSegmentStartsWithExpression(segment) ||
                            !startsWithPropertyName(segment.replace(/\$\{\}/gu, "placeholder"))
                        ) {
                            context.report({
                                node: value,
                                messageId: "noTransitionTemplateMissingProp",
                            });
                            return;
                        }
                    }
                }
            },
        };
    },
};
