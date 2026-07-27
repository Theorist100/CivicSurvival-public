export interface FormattedCaughtError {
    message: string;
    stack: string;
}

export function formatCaughtError(error: unknown): FormattedCaughtError {
    if (error instanceof Error) {
        return {
            message: error.message || error.name || "Unknown Error",
            stack: error.stack || "no stack",
        };
    }

    if (error === null) {
        return { message: "null", stack: "no stack" };
    }

    if (error === undefined) {
        return { message: "undefined", stack: "no stack" };
    }

    if (typeof error === "string") {
        return { message: error, stack: "no stack" };
    }

    if (typeof error === "number" || typeof error === "boolean" || typeof error === "bigint") {
        return { message: String(error), stack: "no stack" };
    }

    if (typeof error === "symbol" || typeof error === "function") {
        return { message: String(error), stack: "no stack" };
    }

    try {
        return { message: JSON.stringify(error) ?? Object.prototype.toString.call(error), stack: "no stack" };
    } catch {
        return { message: Object.prototype.toString.call(error), stack: "no stack" };
    }
}
