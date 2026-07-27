/**
 * CIVIC-UI-022: no-svg-g-onclick
 *
 * Coherent UI does not fire onClick events on SVG <g> elements.
 * Use SVG-level hit testing instead (onClick on the root <svg>, then
 * calculate which element was clicked via coordinates).
 *
 * Historical: LOW-03 in shared/radar/ThreatRadar.tsx (2026-02-23).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow onClick on SVG <g> elements (Coherent UI does not fire these events)",
        },
        messages: {
            noSvgGOnClick:
                "onClick on <g> does not fire in Coherent UI. " +
                "Use SVG-level hit testing on the parent <svg> instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            JSXOpeningElement(node) {
                if (
                    node.name.type !== "JSXIdentifier" ||
                    node.name.name !== "g"
                ) {
                    return;
                }

                const hasOnClick = node.attributes.some(
                    (attr) =>
                        attr.type === "JSXAttribute" &&
                        attr.name.type === "JSXIdentifier" &&
                        attr.name.name === "onClick",
                );

                if (hasOnClick) {
                    context.report({
                        node,
                        messageId: "noSvgGOnClick",
                    });
                }
            },
        };
    },
};
