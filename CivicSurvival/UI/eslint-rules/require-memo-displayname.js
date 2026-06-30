/**
 * CIVIC-UI-010: require-memo-displayname
 *
 * When a component is wrapped in `memo()`, React DevTools shows it as
 * <Anonymous> unless `.displayName` is set. This rule requires that every
 * `const Foo = memo(...)` has a matching `Foo.displayName = "Foo"` in the
 * same file scope.
 *
 * Covers:
 *   const Foo = memo(() => { ... });
 *   const Foo: React.FC<Props> = memo(({ ... }) => { ... });
 *   export const Foo = memo(() => { ... });
 *
 * Historical: 7 of 8 memo components in cognitive/ section lacked displayName,
 * making React DevTools debugging impossible.
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Require .displayName on memo() components for React DevTools",
        },
        messages: {
            missingDisplayName:
                "'{{name}}' is wrapped in memo() but missing .displayName. " +
                'Add: {{name}}.displayName = "{{name}}";',
        },
        schema: [],
    },
    create(context) {
        // Collect: { name → node } for memo() declarations
        const memoComponents = new Map();
        // Collect: set of names that have .displayName assignments
        const displayNames = new Set();

        /**
         * Check if a call expression is `memo(...)` or `React.memo(...)`.
         */
        function isMemoCall(node) {
            if (node.type !== "CallExpression") return false;
            const callee = node.callee;
            // memo(...)
            if (callee.type === "Identifier" && callee.name === "memo") return true;
            // React.memo(...)
            if (
                callee.type === "MemberExpression" &&
                callee.object.type === "Identifier" &&
                callee.object.name === "React" &&
                callee.property.type === "Identifier" &&
                callee.property.name === "memo"
            ) {
                return true;
            }
            return false;
        }

        return {
            // const Foo = memo(...) or export const Foo = memo(...)
            VariableDeclarator(node) {
                if (!node.id || node.id.type !== "Identifier") return;
                if (!node.init) return;

                // Handle: memo(...) directly or memo(...) with TS type assertion
                let initNode = node.init;
                // Skip TypeScript `as` / `satisfies` wrappers
                if (initNode.type === "TSAsExpression" || initNode.type === "TSSatisfiesExpression") {
                    initNode = initNode.expression;
                }

                if (isMemoCall(initNode)) {
                    memoComponents.set(node.id.name, node);
                }
            },

            // Foo.displayName = "Foo"
            ExpressionStatement(node) {
                const expr = node.expression;
                if (expr.type !== "AssignmentExpression") return;
                if (expr.operator !== "=") return;

                const left = expr.left;
                if (
                    left.type === "MemberExpression" &&
                    left.object.type === "Identifier" &&
                    left.property.type === "Identifier" &&
                    left.property.name === "displayName"
                ) {
                    displayNames.add(left.object.name);
                }
            },

            "Program:exit"() {
                for (const [name, node] of memoComponents) {
                    if (!displayNames.has(name)) {
                        context.report({
                            node,
                            messageId: "missingDisplayName",
                            data: { name },
                        });
                    }
                }
            },
        };
    },
};
