/**
 * CIVIC-UI-045: no-modal-ui-mix
 *
 * Keep modal primitives and dashboard UI primitives path-explicit. The shared
 * barrel re-exports modal primitives, so imports from "../shared" can silently
 * select the wrong StatRow/ProgressBar implementation.
 */

"use strict";

const MODAL_PRIMITIVE_NAMES = new Set([
    "StatRow",
    "StatCard",
    "StatCardRow",
    "StatSection",
    "ProgressBar",
    "Badge",
    "Quote",
    "InfoList",
    "InfoListItem",
    "AlertBox",
]);

const OVERLAPPING_NAMES = new Set(["StatRow", "ProgressBar"]);

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: { description: "Force explicit shared/modal and shared/ui primitive imports" },
        messages: {
            modalInNonScenario:
                "shared/modal primitive imported from a non-scenario file. Use shared/ui primitives for dashboard panels.",
            uiInScenario:
                "shared/ui primitive imported in scenario/ file. Use shared/modal primitives for scenario modal palette/spacing.",
            ambiguousBarrel:
                "Importing {{names}} from '{{source}}' is ambiguous. Use explicit '@shared/modal' or '@shared/ui'.",
        },
        schema: [],
    },
    create(context) {
        const filename = context.getFilename();
        const isScenarioFile = /[\\/]scenario[\\/]/.test(filename);
        const isSharedModalFile = /[\\/]shared[\\/]modal[\\/]/.test(filename);

        return {
            ImportDeclaration(node) {
                const source = node.source.value;
                if (typeof source !== "string") return;

                if (isAmbiguousSharedBarrel(source)) {
                    const modalNames = getImportedNames(node).filter((name) =>
                        MODAL_PRIMITIVE_NAMES.has(name)
                    );
                    if (modalNames.length > 0) {
                        context.report({
                            node,
                            messageId: "ambiguousBarrel",
                            data: { names: modalNames.join(", "), source },
                        });
                    }
                    return;
                }

                if (isModalImport(source) && !isScenarioFile && !isSharedModalFile) {
                    context.report({ node, messageId: "modalInNonScenario" });
                    return;
                }

                if (isUiImport(source) && isScenarioFile) {
                    const overlapping = getImportedNames(node).filter((name) =>
                        OVERLAPPING_NAMES.has(name)
                    );
                    if (overlapping.length > 0) {
                        context.report({ node, messageId: "uiInScenario" });
                    }
                }
            },
        };
    },
};

function getImportedNames(node) {
    return node.specifiers
        .filter((specifier) => specifier.type === "ImportSpecifier")
        .map((specifier) => specifier.imported?.name || specifier.imported?.value)
        .filter(Boolean);
}

function isAmbiguousSharedBarrel(source) {
    return (
        source === "@shared" ||
        source === "components/shared" ||
        source.endsWith("/shared") ||
        source.endsWith("\\shared")
    );
}

function isModalImport(source) {
    return source.includes("shared/modal") || source.includes("shared\\modal") || source === "@shared/modal";
}

function isUiImport(source) {
    return source.includes("shared/ui") || source.includes("shared\\ui") || source === "@shared/ui";
}
