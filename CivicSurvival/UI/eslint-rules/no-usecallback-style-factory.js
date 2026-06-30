/**
 * CIVIC-UI-018: no-usecallback-style-factory
 *
 * Detect `useCallback(() => ({ ...style }), [deps])` pattern.
 * `useCallback` memoizes the **function reference**, not its return value.
 * Every call to the memoized function still returns a new object literal,
 * defeating React reconciliation for inline styles.
 *
 * Fix: use `useMemo` with a Map/Record to actually cache style objects,
 * or move the style object outside the component.
 *
 * Historical: Found in ThreatListSection (3 style factories) during UI audit (2026-02-23).
 */

"use strict";

const { isCalleeNamed, unwrapTransparent } = require("./_ast");

const USE_CALLBACK = new Set(["useCallback"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow useCallback that returns a new object literal (memoizes function, not result)",
        },
        messages: {
            styleFactory:
                "useCallback memoizes the function reference, not its return value. " +
                "Each call to this function creates a new {{type}}. " +
                "Use useMemo with a precomputed Map/Record instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            CallExpression(node) {
                // Must be useCallback(...)
                if (!isCalleeNamed(node.callee, USE_CALLBACK)) {
                    return;
                }

                const callback = unwrapTransparent(node.arguments[0]);
                if (!callback) return;

                // Arrow function: () => ({ ... })  or  () => { return { ... }; }
                if (callback.type === "ArrowFunctionExpression") {
                    // Concise body: () => ({...})
                    if (unwrapTransparent(callback.body).type === "ObjectExpression") {
                        context.report({
                            node,
                            messageId: "styleFactory",
                            data: { type: "object" },
                        });
                        return;
                    }

                    // Block body: () => { return {...}; }
                    if (callback.body.type === "BlockStatement") {
                        if (returnsObjectLiteral(callback.body)) {
                            context.report({
                                node,
                                messageId: "styleFactory",
                                data: { type: "object" },
                            });
                            return;
                        }
                    }
                }

                // Regular function expression: function() { return {...}; }
                if (
                    callback.type === "FunctionExpression" &&
                    callback.body.type === "BlockStatement"
                ) {
                    if (returnsObjectLiteral(callback.body)) {
                        context.report({
                            node,
                            messageId: "styleFactory",
                            data: { type: "object" },
                        });
                    }
                }
            },
        };
    },
};

/**
 * Check if every return statement in a block returns an ObjectExpression.
 * Returns true if there is at least one return with an ObjectExpression
 * and no returns with other expression types.
 */
function returnsObjectLiteral(blockStatement) {
    const returns = collectReturnStatements(blockStatement);
    return (
        returns.length > 0 &&
        returns.every((ret) => unwrapTransparent(ret.argument).type === "ObjectExpression")
    );
}

function collectReturnStatements(node, returns = []) {
    if (!node) return returns;
    if (node.type === "ReturnStatement" && node.argument) {
        returns.push(node);
        return returns;
    }

    switch (node.type) {
        case "BlockStatement":
        case "Program":
            for (const child of node.body) collectReturnStatements(child, returns);
            break;
        case "IfStatement":
            collectReturnStatements(node.consequent, returns);
            collectReturnStatements(node.alternate, returns);
            break;
        case "SwitchStatement":
            for (const switchCase of node.cases) {
                for (const consequent of switchCase.consequent) {
                    collectReturnStatements(consequent, returns);
                }
            }
            break;
        case "TryStatement":
            collectReturnStatements(node.block, returns);
            collectReturnStatements(node.handler && node.handler.body, returns);
            collectReturnStatements(node.finalizer, returns);
            break;
    }

    return returns;
}
