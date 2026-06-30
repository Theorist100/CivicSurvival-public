"use strict";

const ELIGIBILITY_IDENTIFIERS = new Set([
    "tensionLevel",
    "wearPercent",
    "operationalDamagePercent",
    "disasterDamagePercent",
    "trustScore",
    "threatLevel",
    "heatLevel",
    "fundBalance",
    "shadowBalance",
    "cityBudget",
    "manpower",
    "casualtyCount",
]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Forbid raw threshold literals on known eligibility identifiers; consume the published categorical instead.",
        },
        messages: {
            literalThreshold:
                "Threshold comparison '{{identifier}} {{operator}} {{value}}' is forbidden. Consume the published categorical from the DTO instead.",
        },
        schema: [],
    },
    create(context) {
        function getIdentifierName(node) {
            let current = node;
            while (current && current.type === "MemberExpression") {
                current = current.property;
            }
            return current && current.type === "Identifier" ? current.name : null;
        }

        return {
            BinaryExpression(node) {
                if (!["<", "<=", ">", ">="].includes(node.operator)) return;
                if (node.right.type !== "Literal" || typeof node.right.value !== "number") return;

                const identifier = getIdentifierName(node.left);
                if (!identifier || !ELIGIBILITY_IDENTIFIERS.has(identifier)) return;

                context.report({
                    node,
                    messageId: "literalThreshold",
                    data: {
                        identifier,
                        operator: node.operator,
                        value: String(node.right.value),
                    },
                });
            },
        };
    },
};
