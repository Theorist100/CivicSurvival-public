/**
 * CIVIC-UI-049: no-pointer-events-lock
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "Disallow CSS-only action locks through pointerEvents: none",
        },
        messages: {
            pointerEventsLock:
                "pointerEvents: \"none\" can create CSS-only locks. Use disabled state, or add 'allow-pointer-events-none: decoration only'.",
        },
        schema: [],
    },
    create(context) {
        const source = context.sourceCode ?? context.getSourceCode();
        function hasAllowComment(node) {
            return source.getCommentsBefore(node).some((comment) =>
                comment.value.includes("allow-pointer-events-none: decoration only")
            );
        }

        function unwrapExpression(node) {
            let current = node;
            while (
                current &&
                (current.type === "TSAsExpression" ||
                    current.type === "TSTypeAssertion" ||
                    current.type === "ChainExpression")
            ) {
                current = current.expression;
            }
            return current;
        }

        function containsNoneValue(node) {
            const value = unwrapExpression(node);
            if (!value) return false;

            if (value.type === "Literal") {
                return value.value === "none";
            }

            if (value.type === "TemplateLiteral") {
                return value.expressions.length === 0 &&
                    value.quasis.length === 1 &&
                    value.quasis[0].value.cooked === "none";
            }

            if (value.type === "ConditionalExpression") {
                return containsNoneValue(value.consequent) || containsNoneValue(value.alternate);
            }

            if (value.type === "LogicalExpression") {
                return containsNoneValue(value.left) || containsNoneValue(value.right);
            }

            return false;
        }

        return {
            Property(node) {
                const keyName = node.key.type === "Identifier" ? node.key.name : node.key.value;
                if (keyName !== "pointerEvents") return;
                if (!containsNoneValue(node.value)) return;
                if (hasAllowComment(node) || hasAllowComment(node.parent)) return;
                context.report({ node: node.value, messageId: "pointerEventsLock" });
            },
        };
    },
};
