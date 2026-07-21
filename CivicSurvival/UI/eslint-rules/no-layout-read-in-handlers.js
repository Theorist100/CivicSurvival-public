/**
 * CIVIC-UI-053: no-layout-read-in-handlers
 *
 * Disallow layout-reading DOM properties/methods (getBoundingClientRect,
 * getClientRects, offsetWidth/Height/Top/Left, clientWidth/Height,
 * scrollWidth/Height) outside of useEffect/useLayoutEffect callbacks.
 *
 * Every one of these is a synchronous query that cohtml 1.64 answers from
 * its layout thread. When the query is issued from a pointer-event handler,
 * a setTimeout/setInterval callback or a requestAnimationFrame callback, it
 * races the layout thread that is mid-mutation of the tree; the query walks
 * into a null layout node and the game dies with a native AV (c0000005) —
 * not an exception, a hard crash. Three iterations of "fixes" (per-pixel
 * mousemove measure → synchronous measure on enter → measure deferred to
 * rAF) all crashed identically, because all of them still read layout from
 * the event path. The only safe patterns are: take coordinates from the
 * event data itself (clientX/clientY), or cache a measurement taken inside
 * a useEffect/useLayoutEffect callback (post-commit, tree settled) and read
 * the cache from the handler.
 *
 * Scope decision: default-deny across all of src. A read is allowed only
 * when its nearest "verdict-bearing" enclosing function is a
 * useEffect/useLayoutEffect callback, and it is reported when any closer
 * enclosing function is an event handler (JSX on* attribute, on* object
 * property, addEventListener argument) or a timer/rAF callback — including
 * handlers that are *declared inside* an effect and registered via
 * addEventListener, which a naive "lexically inside useEffect" check would
 * miss. window "resize" listeners are exempt: they fire outside pointer
 * dispatch and re-measuring on resolution change is the sanctioned pattern.
 * window.innerWidth/innerHeight are not flagged at all — viewport size is
 * an engine-known constant, not a layout-tree query.
 */

"use strict";

const { getStaticPropertyName, getStringLiteralValue, unwrapTransparent } = require("./_ast");

const LAYOUT_READS = new Set([
    "getBoundingClientRect",
    "getClientRects",
    "offsetWidth",
    "offsetHeight",
    "offsetTop",
    "offsetLeft",
    "clientWidth",
    "clientHeight",
    "scrollWidth",
    "scrollHeight",
]);

const TIMER_CALLEES = new Set(["setTimeout", "setInterval", "requestAnimationFrame"]);
const EFFECT_CALLEES = new Set(["useEffect", "useLayoutEffect"]);

const FUNCTION_TYPES = new Set([
    "FunctionExpression",
    "ArrowFunctionExpression",
    "FunctionDeclaration",
]);

const TRANSPARENT_TYPES = new Set([
    "TSAsExpression",
    "TSSatisfiesExpression",
    "TSTypeAssertion",
    "TSNonNullExpression",
    "ParenthesizedExpression",
]);

/** Climb from `node` through TS/paren wrappers; returns [effectiveChild, parent]. */
function climbTransparent(node) {
    let child = node;
    let parent = child.parent;
    while (parent && TRANSPARENT_TYPES.has(parent.type)) {
        child = parent;
        parent = parent.parent;
    }
    return [child, parent];
}

function getCalleeBaseName(callee) {
    const value = unwrapTransparent(callee);
    if (!value) return null;
    if (value.type === "Identifier") return value.name;
    if (value.type === "MemberExpression") return getStaticPropertyName(value.property);
    return null;
}

/**
 * Classify the syntactic position where a function value is *used*.
 * `child` is the node occupying that position (the function itself or an
 * identifier referencing it); `parent` is its AST parent.
 * Returns "banned" | "allowed" | null (neutral).
 */
function classifyUsage(child, parent) {
    if (!parent) return null;

    // <div onClick={fn}> / <div onClick={() => ...}>
    if (
        parent.type === "JSXExpressionContainer" &&
        parent.parent &&
        parent.parent.type === "JSXAttribute" &&
        parent.parent.name &&
        /^on[A-Z]/.test(String(parent.parent.name.name))
    ) {
        return "banned";
    }

    // { onClick: fn } — handler passed through a props object
    if (
        parent.type === "Property" &&
        parent.value === child &&
        /^on[A-Z]/.test(String(getStaticPropertyName(parent.key)))
    ) {
        return "banned";
    }

    if (parent.type === "CallExpression" && parent.arguments.includes(child)) {
        const calleeName = getCalleeBaseName(parent.callee);
        if (calleeName === "addEventListener") {
            // window "resize" is the sanctioned re-measure trigger: it fires
            // outside pointer dispatch. Anything else (or a dynamic name) is banned.
            const eventName = getStringLiteralValue(parent.arguments[0]);
            return eventName === "resize" ? null : "banned";
        }
        if (TIMER_CALLEES.has(calleeName)) return "banned";
        if (EFFECT_CALLEES.has(calleeName) && parent.arguments[0] === child) {
            return "allowed";
        }
    }

    return null;
}

/**
 * Classify an enclosing function: where is it used? Checks both the direct
 * syntactic position and — for `const fn = () => {}` / `function fn() {}` —
 * every later reference of the variable (handlers declared inside effects
 * and registered via addEventListener are caught through this path).
 */
function classifyFunction(fn, sourceCode) {
    const [child, parent] = climbTransparent(fn);

    const direct = classifyUsage(child, parent);
    if (direct) return direct;

    let variables = [];
    if (parent && parent.type === "VariableDeclarator" && parent.init === child) {
        variables = sourceCode.getDeclaredVariables(parent);
    } else if (fn.type === "FunctionDeclaration") {
        variables = sourceCode.getDeclaredVariables(fn);
    }
    for (const variable of variables) {
        for (const reference of variable.references) {
            const id = reference.identifier;
            if (id === fn.id) continue;
            const [refChild, refParent] = climbTransparent(id);
            if (classifyUsage(refChild, refParent) === "banned") return "banned";
        }
    }

    return null;
}

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow layout-reading DOM queries outside useEffect/useLayoutEffect (native cohtml crash)",
        },
        messages: {
            noLayoutRead:
                "'{{prop}}' is a synchronous query to the cohtml layout thread; issued from an " +
                "event handler, timer or rAF callback it races the thread mutating the tree and " +
                "null-derefs natively (c0000005 — three shipped crashes of this class). Take " +
                "coordinates from the event data (clientX/clientY), or measure once inside a " +
                "useEffect/useLayoutEffect callback and read the cached value from the handler.",
        },
        schema: [],
    },
    create(context) {
        const sourceCode = context.sourceCode || context.getSourceCode();

        return {
            MemberExpression(node) {
                const prop = getStaticPropertyName(node.property);
                if (!prop || !LAYOUT_READS.has(prop)) return;

                // Walk enclosing functions innermost-first; the first one with a
                // verdict decides. Reaching module/render scope without a verdict
                // means the read is in a render path or plain helper — also banned.
                let current = node.parent;
                let fn = null;
                while (current) {
                    if (FUNCTION_TYPES.has(current.type)) {
                        fn = current;
                        const verdict = classifyFunction(fn, sourceCode);
                        if (verdict === "allowed") return;
                        if (verdict === "banned") break;
                    }
                    current = current.parent;
                }

                context.report({
                    node,
                    messageId: "noLayoutRead",
                    data: { prop },
                });
            },
        };
    },
};
