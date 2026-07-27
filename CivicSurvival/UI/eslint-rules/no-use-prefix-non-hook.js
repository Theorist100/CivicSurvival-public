/**
 * CIVIC-UI-034: no-use-prefix-non-hook
 *
 * Warn when a function named `use*` (React hook naming convention) does not
 * call any hooks internally. Such functions are pure helpers, not hooks,
 * and the `use` prefix misleads both developers and the Rules of Hooks linter.
 *
 * Fix: rename to `get*`, `compute*`, `is*`, etc.
 *
 * Historical: H-03 in hooks audit (2026-02-23) — `useAttackCost` and
 * `useCanAfford` were pure arithmetic but named as hooks.
 */

"use strict";

const USE_RE = /^use[A-Z]/;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Warn when a use*-named function contains no hook calls (not a real hook)",
        },
        messages: {
            notAHook:
                "'{{name}}' is named like a React hook but calls no hooks internally. " +
                "Rename to 'get*', 'compute*', 'is*', etc. to avoid confusing the " +
                "Rules of Hooks linter and other developers.",
        },
        schema: [],
    },
    create(context) {
        const definedHookNames = new Set();
        const framesByName = new Map();
        const reportedNodes = new WeakSet();
        const stack = [];

        /**
         * If the function is named use*, push a tracking frame.
         */
        function enterFunction(node) {
            const name = getFunctionName(node);
            if (name && USE_RE.test(name)) {
                const frame = {
                    name,
                    reportNode: getReportNode(node),
                    hasDirectHookCall: false,
                    sameFileHookCalls: new Set(),
                };
                stack.push(frame);
                if (!framesByName.has(name)) {
                    framesByName.set(name, frame);
                }
            }
        }

        function exitFunction(node) {
            const name = getFunctionName(node);
            if (name && USE_RE.test(name) && stack.length > 0) {
                stack.pop();
            }
        }

        return {
            Program(node) {
                collectDefinedHookNames(node, definedHookNames);
            },
            FunctionDeclaration: enterFunction,
            "FunctionDeclaration:exit": exitFunction,
            ArrowFunctionExpression: enterFunction,
            "ArrowFunctionExpression:exit": exitFunction,
            FunctionExpression: enterFunction,
            "FunctionExpression:exit": exitFunction,

            CallExpression(node) {
                if (stack.length === 0) return;

                const callee = node.callee;
                let name = null;

                // useValue(...), useMemo(...)
                if (callee.type === "Identifier") {
                    name = callee.name;
                }
                // React.useMemo(...)
                if (
                    callee.type === "MemberExpression" &&
                    callee.property.type === "Identifier"
                ) {
                    name = callee.property.name;
                }

                if (name && USE_RE.test(name)) {
                    const frame = stack[stack.length - 1];
                    if (definedHookNames.has(name)) {
                        frame.sameFileHookCalls.add(name);
                    } else {
                        frame.hasDirectHookCall = true;
                    }
                }
            },
            "Program:exit"() {
                const memo = new Map();
                for (const frame of framesByName.values()) {
                    if (!containsRealHook(frame, framesByName, memo, new Set())) {
                        if (reportedNodes.has(frame.reportNode)) continue;
                        reportedNodes.add(frame.reportNode);
                        context.report({
                            node: frame.reportNode,
                            messageId: "notAHook",
                            data: { name: frame.name },
                        });
                    }
                }
            },
        };
    },
};

// ============ Helpers ============

/**
 * Extract the name of a function node.
 * - FunctionDeclaration: node.id.name
 * - Arrow/FunctionExpression assigned to VariableDeclarator: parent.id.name
 */
function getFunctionName(node) {
    // function useFoo() {}
    if (node.type === "FunctionDeclaration" && node.id) {
        return node.id.name;
    }

    // const useFoo = () => {} | const useFoo = function() {}
    if (
        (node.type === "ArrowFunctionExpression" ||
            node.type === "FunctionExpression") &&
        node.parent &&
        node.parent.type === "VariableDeclarator" &&
        node.parent.id &&
        node.parent.id.type === "Identifier"
    ) {
        return node.parent.id.name;
    }

    return null;
}

/**
 * Get the AST node to highlight in the report.
 */
function getReportNode(node) {
    if (node.type === "FunctionDeclaration" && node.id) {
        return node.id;
    }
    if (node.parent && node.parent.type === "VariableDeclarator" && node.parent.id) {
        return node.parent.id;
    }
    return node;
}

function collectDefinedHookNames(node, names) {
    if (!node || typeof node.type !== "string") return;
    const name = getFunctionName(node);
    if (name && USE_RE.test(name)) {
        names.add(name);
    }
    for (const key of Object.keys(node)) {
        if (key === "parent") continue;
        const value = node[key];
        if (Array.isArray(value)) {
            for (const child of value) collectDefinedHookNames(child, names);
        } else if (value && typeof value.type === "string") {
            collectDefinedHookNames(value, names);
        }
    }
}

function containsRealHook(frame, framesByName, memo, visiting) {
    if (memo.has(frame.name)) return memo.get(frame.name);
    if (frame.hasDirectHookCall) {
        memo.set(frame.name, true);
        return true;
    }
    if (visiting.has(frame.name)) {
        return false;
    }
    visiting.add(frame.name);
    for (const calledName of frame.sameFileHookCalls) {
        const calledFrame = framesByName.get(calledName);
        if (calledFrame && containsRealHook(calledFrame, framesByName, memo, visiting)) {
            visiting.delete(frame.name);
            memo.set(frame.name, true);
            return true;
        }
    }
    visiting.delete(frame.name);
    memo.set(frame.name, false);
    return false;
}
