/**
 * eslint-plugin-civic — Custom ESLint rules for Coherent UI.
 *
 * Rules:
 *   CIVIC-UI-001  no-css-gap              CSS gap not supported in Coherent UI
 *   CIVIC-UI-002  no-px-units             Use rem, not px
 *   CIVIC-UI-003  no-hardcoded-rgba       Use theme tokens, not hardcoded colors
 *   CIVIC-UI-004  no-tiny-font-size       fontSize below 10rem is unreadable
 *   CIVIC-UI-005  no-empty-imports        Empty named imports (dead code)
 *   CIVIC-UI-006  no-transition-all       Implicit 'all' in transition (Coherent crash)
 *   CIVIC-UI-008  no-linear-gradient      linear-gradient (limited Coherent support)
 *   CIVIC-UI-010  require-memo-displayname  memo() without .displayName
 *   CIVIC-UI-011  no-zero-gap             gap="0rem" is the default (dead code)
 *   CIVIC-UI-012  no-sticky               position: "sticky" unsupported in Coherent
 *   CIVIC-UI-013  require-webkit-filter   filter without WebkitFilter prefix
 *   CIVIC-UI-014  no-ref-in-memo          .current inside useMemo (stale value)
 *   CIVIC-UI-015  no-unused-binding       useSafe* assigned to _var (wasted subscription)
 *   CIVIC-UI-016  no-debug-bridge-calls   scLog() in production code
 *   CIVIC-UI-017  no-dead-l10n-fallback  l.t() || "fallback" is dead code
 *   CIVIC-UI-018  no-usecallback-style-factory  useCallback returning new object
 *   CIVIC-UI-019  no-usememo-style-factory     useMemo returning function that creates new objects
 *   CIVIC-UI-020  no-hex-alpha                 `${color}10` creates invalid 8-digit hex
 *   CIVIC-UI-021  no-inline-style-tag          <style> in JSX duplicated on every mount
 *   CIVIC-UI-022  no-svg-g-onclick             onClick on <g> doesn't fire in Coherent
 *   CIVIC-UI-023  no-default-export           Use named exports, not export default
 *   CIVIC-UI-024  no-inset                    CSS inset not supported in Coherent UI
 *   CIVIC-UI-025  no-hardcoded-jsx-text       Unlocalized text literals in JSX
 *   CIVIC-UI-026  require-min-height-flex     Flex column without minHeight (collapse)
 *   CIVIC-UI-027  no-backdrop-filter          backdropFilter unsupported (Chrome 76+)
 *   CIVIC-UI-028  no-em-units                 Use rem, not em
 *   CIVIC-UI-029  no-div-onclick              onClick on non-interactive element without role
 *   CIVIC-UI-031  no-coherent-unsupported-prop  Blocklist of unsupported CSS properties
 *   CIVIC-UI-032  require-webkit-user-select    userSelect needs WebkitUserSelect prefix
 *   CIVIC-UI-033  no-viewport-units             vh/vw/vmin/vmax unreliable in Coherent
 *   CIVIC-UI-039  format-money-consistency        Inline money formatting instead of formatMoney()
 *   CIVIC-UI-040  no-hardcoded-duration           Magic time constants (900, 3600, 86400)
 *   CIVIC-UI-041  no-duplicate-trigger-binding    Multiple exports calling same trigger binding
 *   CIVIC-UI-045  no-modal-ui-mix                 Modal/UI primitive imports must be explicit
 *   CIVIC-UI-047  no-raw-reason-id                JSX render of *.reasonId must use l.tDynamic
 *   CIVIC-UI-048  civic-no-eligibility-threshold-literal  Eligibility DTO categoricals, not UI thresholds
 *   CIVIC-UI-049  no-pointer-events-lock          No CSS-only action locks
 *   CIVIC-UI-050  no-literal-binding-name         Binding helper names must use generated B.*
 *   CIVIC-UI-051  no-unknown-font-family          Only fonts shipped with CS2 resolve in GameFace
 *   CIVIC-UI-052  no-mixed-calc                   calc() mixing % with other units crashes cohtml
 *   CIVIC-UI-053  no-layout-read-in-handlers      Layout queries in event/timer paths crash cohtml
 */

"use strict";

module.exports = {
    rules: {
        "no-css-gap": require("./no-css-gap"),
        "no-px-units": require("./no-px-units"),
        "no-hardcoded-rgba": require("./no-hardcoded-rgba"),
        "no-tiny-font-size": require("./no-tiny-font-size"),
        "no-empty-imports": require("./no-empty-imports"),
        "no-transition-all": require("./no-transition-all"),
        "no-linear-gradient": require("./no-linear-gradient"),
        "require-memo-displayname": require("./require-memo-displayname"),
        "no-zero-gap": require("./no-zero-gap"),
        "no-sticky": require("./no-sticky"),
        "require-webkit-filter": require("./require-webkit-filter"),
        "no-ref-in-memo": require("./no-ref-in-memo"),
        "no-unused-binding": require("./no-unused-binding"),
        "no-debug-bridge-calls": require("./no-debug-bridge-calls"),
        "no-dead-l10n-fallback": require("./no-dead-l10n-fallback"),
        "no-usecallback-style-factory": require("./no-usecallback-style-factory"),
        "no-usememo-style-factory": require("./no-usememo-style-factory"),
        "no-hex-alpha": require("./no-hex-alpha"),
        "no-inline-style-tag": require("./no-inline-style-tag"),
        "no-svg-g-onclick": require("./no-svg-g-onclick"),
        "no-default-export": require("./no-default-export"),
        "no-inset": require("./no-inset"),
        "no-hardcoded-jsx-text": require("./no-hardcoded-jsx-text"),
        "require-min-height-flex": require("./require-min-height-flex"),
        "no-backdrop-filter": require("./no-backdrop-filter"),
        "no-em-units": require("./no-em-units"),
        "no-div-onclick": require("./no-div-onclick"),
        "no-coherent-unsupported-prop": require("./no-coherent-unsupported-prop"),
        "require-webkit-user-select": require("./require-webkit-user-select"),
        "no-viewport-units": require("./no-viewport-units"),
        "no-use-prefix-non-hook": require("./no-use-prefix-non-hook"),
        "no-duplicate-util-import": require("./no-duplicate-util-import"),
        "format-money-consistency": require("./format-money-consistency"),
        "no-hardcoded-duration": require("./no-hardcoded-duration"),
        "no-duplicate-trigger-binding": require("./no-duplicate-trigger-binding"),
        "no-modal-ui-mix": require("./no-modal-ui-mix"),
        "no-raw-reason-id": require("./no-raw-reason-id"),
        "civic-no-eligibility-threshold-literal": require("./civic-no-eligibility-threshold-literal"),
        "no-pointer-events-lock": require("./no-pointer-events-lock"),
        "no-literal-binding-name": require("./no-literal-binding-name"),
        "no-unknown-font-family": require("./no-unknown-font-family"),
        "no-mixed-calc": require("./no-mixed-calc"),
        "no-layout-read-in-handlers": require("./no-layout-read-in-handlers"),
    },
};
