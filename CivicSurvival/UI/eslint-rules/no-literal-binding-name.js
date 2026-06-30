"use strict";

const SECOND_ARG_TARGETS = new Set(["trigger", "bindValue"]);
const FIRST_ARG_TARGETS = new Set(["triggerCivic", "bindCivicValue"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow raw string literals as binding names in trigger/binding calls",
        },
        messages: {
            noRawBindingString:
                "Binding name '{{name}}' is a raw string literal — use B.{{suggested}} " +
                "for compile-time safety. Raw strings bypass typo detection.",
        },
        schema: [],
    },
    create(context) {
        return {
            CallExpression(node) {
                if (node.callee.type !== "Identifier")
                    return;

                const targetIndex = SECOND_ARG_TARGETS.has(node.callee.name)
                    ? 1
                    : FIRST_ARG_TARGETS.has(node.callee.name)
                        ? 0
                        : -1;
                if (targetIndex < 0)
                    return;

                if (node.arguments.length <= targetIndex)
                    return;

                const bindingArg = node.arguments[targetIndex];

                if (bindingArg.type !== "Literal" || typeof bindingArg.value !== "string")
                    return;

                const rawName = bindingArg.value;
                const suggested = rawName.replace(/[^a-zA-Z0-9]/g, "");

                context.report({
                    node: bindingArg,
                    messageId: "noRawBindingString",
                    data: { name: rawName, suggested },
                });
            },
        };
    },
};
