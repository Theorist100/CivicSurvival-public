/**
 * CIVIC-UI-005: no-empty-imports
 *
 * Empty imports `import { } from "..."` are dead code — nothing is used.
 * Typically left behind after removing all named imports during refactoring.
 *
 * Ignores type-only imports (`import type { } from "..."`) and
 * side-effect imports (`import "..."`) which have no specifiers by design.
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow empty named imports (dead code)",
        },
        messages: {
            noEmptyImport:
                "Empty import from '{{source}}' — nothing is imported. " +
                "Remove the import or add the needed specifiers.",
        },
        schema: [],
    },
    create(context) {
        return {
            ImportDeclaration(node) {
                // Side-effect imports (`import "mod"`) have 0 specifiers by design — skip
                // They have no braces in source, just `import "module"`
                if (node.specifiers.length === 0) {
                    // Check if this is truly `import { } from "..."` vs `import "..."`
                    // Side-effect imports have no importKind override and no braces
                    const sourceCode = context.sourceCode || context.getSourceCode();
                    const text = sourceCode.getText(node);
                    // Side-effect import: `import "react"` or `import "react";`
                    // Empty named import: `import { } from "react"`
                    if (!/\{/.test(text)) return;

                    // Type-only imports: `import type { } from "..."` — skip
                    if (node.importKind === "type") return;

                    context.report({
                        node,
                        messageId: "noEmptyImport",
                        data: { source: node.source.value },
                    });
                }
            },
        };
    },
};
