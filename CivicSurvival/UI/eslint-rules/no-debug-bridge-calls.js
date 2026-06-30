/**
 * CIVIC-UI-016: no-debug-bridge-calls
 *
 * Disallow `scLog()` in the **render path** of React components.
 * Each call crosses the Coherent C#↔JS bridge — in a render body
 * this fires every frame, adding measurable latency.
 *
 * Allowed:
 * - Inside useEffect / useLayoutEffect callbacks (one-time or dep-gated)
 * - Inside event handlers (onClick, onChange, etc.)
 * - Inside named functions defined in the component (helpers called from handlers)
 * - Files under devtools/ directories
 * - Non-component files (hooks, utils, bindings, boot)
 * - ErrorBoundary (crash reporting)
 *
 * Historical: scLog left inside render body fired on every re-render
 * in production SettingsPanel.
 */

"use strict";

const { getCalleeName, isCalleeNamed } = require("./_ast");

const COMPONENT_PATH = /[/\\]components[/\\]/;
const EXCLUDED_FILES = [/ErrorBoundary\.tsx$/];
const DEBUG_FUNCTIONS = new Set(["scLog"]);

// Effect hooks whose callbacks are NOT render-path
const EFFECT_HOOKS = new Set(["useEffect", "useLayoutEffect"]);

/**
 * Walk up the AST to determine if `node` is inside a safe (non-render) scope:
 * - Arrow/function inside useEffect/useLayoutEffect call
 * - Arrow/function that is an event handler prop (onXxx)
 * - Named function declaration inside component (called from handlers)
 */
function isInsideSafeScope(node) {
    let current = node.parent;
    while (current) {
        // useEffect(() => { scLog() })  — safe
        if (
            current.type === "ArrowFunctionExpression" ||
            current.type === "FunctionExpression"
        ) {
            const parent = current.parent;
            // Direct argument to useEffect/useLayoutEffect
            if (
                parent &&
                parent.type === "CallExpression" &&
                isCalleeNamed(parent.callee, EFFECT_HOOKS)
            ) {
                return true;
            }
            // JSX attribute: onClick={() => { scLog() }}
            if (parent && parent.type === "JSXExpressionContainer") {
                return true;
            }
            if (isComponentRootFunction(current)) {
                return false;
            }
            // Any non-root nested function/arrow = not the render body itself.
            if (parent) {
                return true;
            }
        }

        if (current.type === "FunctionDeclaration") {
            return !isComponentRootFunction(current);
        }

        current = current.parent;
    }
    return false;
}

function isComponentRootFunction(node) {
    const name = getFunctionName(node);
    return Boolean(name && /^[A-Z]/u.test(name));
}

function getFunctionName(node) {
    if (node.type === "FunctionDeclaration") {
        return node.id && node.id.name;
    }
    const parent = node.parent;
    if (
        parent &&
        parent.type === "VariableDeclarator" &&
        parent.id.type === "Identifier"
    ) {
        return parent.id.name;
    }
    return null;
}

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Disallow debug bridge calls (scLog) in the render path of components",
        },
        messages: {
            noDebugBridgeCall:
                "'{{name}}()' in render path crosses the C#↔JS bridge every frame. " +
                "Move it into useEffect, an event handler, or a devtools/ file.",
        },
        schema: [],
    },
    create(context) {
        const filename = context.filename || context.getFilename();
        if (
            !COMPONENT_PATH.test(filename) ||
            EXCLUDED_FILES.some((p) => p.test(filename))
        ) {
            return {};
        }

        return {
            CallExpression(node) {
                if (
                    isCalleeNamed(node.callee, DEBUG_FUNCTIONS) &&
                    !isInsideSafeScope(node)
                ) {
                    const name = getCalleeName(node.callee);
                    context.report({
                        node,
                        messageId: "noDebugBridgeCall",
                        data: { name },
                    });
                }
            },
        };
    },
};
