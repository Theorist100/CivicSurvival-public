/**
 * CIVIC-UI-054: no-file-served-icons
 *
 * Disallow referencing icon art as file URLs (string/template literal). ALL
 * art ships inside the bundle — svg glyphs as inline JSX (iconGlyphs.tsx),
 * raster thumbnails as data URIs (buildingThumbnails.ts). A file-served image
 * re-introduces failure modes this project already paid for:
 *
 *  1. Host-map dependency: coui://ui-mods resolution shares a non-thread-safe
 *     host map with every installed mod; a squatter key drop blanked ALL mod
 *     icons for a player (v0.2.1 incident, memory: mod-ui-assets-via-ui-mods-host).
 *  2. Mid-session mod-folder wipe: a Paradox update deletes the install dir
 *     while the bundle keeps running from memory — every file URL 404s
 *     (bug report #37, 2026-07-14, all 19 cs-icons blanked for a player).
 *  3. Engine SVG-image loads were an unproven suspect of the cohtml native AV
 *     (CRASH_TRIAGE.md §5) — eliminated by inlining on 2026-07-20.
 *
 * Fix: svg → add the glyph markup to iconGlyphs.tsx and register a component
 * in Icons.tsx; raster → import via `@buildingIcons/*` (webpack asset/inline)
 * and map it in buildingThumbnails.ts.
 */

"use strict";

// A string that looks like an svg file reference: ends with `.svg`
// (optionally followed by a query/fragment). Case-insensitive.
const SVG_URL_PATTERN = /\.svg(?:[?#][^\s"'`]*)?$/iu;

// Any reference into the retired file-served icon folder (the old
// coui://ui-mods/cs-icons/ host path), regardless of extension.
const CS_ICONS_PATTERN = /cs-icons\//iu;

/** @type {import("eslint").Rule.RuleModule} */
module.exports = {
    meta: {
        type: "problem",
        docs: {
            description:
                "Disallow file-served icon URLs — svg glyphs are inline JSX (iconGlyphs.tsx), raster art is inline data URIs (buildingThumbnails.ts)",
        },
        messages: {
            noSvgUrl:
                "SVG must not be referenced as a file ('{{value}}'). Inline the glyph " +
                "in iconGlyphs.tsx and register a component in Icons.tsx — file-served " +
                "svg depends on the shared coui host map (icons went blank for a player " +
                "when another mod dropped it) and was a cohtml-crash suspect (CRASH_TRIAGE §5).",
            noCsIconsUrl:
                "cs-icons/ file URLs are retired ('{{value}}'). Import the raster via " +
                "@buildingIcons/* (webpack inlines it as a data URI) and map it in " +
                "buildingThumbnails.ts — file-served images 404 when a Paradox update " +
                "wipes the mod folder mid-session (bug report #37).",
        },
        schema: [],
    },
    create(context) {
        const check = (node, value) => {
            if (typeof value !== "string") return;
            const trimmed = value.trim();
            if (CS_ICONS_PATTERN.test(trimmed)) {
                context.report({ node, messageId: "noCsIconsUrl", data: { value: trimmed } });
                return;
            }
            if (!SVG_URL_PATTERN.test(trimmed)) return;
            context.report({ node, messageId: "noSvgUrl", data: { value: trimmed } });
        };

        return {
            Literal(node) {
                check(node, node.value);
            },
            TemplateLiteral(node) {
                // Flag when the LAST quasi ends with .svg — `${host}globe.svg`.
                // cs-icons/ can sit in ANY quasi (`coui://ui-mods/cs-icons/${name}`),
                // so every quasi is checked for it.
                for (const quasi of node.quasis) {
                    if (CS_ICONS_PATTERN.test(quasi.value.raw)) {
                        context.report({
                            node: quasi,
                            messageId: "noCsIconsUrl",
                            data: { value: quasi.value.raw.trim() },
                        });
                        return;
                    }
                }
                const last = node.quasis[node.quasis.length - 1];
                if (last) check(last, last.value.raw);
            },
        };
    },
};
