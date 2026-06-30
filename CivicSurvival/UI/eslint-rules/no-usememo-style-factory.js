/**
 * CIVIC-UI-019: no-usememo-style-factory
 *
 * Detect `useMemo(() => (arg) => ({ ...style }), [deps])` pattern.
 * `useMemo` memoizes the **outer function's return value** — which here is
 * an inner arrow that returns a new object on every call.
 * The memoized value is a function reference, so each call to it
 * still creates a fresh object, defeating React reconciliation.
 *
 * Fix: precompute a Record/Map of all possible style variants inside useMemo,
 * then look them up by key instead of calling a factory.
 *
 * Historical: Found in GridOpsSection FooterTabs (tabStyle) during UI audit (2026-02-23).
 */

"use strict";

const { isCalleeNamed, unwrapTransparent } = require("./_ast");

const USE_MEMO = new Set(["useMemo"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow useMemo that returns a function producing new object literals",
        },
        messages: {
            memoFactory:
                "useMemo returns a function that creates a new object on every call. " +
                "The function reference is memoized, but its return value is not. " +
                "Precompute all variants as a Record/Map inside useMemo instead.",
        },
        schema: [],
    },
    create(context) {
        return {
            CallExpression(node) {
                // Must be useMemo(...)
                if (!isCalleeNamed(node.callee, USE_MEMO)) {
                    return;
                }

                const factory = unwrapTransparent(node.arguments[0]);
                if (!factory) return;

                // The outer function passed to useMemo
                const outerBody = getEffectiveBody(factory);
                if (!outerBody) return;

                // Check if the outer function returns an inner function
                const innerFn = getReturnedFunction(factory, outerBody);
                if (!innerFn) return;

                // Check if the inner function returns an object literal
                if (returnsObjectLiteral(innerFn)) {
                    context.report({
                        node,
                        messageId: "memoFactory",
                    });
                }
            },
        };
    },
};

/**
 * Get the effective body expression/statement from a function node.
 * For concise arrows: returns the body expression.
 * For block bodies: returns the BlockStatement.
 */
function getEffectiveBody(fn) {
    if (
        fn.type === "ArrowFunctionExpression" ||
        fn.type === "FunctionExpression"
    ) {
        return unwrapTransparent(fn.body);
    }
    return null;
}

/**
 * Determine if a function returns another function, and return that inner function.
 *
 * Handles:
 *   () => (arg) => ({...})                    — concise arrow returning arrow
 *   () => { return (arg) => ({...}); }        — block arrow returning arrow
 *   () => function(arg) { return {...}; }     — concise arrow returning function expr
 */
function getReturnedFunction(outerFn, outerBody) {
    // Concise arrow: () => <innerFn>
    if (outerFn.type === "ArrowFunctionExpression" && outerBody.type !== "BlockStatement") {
        outerBody = unwrapTransparent(outerBody);
        if (
            outerBody.type === "ArrowFunctionExpression" ||
            outerBody.type === "FunctionExpression"
        ) {
            return outerBody;
        }
        return null;
    }

    // Block body: () => { return <innerFn>; }
    if (outerBody.type === "BlockStatement") {
        const returns = collectReturnStatements(outerBody);
        if (returns.length === 0) return null;

        // All return statements must return a function
        const allFunctions = returns.every(
            (ret) => {
                const argument = unwrapTransparent(ret.argument);
                return (
                    argument.type === "ArrowFunctionExpression" ||
                    argument.type === "FunctionExpression"
                );
            }
        );
        if (allFunctions) {
            return unwrapTransparent(returns[0].argument);
        }
    }

    return null;
}

/**
 * Check if an inner function returns an object literal.
 * Handles concise arrow and block body.
 */
function returnsObjectLiteral(fn) {
    const body = unwrapTransparent(fn.body);

    // Concise arrow: (arg) => ({...})
    if (fn.type === "ArrowFunctionExpression" && body.type === "ObjectExpression") {
        return true;
    }

    // Block body: (arg) => { return {...}; }
    if (body.type === "BlockStatement") {
        const returns = collectReturnStatements(body);
        return (
            returns.length > 0 &&
            returns.every((ret) => unwrapTransparent(ret.argument).type === "ObjectExpression")
        );
    }

    return false;
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
