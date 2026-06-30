"use strict";

const TRANSPARENT_TYPES = new Set([
    "TSAsExpression",
    "TSSatisfiesExpression",
    "TSTypeAssertion",
    "TSNonNullExpression",
    "ParenthesizedExpression",
]);

function unwrapTransparent(node) {
    let current = node;
    while (current && TRANSPARENT_TYPES.has(current.type)) {
        current = current.expression;
    }
    return current;
}

function getStringLiteralValue(node) {
    const value = unwrapTransparent(node);
    if (!value) return undefined;
    if (value.type === "Literal" && typeof value.value === "string") {
        return value.value;
    }
    if (value.type === "TemplateLiteral" && value.expressions.length === 0) {
        return value.quasis.map((quasi) => quasi.value.cooked ?? quasi.value.raw).join("");
    }
    return undefined;
}

function getStaticPropertyName(node) {
    const value = unwrapTransparent(node);
    if (!value) return null;
    if (value.type === "Identifier") return value.name;
    if (value.type === "Literal") return String(value.value);
    return null;
}

function getCalleeName(callee) {
    const value = unwrapTransparent(callee);
    if (!value) return null;
    if (value.type === "Identifier") return value.name;
    if (value.type === "MemberExpression") {
        return getStaticPropertyName(value.property);
    }
    return null;
}

function isCalleeNamed(callee, names) {
    const name = getCalleeName(callee);
    return name !== null && names.has(name);
}

function* getStaticStringSegments(node) {
    const value = unwrapTransparent(node);
    if (!value) return;
    if (value.type === "Literal" && typeof value.value === "string") {
        yield value.value;
        return;
    }
    if (value.type === "TemplateLiteral") {
        for (const quasi of value.quasis) {
            yield quasi.value.cooked ?? quasi.value.raw;
        }
    }
}

function sourceTextEquals(context, a, b) {
    const sourceCode = context.sourceCode || context.getSourceCode();
    return sourceCode.getText(unwrapTransparent(a)) === sourceCode.getText(unwrapTransparent(b));
}

module.exports = {
    unwrapTransparent,
    getStringLiteralValue,
    getStaticPropertyName,
    getCalleeName,
    isCalleeNamed,
    getStaticStringSegments,
    sourceTextEquals,
};
