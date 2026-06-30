// @ts-check
import eslint from "@eslint/js";
import tseslint from "typescript-eslint";
import reactPlugin from "eslint-plugin-react";
import reactHooksPlugin from "eslint-plugin-react-hooks";
import globals from "globals";
import importPlugin from "eslint-plugin-import";
import unicornPlugin from "eslint-plugin-unicorn";
import sonarjsPlugin from "eslint-plugin-sonarjs";
import civicPlugin from "./eslint-rules/index.js";

export default tseslint.config(
    // ── Ignore patterns ──────────────────────────────────────────
    {
        ignores: ["build/", "node_modules/", "webpack.config.js", "eslint-rules/", "src/**/*.js"],
    },
    {
        files: ["src/**/*.{ts,tsx}"],
        ignores: ["src/actions/**", "src/hooks/actions/**"],
        rules: {
            "no-restricted-imports": ["error", {
                patterns: [
                    {
                        group: [
                            "actions/*",
                            "@actions/*",
                            "../actions/*",
                            "../../actions/*",
                            "../../../actions/*",
                            "../../../../actions/*",
                            "../../../../../actions/*",
                            "../../../../../../actions/*",
                        ],
                        message: "Raw trigger wrappers in src/actions are internal. Import feature action hooks from @hooks/actions instead.",
                    },
                ],
            }],
        },
    },

    // ── Base: ESLint recommended ─────────────────────────────────
    eslint.configs.recommended,

    // ── TypeScript: recommended (type-aware OFF — no project ref) ─
    ...tseslint.configs.recommended,

    // ── React + Civic (Coherent UI rules) ──────────────────────────
    {
        plugins: {
            react: reactPlugin,
            "react-hooks": reactHooksPlugin,
            civic: civicPlugin,
            import: importPlugin,
            unicorn: unicornPlugin,
            sonarjs: sonarjsPlugin,
        },
        languageOptions: {
            globals: {
                ...globals.browser,
            },
            parserOptions: {
                ecmaFeatures: { jsx: true },
            },
        },
        settings: {
            react: { version: "18.3" },
        },
        rules: {
            // ── React Hooks (CRITICAL — prevents silent breakage) ──
            "react-hooks/rules-of-hooks": "error",
            "react-hooks/exhaustive-deps": "warn",

            // ── React JSX ──────────────────────────────────────────
            "react/jsx-key": "error",
            "react/jsx-no-duplicate-props": "error",
            "react/no-children-prop": "error",
            "react/no-direct-mutation-state": "error",
            "react/react-in-jsx-scope": "off", // React 17+ JSX transform

            // ── TypeScript ─────────────────────────────────────────
            "@typescript-eslint/no-unused-vars": ["warn", {
                argsIgnorePattern: "^_",
                varsIgnorePattern: "^_",
            }],
            "@typescript-eslint/no-explicit-any": "warn",
            "@typescript-eslint/no-non-null-assertion": "warn",
            "@typescript-eslint/consistent-type-imports": ["warn", {
                prefer: "type-imports",
                fixStyle: "inline-type-imports",
            }],

            // ── General quality ────────────────────────────────────
            "no-console": "error",  // All logging must go through scLog/scWarn/scError → CivicSurvival.log
            "no-debugger": "error",
            "no-duplicate-imports": "off", // superseded by import/no-duplicates
            "eqeqeq": ["error", "always", { null: "ignore" }],
            "no-var": "error",
            "prefer-const": "warn",
            "no-restricted-syntax": ["error", {
                selector: "Identifier[name='SegmentedControl']",
                message: "Use SegmentedTabs from @shared/ui; SegmentedControl name is not allowed.",
            }],

            // ── Civic: Coherent UI engine rules ─────────────────────
            "civic/no-css-gap": "error",        // CIVIC-UI-001: gap not supported
            "civic/no-px-units": "error",       // CIVIC-UI-002: use rem, not px
            "civic/no-hardcoded-rgba": "warn",  // CIVIC-UI-003: use theme tokens
            "civic/no-tiny-font-size": "error", // CIVIC-UI-004: min 10rem font
            "civic/no-empty-imports": "error",  // CIVIC-UI-005: dead empty imports
            "civic/no-transition-all": "error", // CIVIC-UI-006: implicit all crashes Coherent
            "civic/no-linear-gradient": "warn", // CIVIC-UI-008: limited gradient support
            "civic/require-memo-displayname": "warn", // CIVIC-UI-010: memo needs .displayName
            "civic/no-zero-gap": "warn",        // CIVIC-UI-011: gap="0" is default (dead code)
            "civic/no-sticky": "error",         // CIVIC-UI-012: sticky unsupported in Coherent
            "civic/require-webkit-filter": "warn", // CIVIC-UI-013: filter needs -webkit- prefix
            "civic/no-ref-in-memo": "warn",     // CIVIC-UI-014: .current in useMemo = stale
            "civic/no-unused-binding": "warn",  // CIVIC-UI-015: useSafe* → _var = wasted sub
            "civic/no-debug-bridge-calls": "warn", // CIVIC-UI-016: scLog() in production
            "civic/no-dead-l10n-fallback": "error", // CIVIC-UI-017: l.t() || "fallback" is dead code
            "civic/no-usecallback-style-factory": "error", // CIVIC-UI-018: useCallback returning new object
            "civic/no-usememo-style-factory": "error", // CIVIC-UI-019: useMemo returning function factory
            "civic/no-hex-alpha": "error",            // CIVIC-UI-020: ${color}XX creates invalid 8-digit hex
            "civic/no-inline-style-tag": "error",     // CIVIC-UI-021: <style> in JSX duplicated per mount
            "civic/no-svg-g-onclick": "error",       // CIVIC-UI-022: onClick on <g> dead in Coherent
            "civic/no-default-export": "warn",       // CIVIC-UI-023: use named exports only
            "civic/no-inset": "error",               // CIVIC-UI-024: inset unsupported in Coherent
            "civic/no-hardcoded-jsx-text": "warn",   // CIVIC-UI-025: use l.t() for all visible text
            "civic/require-min-height-flex": "warn",  // CIVIC-UI-026: flex column needs minHeight
            "civic/no-backdrop-filter": "error",       // CIVIC-UI-027: backdropFilter unsupported (Chrome 76+)
            "civic/no-em-units": "error",              // CIVIC-UI-028: use rem, not em
            "civic/no-div-onclick": "warn",            // CIVIC-UI-029: onClick on non-interactive without role
            "civic/no-coherent-unsupported-prop": "error", // CIVIC-UI-031: blocklist of unsupported CSS props
            "civic/require-webkit-user-select": "warn",    // CIVIC-UI-032: userSelect needs WebkitUserSelect
            "civic/no-viewport-units": "error",            // CIVIC-UI-033: vh/vw unreliable in Coherent subview
            "civic/no-use-prefix-non-hook": "warn",       // CIVIC-UI-034: use* name but no hook calls inside
            "civic/no-duplicate-util-import": "error",    // CIVIC-UI-035: safeJsonParse only from utils/jsonParse
            "civic/format-money-consistency": "warn",          // CIVIC-UI-039: use formatMoney(), not inline / 1000
            "civic/no-hardcoded-duration": "warn",             // CIVIC-UI-040: magic time constants (900, 3600, 86400)
            "civic/no-duplicate-trigger-binding": "error",    // CIVIC-UI-041: multiple exports calling same trigger binding
            "civic/no-modal-ui-mix": "error",                 // CIVIC-UI-045: modal/ui primitive imports must be explicit
            "civic/no-raw-reason-id": "error",                // CIVIC-UI-047: reason ids must be localized
            "civic/civic-no-eligibility-threshold-literal": "error", // CIVIC-UI-048: DTO categories over raw thresholds
            "civic/no-pointer-events-lock": "error",          // CIVIC-UI-049: disabled state over CSS-only pointer lock
            "civic/no-literal-binding-name": "error",         // CIVIC-UI-050: binding helper names must use generated B.*
            "civic/no-unknown-font-family": "error",          // CIVIC-UI-051: only fonts shipped with CS2 resolve in GameFace
            "civic/no-mixed-calc": "error",                   // CIVIC-UI-052: calc() mixing % with other units crashes cohtml
            "civic/no-layout-read-in-handlers": "error",      // CIVIC-UI-053: layout queries in event/timer paths crash cohtml

            // ── Import hygiene ────────────────────────────────────────
            "import/first": "error",                      // imports must come before any other statements
            "import/no-duplicates": "error",           // merge duplicate import lines
            "import/no-self-import": "error",          // import from own file = bug
            "import/no-mutable-exports": "error",      // export let = shared mutable state
            "import/no-named-as-default": "warn",      // import Foo when Foo is named export
            "import/no-cycle": "warn",                 // circular imports
            "import/no-useless-path-segments": "warn",   // ./foo/../bar → ./bar

            // ── Unicorn: code quality ─────────────────────────────────
            "unicorn/no-abusive-eslint-disable": "error",   // bare eslint-disable without rule name
            "unicorn/no-negation-in-equality-check": "error", // !a === b is always a bug
            "unicorn/throw-new-error": "error",              // throw Error() → throw new Error()
            "unicorn/no-empty-file": "warn",                 // empty files = dead code
            "unicorn/no-useless-spread": "warn",             // [...arr] when not needed
            "unicorn/no-useless-undefined": "warn",          // fn(undefined) → fn()
            "unicorn/no-lonely-if": "warn",                  // else { if } → else if
            "unicorn/no-zero-fractions": "warn",             // 1.0 → 1
            "unicorn/prefer-includes": "warn",               // indexOf !== -1 → includes()
            "unicorn/prefer-number-properties": "warn",      // isNaN → Number.isNaN
            "unicorn/prefer-optional-catch-binding": "warn", // catch(e) unused → catch {}
            "unicorn/prefer-string-slice": "warn",           // substr → slice
            "unicorn/prefer-array-flat-map": "warn",         // map().flat() → flatMap()
            "unicorn/prefer-math-min-max": "warn",           // ternary → Math.min/max
            "unicorn/prefer-logical-operator-over-ternary": "warn", // a ? a : b → a || b
            "unicorn/consistent-function-scoping": "warn",   // inner fn without closure → hoist

            // ── SonarJS: bug detection ───────────────────────────────
            "sonarjs/no-identical-expressions": "error",     // a === a, a && a
            "sonarjs/no-identical-conditions": "error",      // same condition in if/else if chain
            "sonarjs/no-all-duplicated-branches": "error",   // if/else with identical body
            "sonarjs/no-element-overwrite": "error",         // arr[i]=x; arr[i]=y; (first dead)
            "sonarjs/no-hook-setter-in-body": "error",       // setState during render = infinite loop
            "sonarjs/jsx-no-leaked-render": "warn",          // {count && <X/>} renders "0" when count=0
            "sonarjs/no-duplicated-branches": "warn",        // some branches with same body
            "sonarjs/no-collapsible-if": "warn",             // nested if without else → merge
            "sonarjs/no-redundant-boolean": "warn",          // if(x) return true else return false
            "sonarjs/prefer-single-boolean-return": "warn",  // same — simplify boolean return
            "sonarjs/no-redundant-jump": "warn",             // useless return/break/continue
            "sonarjs/no-unused-collection": "warn",          // array filled but never read
            "sonarjs/no-gratuitous-expressions": "warn",     // always true/false condition
            "sonarjs/no-identical-functions": "warn",        // copy-pasted functions
            "sonarjs/cognitive-complexity": ["warn", 25],    // function too complex (threshold 25)
            "sonarjs/no-nested-conditional": "off",          // too noisy — ternaries in JSX are inevitable

            // ── Off (not relevant for CS2 Coherent UI) ─────────────
            "no-undef": "off", // TypeScript handles this
            "@typescript-eslint/ban-ts-comment": "off", // Needed for cs2/api quirks
            "@typescript-eslint/no-empty-function": "off",
            "@typescript-eslint/no-inferrable-types": "off",
        },
    },

    // ── Coming Soon placeholders — not yet localized ─────────────
    {
        files: [
            "src/components/Dashboard/ContentPanel/views/ArenaPreviewContent.tsx",
            "src/components/Dashboard/ContentPanel/views/GlobalOperationsContent.tsx",
            "src/components/Dashboard/ContentPanel/views/OpsAnnounceContent.tsx",
            "src/components/Dashboard/ContentPanel/views/RoadmapContent.tsx",
        ],
        rules: {
            "civic/no-hardcoded-jsx-text": "off",
        },
    },
);
