/**
 * CIVIC-UI-040: no-hardcoded-duration
 *
 * Detect hardcoded time duration constants in UI components.
 * These should come from the DTO (C# controls timing) or be extracted
 * into named constants for clarity and maintainability.
 *
 * Flagged values: 900, 3600, 86400 (15min, 1hr, 1day in seconds)
 * and their millisecond equivalents: 900000, 3600000, 86400000.
 *
 * Allowed contexts:
 *   - CSS transition/animation values (small numbers, different domain)
 *   - Import/require statements
 *   - Type annotations
 *   - Comments
 *   - devtools/ directory files
 *
 * W3 audit S6-02 — Ring timer hardcoded phaseDuration=900s instead of DTO value.
 */

"use strict";

const MAGIC_DURATIONS = new Set([900, 3600, 86400, 900000, 3600000, 86400000]);

const DEVTOOLS_PATH = /[/\\]devtools[/\\]/;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "suggestion",
        docs: {
            description:
                "Disallow hardcoded time duration constants (900, 3600, 86400) in components",
        },
        messages: {
            noHardcodedDuration:
                "Hardcoded duration {{value}}s detected. Use a named constant or read " +
                "from the DTO — C# controls timing, not the UI.",
        },
        schema: [],
    },
    create(context) {
        const filename = context.filename || context.getFilename();
        if (DEVTOOLS_PATH.test(filename)) {
            return {};
        }

        return {
            Literal(node) {
                if (typeof node.value !== "number")
                    return;

                if (!MAGIC_DURATIONS.has(node.value))
                    return;

                // Skip if inside a style object (CSS transitions use small numbers)
                if (isInsideStyleObject(node))
                    return;

                // Skip if it's a constant declaration name containing "SECONDS" etc.
                // (the definition is fine, usage of the literal elsewhere is not)
                if (isInNamedConstant(node))
                    return;

                context.report({
                    node,
                    messageId: "noHardcodedDuration",
                    data: { value: String(node.value) },
                });
            },
        };
    },
};

function isInsideStyleObject(node) {
    let current = node.parent;
    while (current) {
        // style={{ ... }} — JSX attribute named "style"
        if (
            current.type === "JSXAttribute" &&
            current.name &&
            current.name.name === "style"
        ) {
            return true;
        }
        current = current.parent;
    }
    return false;
}

function isInNamedConstant(node) {
    let current = node.parent;
    while (current) {
        if (current.type === "VariableDeclarator" && current.id) {
            const name = current.id.name || "";
            // SECONDS_PER_DAY, PHASE_DURATION, etc. — definition is OK
            if (/^[A-Z_]+$/.test(name)) {
                return true;
            }
        }
        current = current.parent;
    }
    return false;
}
