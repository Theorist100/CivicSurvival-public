/**
 * CIVIC-UI-041: no-duplicate-trigger-binding
 *
 * Detect when multiple exported functions in the same file call trigger()
 * with the same binding name. One of them is likely dead code or a
 * copy-paste with wrong arity.
 *
 * Example:
 *   export const foo = () => trigger(B.Group, B.DoThing);          // 0-arg
 *   export const bar = (x) => trigger(B.Group, B.DoThing, x);    // 1-arg ← duplicate
 *
 * W3 audit S10-01, S3-04, S1-02 — duplicate trigger wrappers with different arity.
 */

"use strict";

const { getCalleeName, getStaticPropertyName, unwrapTransparent } = require("./_ast");

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow multiple exported functions calling trigger() with the same binding name",
        },
        messages: {
            duplicateTrigger:
                "Duplicate trigger binding '{{binding}}': '{{first}}' and '{{second}}' both call " +
                "trigger() with the same binding. One may be dead code or have wrong arity.",
        },
        schema: [],
    },
    create(context) {
        // Track: bindingName → { name: exportedFunctionName, node }
        /** @type {Map<string, { name: string, node: import("eslint").Rule.Node }>} */
        const triggersByBinding = new Map();

        /**
         * Extract the binding name from a trigger() call.
         * Looks for: trigger(B.Group, B.BindingName, ...) or triggerCivic(B.BindingName, ...)
         * Returns the B.BindingName source text or null.
         */
        function extractBindingName(callExpr) {
            if (callExpr.type !== "CallExpression") return null;

            const calleeName = getCalleeName(callExpr.callee);
            const bindingArgIndex = calleeName === "trigger" ? 1
                : calleeName === "triggerCivic" ? 0
                    : -1;
            if (bindingArgIndex < 0 || callExpr.arguments.length <= bindingArgIndex) return null;
            const bindingArg = callExpr.arguments[bindingArgIndex];
            const value = unwrapTransparent(bindingArg);

            // B.BindingName
            if (
                value.type === "MemberExpression" &&
                value.object.type === "Identifier" &&
                value.object.name === "B"
            ) {
                return `B.${getStaticPropertyName(value.property)}`;
            }

            return null;
        }

        /**
         * Find trigger() call inside a function body or expression.
         */
        function findTriggerCall(node) {
            node = unwrapTransparent(node);
            if (!node) return null;

            if (node.type === "CallExpression") {
                const binding = extractBindingName(node);
                if (binding) return { binding, node };
            }

            // Function expression body: () => trigger(...)
            if (
                node.type === "ArrowFunctionExpression" ||
                node.type === "FunctionExpression" ||
                node.type === "FunctionDeclaration"
            ) {
                return findTriggerCall(node.body);
            }

            // Block body: { return trigger(...); }, nested if/switch, or { trigger(...); }
            if (node.type === "BlockStatement") {
                for (const stmt of node.body) {
                    const result = findTriggerCall(stmt);
                    if (result) return result;
                }
            }

            if (node.type === "ExpressionStatement") {
                return findTriggerCall(node.expression);
            }

            if (node.type === "ReturnStatement" && node.argument) {
                return findTriggerCall(node.argument);
            }

            if (node.type === "IfStatement") {
                return findTriggerCall(node.consequent) || findTriggerCall(node.alternate);
            }

            if (node.type === "SwitchStatement") {
                for (const switchCase of node.cases) {
                    for (const consequent of switchCase.consequent) {
                        const result = findTriggerCall(consequent);
                        if (result) return result;
                    }
                }
            }

            return null;
        }

        return {
            "Program:exit"(node) {
                const functionsByName = new Map();
                const exportSpecifiers = [];

                for (const statement of node.body) {
                    collectTopLevelFunctions(statement, functionsByName);
                }

                for (const statement of node.body) {
                    if (statement.type !== "ExportNamedDeclaration") continue;

                    if (statement.declaration) {
                        recordExportedDeclaration(statement.declaration);
                        continue;
                    }

                    for (const specifier of statement.specifiers) {
                        if (
                            specifier.type === "ExportSpecifier" &&
                            specifier.local.type === "Identifier"
                        ) {
                            exportSpecifiers.push({
                                localName: specifier.local.name,
                                exportName: specifier.exported.type === "Identifier"
                                    ? specifier.exported.name
                                    : specifier.local.name,
                            });
                        }
                    }
                }

                for (const specifier of exportSpecifiers) {
                    recordTrigger(
                        specifier.exportName,
                        functionsByName.get(specifier.localName)
                    );
                }
            },
        };

        function collectTopLevelFunctions(statement, functionsByName) {
            if (statement.type === "ExportNamedDeclaration" && statement.declaration) {
                statement = statement.declaration;
            }

            if (statement.type === "FunctionDeclaration" && statement.id) {
                functionsByName.set(statement.id.name, findTriggerCall(statement));
                return;
            }

            if (statement.type !== "VariableDeclaration") return;

            for (const declarator of statement.declarations) {
                if (
                    declarator.type !== "VariableDeclarator" ||
                    !declarator.init ||
                    declarator.id.type !== "Identifier"
                ) {
                    continue;
                }

                const init = unwrapTransparent(declarator.init);
                if (
                    init.type === "ArrowFunctionExpression" ||
                    init.type === "FunctionExpression"
                ) {
                    functionsByName.set(declarator.id.name, findTriggerCall(init));
                }
            }
        }

        function recordExportedDeclaration(declaration) {
            if (declaration.type === "FunctionDeclaration") {
                const exportName = declaration.id ? declaration.id.name : "<anonymous>";
                recordTrigger(exportName, findTriggerCall(declaration));
                return;
            }

            if (declaration.type !== "VariableDeclaration") return;

            for (const declarator of declaration.declarations) {
                if (
                    declarator.type !== "VariableDeclarator" ||
                    declarator.id.type !== "Identifier" ||
                    !declarator.init
                ) {
                    continue;
                }

                const init = unwrapTransparent(declarator.init);
                if (
                    init.type === "ArrowFunctionExpression" ||
                    init.type === "FunctionExpression"
                ) {
                    recordTrigger(declarator.id.name, findTriggerCall(init));
                }
            }
        }

        function recordTrigger(exportName, result) {
            if (!result) return;
            const existing = triggersByBinding.get(result.binding);
            if (existing) {
                context.report({
                    node: result.node,
                    messageId: "duplicateTrigger",
                    data: {
                        binding: result.binding,
                        first: existing.name,
                        second: exportName,
                    },
                });
            } else {
                triggersByBinding.set(result.binding, {
                    name: exportName,
                    node: result.node,
                });
            }
        }
    },
};
