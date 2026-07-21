/**
 * CIVIC-UI-029: no-div-onclick
 *
 * Disallow `onClick` on `<div>` / `<span>` without an interactive role,
 * tabIndex, and keyboard handler.
 * Interactive elements should use `<button>` for correct semantics and
 * consistent behavior across Coherent UI. A `<div>` with `onClick` but
 * no keyboard path is not keyboard-navigable and signals "accidental div".
 *
 * Allowed: <div onClick={fn} role="button" tabIndex={0} onKeyDown={fn}>
 * Blocked: <div onClick={fn}> (should be <button>)
 *
 * Fix: change to `<button>` or add role, tabIndex, and keyboard handling.
 *
 * Historical: M4 in LeaderboardPanel tab controls (2026-02-23 audit).
 */

"use strict";

const NON_INTERACTIVE = new Set(["div", "span", "li", "td", "tr", "p", "label"]);
const BUTTON_ROLES = new Set(["button", "link", "menuitem", "tab", "switch", "checkbox"]);
const KEY_HANDLERS = new Set(["onKeyDown", "onKeyPress", "onKeyUp"]);
const DELEGATED_CLICK_ATTR = "data-civic-delegated-click";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Disallow onClick on non-interactive elements without role attribute",
        },
        messages: {
            divOnClick:
                "'<{{tag}}>' has onClick. Missing: {{missing}}. " +
                "Use <button>, or add role=\"button\" + tabIndex={0} + onKeyDown handler.",
        },
        schema: [],
    },
    create(context) {
        return {
            JSXOpeningElement(node) {
                // Only check HTML elements (lowercase tag names)
                const tagName =
                    node.name.type === "JSXIdentifier" ? node.name.name : null;
                if (!tagName || !NON_INTERACTIVE.has(tagName)) return;

                const attrs = node.attributes || [];

                const hasOnClick = attrs.some(
                    (attr) =>
                        attr.type === "JSXAttribute" &&
                        attr.name &&
                        attr.name.name === "onClick"
                );
                if (!hasOnClick) return;

                const roleAttr = attrs.find(
                    (attr) =>
                        attr.type === "JSXAttribute" &&
                        attr.name &&
                        attr.name.name === "role"
                );
                const roleValue = getJSXStringValue(roleAttr && roleAttr.value);

                // Ignore if parent is a list item renderer or event delegation container
                // (e.g., <div onClick={handleClick}> wrapping children for event delegation)
                // Requires an explicit project-owned opt-out, not any data-* attribute.
                const hasDelegatedClickAttr = attrs.some(
                    (attr) =>
                        attr.type === "JSXAttribute" &&
                        attr.name &&
                        attr.name.name === DELEGATED_CLICK_ATTR
                );
                if (hasDelegatedClickAttr) return;

                const hasTabIndex = attrs.some(
                    (attr) => attr.type === "JSXAttribute" && attr.name?.name === "tabIndex"
                );
                const hasKeyHandler = attrs.some(
                    (attr) => attr.type === "JSXAttribute" && KEY_HANDLERS.has(attr.name?.name)
                );
                if (roleValue && BUTTON_ROLES.has(roleValue) && hasTabIndex && hasKeyHandler) return;

                const missing = [];
                if (!roleValue || !BUTTON_ROLES.has(roleValue)) missing.push("role");
                if (!hasTabIndex) missing.push("tabIndex");
                if (!hasKeyHandler) missing.push("keyboard handler");

                context.report({
                    node,
                    messageId: "divOnClick",
                    data: { tag: tagName, missing: missing.join(", ") },
                });
            },
        };
    },
};

function getJSXStringValue(value) {
    if (!value) return undefined;
    if (value.type === "Literal" && typeof value.value === "string") return value.value;
    if (
        value.type === "JSXExpressionContainer" &&
        value.expression.type === "Literal" &&
        typeof value.expression.value === "string"
    ) {
        return value.expression.value;
    }
    return undefined;
}
