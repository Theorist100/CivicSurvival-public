/**
 * CIVIC-UI-021: no-inline-style-tag
 *
 * Disallow <style> JSX elements inside components. Each mount injects a new
 * <style> DOM element, causing duplicate @keyframes/@font-face rules.
 *
 * Fix: inject styles at module level with a duplicate guard:
 *   if (!document.querySelector('[data-my-animation]')) {
 *       const el = document.createElement("style");
 *       el.setAttribute('data-my-animation', 'true');
 *       el.textContent = `@keyframes ...`;
 *       document.head.appendChild(el);
 *   }
 *
 * Historical: HIGH-01 in shared/common/Spinner.tsx (2026-02-23).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow <style> JSX elements (duplicated on every mount — use module-level injection with guard)",
        },
        messages: {
            noInlineStyleTag:
                "<style> in JSX injects a new DOM element on every mount. " +
                "Move to module-level injection with a data-attribute duplicate guard.",
        },
        schema: [],
    },
    create(context) {
        return {
            JSXOpeningElement(node) {
                if (
                    node.name.type === "JSXIdentifier" &&
                    node.name.name === "style"
                ) {
                    context.report({
                        node,
                        messageId: "noInlineStyleTag",
                    });
                }
            },
        };
    },
};
