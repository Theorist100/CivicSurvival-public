/**
 * civic/no-default-export — CIVIC-UI-023
 *
 * Forbids `export default`. All exports must be named.
 * Prevents redundant double-export pattern (named + default).
 */

"use strict";

module.exports = {
    meta: {
        type: "suggestion",
        docs: { description: "Disallow default exports" },
        messages: {
            noDefault: "Use named exports instead of export default.",
        },
    },
    create(context) {
        return {
            ExportDefaultDeclaration(node) {
                context.report({ node, messageId: "noDefault" });
            },
        };
    },
};
