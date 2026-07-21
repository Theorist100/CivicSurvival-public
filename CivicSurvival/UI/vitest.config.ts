import fs from "node:fs";
import path from "node:path";
import { defineConfig } from "vitest/config";

const root = __dirname;
const tsconfigPath = path.resolve(root, "tsconfig.json");

interface TsConfig {
    compilerOptions?: {
        paths?: Record<string, string[]>;
    };
}

function escapeRegExp(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function toVitePath(value: string): string {
    return value.replace(/\\/g, "/");
}

function resolveTsPath(target: string): string {
    return toVitePath(path.resolve(root, target));
}

function tsconfigPathAliases() {
    const config = JSON.parse(fs.readFileSync(tsconfigPath, "utf8")) as TsConfig;
    const paths = config.compilerOptions?.paths ?? {};

    return Object.entries(paths).map(([source, targets]) => {
        const target = targets[0];
        if (!target) {
            throw new Error(`tsconfig path '${source}' has no target`);
        }

        if (source.endsWith("/*") && target.endsWith("/*")) {
            const sourcePrefix = source.slice(0, -2);
            const targetPrefix = target.slice(0, -2);
            return {
                find: new RegExp(`^${escapeRegExp(sourcePrefix)}/(.+)$`),
                replacement: `${resolveTsPath(targetPrefix)}/$1`,
            };
        }

        return {
            find: new RegExp(`^${escapeRegExp(source)}$`),
            replacement: resolveTsPath(target),
        };
    });
}

export default defineConfig({
    test: {
        environment: "jsdom",
        globals: false,
        include: ["tests/**/*.test.{ts,tsx}"],
    },
    resolve: {
        alias: tsconfigPathAliases(),
    },
});
