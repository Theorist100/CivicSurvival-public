/**
 * CIVIC-UI-047: no-raw-reason-id
 *
 * JSX render of `*.reasonId` must pass through l.tDynamic(...).
 */

"use strict";

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description: "JSX render of *.reasonId must pass through l.tDynamic()",
        },
        messages: {
            rawRender:
                "Reason-id rendered without l.tDynamic() wrapper. Wrap in l.tDynamic({{name}}).",
        },
        schema: [],
    },
    create(context) {
        function isReasonIdMember(node) {
            return (
                node &&
                node.type === "MemberExpression" &&
                node.property &&
                node.property.type === "Identifier" &&
                node.property.name === "reasonId"
            );
        }

        function isInsideTDynamic(node) {
            let current = node.parent;
            while (current) {
                if (
                    current.type === "CallExpression" &&
                    current.callee.type === "MemberExpression" &&
                    current.callee.property.type === "Identifier" &&
                    current.callee.property.name === "tDynamic"
                ) {
                    return true;
                }
                if (current.type === "JSXExpressionContainer") return false;
                current = current.parent;
            }
            return false;
        }

        function report(node) {
            context.report({
                node,
                messageId: "rawRender",
                data: { name: context.getSourceCode().getText(node) },
            });
        }

        function inspectExpression(expr) {
            if (!expr) return;
            if (isInsideTDynamic(expr)) return;
            if (isReasonIdMember(expr)) {
                report(expr);
                return;
            }
            if (expr.type === "LogicalExpression") {
                if (expr.operator === "&&") {
                    inspectExpression(expr.right);
                    return;
                }
                inspectExpression(expr.left);
                inspectExpression(expr.right);
                return;
            }
            if (expr.type === "ConditionalExpression") {
                inspectExpression(expr.consequent);
                inspectExpression(expr.alternate);
                return;
            }
            if (expr.type === "CallExpression") {
                for (const arg of expr.arguments) inspectExpression(arg);
            }
        }

        return {
            JSXExpressionContainer(node) {
                if (!["JSXElement", "JSXFragment"].includes(node.parent.type)) return;
                inspectExpression(node.expression);
            },
        };
    },
};
