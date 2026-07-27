/**
 * CIVIC-UI-015: no-unused-binding
 *
 * Detect `useSafeString` / `useSafeNumber` / `useSafeBoolean` results
 * assigned to an underscore-prefixed variable (convention for "intentionally
 * unused"). These hooks subscribe to a C# binding via the Coherent bridge —
 * each subscription has a real cost (bridge traffic + re-renders) even if
 * the returned value is discarded.
 *
 * If the value isn't needed, remove the hook call entirely.
 *
 * Historical: _currentLocale in SettingsPanel subscribed to locale binding
 * but never used the value — caused unnecessary re-renders on every
 * locale change.
 */

"use strict";

const { unwrapTransparent } = require("./_ast");

const SAFE_HOOKS = new Set([
    "useSafeString",
    "useSafeNumber",
    "useSafeBoolean",
    "useSafeJsonArray",
    "useSafeJsonObject",
    "useSafeJsonArrayWithState",
    "useSafeJsonObjectWithState",
]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow useSafe* binding hooks assigned to unused (_-prefixed) variables",
        },
        messages: {
            noUnusedBinding:
                "'{{variable}}' subscribes to a C# binding via {{hook}}() but the value " +
                "is never used (underscore prefix). Each subscription costs bridge traffic " +
                "and re-renders. Remove the hook call entirely.",
        },
        schema: [],
    },
    create(context) {
        return {
            VariableDeclarator(node) {
                // Pattern: const _foo = useSafeString(binding$, default)
                const hook = getSafeHookName(unwrapTransparent(node.init));
                if (!hook) return;

                if (
                    node.id &&
                    node.id.type === "Identifier" &&
                    node.id.name.startsWith("_")
                ) {
                    report(node, node.id.name, hook);
                    return;
                }

                for (const id of getPatternIdentifiers(node.id)) {
                    if (id.name.startsWith("_")) {
                        report(id, id.name, hook);
                    }
                }
            },
        };

        function report(node, variable, hook) {
            context.report({
                node,
                messageId: "noUnusedBinding",
                data: { variable, hook },
            });
        }
    },
};

function getSafeHookName(node) {
    return (
        node &&
        node.type === "CallExpression" &&
        node.callee.type === "Identifier" &&
        SAFE_HOOKS.has(node.callee.name) &&
        node.callee.name
    );
}

function getPatternIdentifiers(node) {
    if (!node) return [];
    if (node.type === "Identifier") return [node];
    if (node.type === "ArrayPattern") {
        return node.elements.flatMap((element) => getPatternIdentifiers(element));
    }
    if (node.type === "ObjectPattern") {
        return node.properties.flatMap((prop) => {
            if (prop.type === "RestElement") return getPatternIdentifiers(prop.argument);
            return getPatternIdentifiers(prop.value);
        });
    }
    if (node.type === "AssignmentPattern") return getPatternIdentifiers(node.left);
    if (node.type === "RestElement") return getPatternIdentifiers(node.argument);
    return [];
}
