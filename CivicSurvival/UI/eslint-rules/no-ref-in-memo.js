/**
 * CIVIC-UI-014: no-ref-in-memo
 *
 * Detect `.current` access inside `useMemo` callback body.
 * Refs are mutable — `.current` read at memo computation time becomes stale
 * when the ref changes but memo deps don't. This is a common React footgun.
 *
 * useCallback is NOT flagged: `.current` inside useCallback is read at call
 * time (not creation time), so it always gets the latest value.
 *
 * Historical: Dashboard drag position (positionRef.current) inside useMemo
 * caused panel to snap to stale coordinates on domain/viewType change.
 */

"use strict";

const { isCalleeNamed } = require("./_ast");

const USE_MEMO = new Set(["useMemo"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow .current ref access inside useMemo (stale value risk)",
        },
        messages: {
            noRefInMemo:
                "'.current' read inside useMemo captures the value at computation time. " +
                "If the ref changes without a deps change, the memo returns stale data. " +
                "Move the .current read outside useMemo or use state instead.",
        },
        schema: [],
    },
    create(context) {
        // Stack tracks whether we're inside a useMemo callback.
        // Depth > 0 means we're inside one (handles nested useMemo).
        let memoDepth = 0;

        function isUseMemoCallback(node) {
            const parent = node.parent;
            return (
                parent &&
                parent.type === "CallExpression" &&
                isCalleeNamed(parent.callee, USE_MEMO) &&
                parent.arguments[0] === node
            );
        }

        return {
            // Enter useMemo callback (arrow function or function expression)
            "CallExpression > ArrowFunctionExpression"(node) {
                if (isUseMemoCallback(node)) memoDepth++;
            },
            "CallExpression > ArrowFunctionExpression:exit"(node) {
                if (isUseMemoCallback(node)) memoDepth--;
            },
            "CallExpression > FunctionExpression"(node) {
                if (isUseMemoCallback(node)) memoDepth++;
            },
            "CallExpression > FunctionExpression:exit"(node) {
                if (isUseMemoCallback(node)) memoDepth--;
            },

            // Flag .current access inside memo callback
            MemberExpression(node) {
                if (
                    memoDepth > 0 &&
                    !node.computed &&
                    node.property.type === "Identifier" &&
                    node.property.name === "current"
                ) {
                    context.report({ node, messageId: "noRefInMemo" });
                }
            },
        };
    },
};
