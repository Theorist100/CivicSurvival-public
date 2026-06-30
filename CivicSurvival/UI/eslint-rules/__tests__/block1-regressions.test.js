"use strict";

const { test } = require("node:test");
const { RuleTester } = require("eslint");
const parser = require("@typescript-eslint/parser");

const languageOptions = {
    parser,
    ecmaVersion: 2022,
    sourceType: "module",
    parserOptions: {
        ecmaFeatures: { jsx: true },
    },
};

function runRule(name, rule, cases) {
    test(name, () => {
        const tester = new RuleTester({ languageOptions });
        tester.run(name, rule, cases);
    });
}

runRule("no-literal-binding-name", require("../no-literal-binding-name"), {
    valid: [
        'const a = bindValue(B.Group, B.UiTheme, 0);',
        'const b = bindCivicValue(B.UiTheme, 0);',
        'triggerCivic(B.SetLanguage, "en-US");',
    ],
    invalid: [
        {
            code: 'const a = bindValue(B.Group, "uiTheme", 0);',
            errors: [{ messageId: "noRawBindingString" }],
        },
        {
            code: 'const b = bindCivicValue("uiTheme", 0);',
            errors: [{ messageId: "noRawBindingString" }],
        },
        {
            code: 'triggerCivic("setLanguage", "en-US");',
            errors: [{ messageId: "noRawBindingString" }],
        },
    ],
});

runRule("no-hardcoded-rgba", require("../no-hardcoded-rgba"), {
    valid: ['const style = { boxShadow: theme.shadows.panel };'],
    invalid: [
        {
            code: 'const style = { boxShadow: "0 4rem 20rem rgba(0, 0, 0, 0.5)" };',
            errors: [{ messageId: "noHardcodedColor" }],
        },
    ],
});

runRule("format-money-consistency", require("../format-money-consistency"), {
    valid: ["const label = formatMoney(value);"],
    invalid: [
        {
            code: "const label = `${value / 1000000}M`;",
            errors: [{ messageId: "useFormatMoney" }],
        },
        {
            code: 'const label = (value / 1000000000) + "B";',
            errors: [{ messageId: "useFormatMoney" }],
        },
    ],
});

runRule("no-hardcoded-jsx-text", require("../no-hardcoded-jsx-text"), {
    valid: ['const el = <span>{l.t("UI_OK")}</span>;'],
    invalid: [
        {
            code: 'const el = <span>{enabled ? "Enabled" : "Disabled"}</span>;',
            errors: [
                { messageId: "noHardcodedText" },
                { messageId: "noHardcodedText" },
            ],
        },
        {
            code: "const el = <span>{`Manual ${count}`}</span>;",
            errors: [{ messageId: "noHardcodedText" }],
        },
    ],
});

runRule("no-div-onclick", require("../no-div-onclick"), {
    valid: [
        {
            code: 'const el = <div onClick={fn} role="button" tabIndex={0} onKeyDown={fn} />;',
        },
    ],
    invalid: [
        {
            code: 'const el = <div onClick={fn} data-no-drag />;',
            errors: [{ messageId: "divOnClick" }],
        },
        {
            code: 'const el = <div onClick={fn} role="button" />;',
            errors: [{ messageId: "divOnClick" }],
        },
        {
            code: 'const el = <div onClick={fn} role="button" tabIndex={0} />;',
            errors: [{ messageId: "divOnClick" }],
        },
    ],
});

runRule("no-duplicate-trigger-binding", require("../no-duplicate-trigger-binding"), {
    valid: [
        "export const one = () => bridge.trigger(B.Group, B.One); export const two = () => triggerCivic(B.Two);",
    ],
    invalid: [
        {
            code: "export const one = () => bridge.trigger(B.Group, B.Toggle); export const two = () => triggerCivic(B.Toggle);",
            errors: [{ messageId: "duplicateTrigger" }],
        },
        {
            code: "const one = () => { if (ok) { return bridge.trigger(B.Group, B.Toggle); } }; const two = () => triggerCivic(B.Toggle); export { one, two };",
            errors: [{ messageId: "duplicateTrigger" }],
        },
    ],
});

runRule("no-usecallback-style-factory", require("../no-usecallback-style-factory"), {
    valid: ["const style = useMemo(() => ({ opacity: 1 }), []);"],
    invalid: [
        {
            code: "const styleFactory = useCallback(() => ({ opacity: 1 } as const), []);",
            errors: [{ messageId: "styleFactory" }],
        },
        {
            code: "const styleFactory = React.useCallback(() => { if (active) return { opacity: 1 } as const; return { opacity: 0 }; }, [active]);",
            errors: [{ messageId: "styleFactory" }],
        },
    ],
});

runRule("no-usememo-style-factory", require("../no-usememo-style-factory"), {
    valid: ["const styles = useMemo(() => ({ active: { opacity: 1 } }), []);"],
    invalid: [
        {
            code: "const styleFactory = React.useMemo(() => ((active) => ({ opacity: active ? 1 : 0 }) as const), []);",
            errors: [{ messageId: "memoFactory" }],
        },
        {
            code: "const styleFactory = useMemo(() => { if (active) return (tone) => ({ color: tone } as const); return () => ({ color: 'red' }); }, [active]);",
            errors: [{ messageId: "memoFactory" }],
        },
    ],
});

runRule("no-debug-bridge-calls", require("../no-debug-bridge-calls"), {
    valid: [
        {
            filename: "src/components/Foo.tsx",
            code: "function Foo() { useEffect(() => { logger.scLog('ready'); }, []); return <div />; }",
        },
        {
            filename: "src/components/Foo.tsx",
            code: "function Foo() { const handleClick = () => logger.scLog('click'); return <button onClick={handleClick} />; }",
        },
    ],
    invalid: [
        {
            filename: "src/components/Foo.tsx",
            code: "function Foo() { logger.scLog('render'); return <div />; }",
            errors: [{ messageId: "noDebugBridgeCall" }],
        },
        {
            filename: "src/components/Foo.tsx",
            code: "const Foo = () => { scLog('render'); return <div />; };",
            errors: [{ messageId: "noDebugBridgeCall" }],
        },
    ],
});

runRule("no-ref-in-memo", require("../no-ref-in-memo"), {
    valid: ["const value = useCallback(() => ref.current, []);"],
    invalid: [
        {
            code: "const value = React.useMemo(() => ref.current + 1, []);",
            errors: [{ messageId: "noRefInMemo" }],
        },
    ],
});

runRule("no-hex-alpha", require("../no-hex-alpha"), {
    valid: ['const style = { color: hexToRgba(theme.colors.accent, 0.5) };'],
    invalid: [
        {
            code: 'const style = { backgroundColor: theme.colors.accent + "AA" };',
            errors: [{ messageId: "noHexAlpha" }],
        },
        {
            code: 'const style = { backgroundColor: "#FFFFFF" + "AA" };',
            errors: [{ messageId: "noHexAlpha" }],
        },
    ],
});

runRule("no-px-units", require("../no-px-units"), {
    valid: ['const style = { border: "2rem solid red" };'],
    invalid: [
        {
            code: "const style = { border: `1px solid ${color}` };",
            errors: [{ messageId: "noPx" }],
        },
        {
            code: "el.style.width = `${width}px`;",
            errors: [{ messageId: "noPx" }],
        },
    ],
});

runRule("no-transition-all", require("../no-transition-all"), {
    valid: [
        'const style = { transition: `opacity ${duration}` as const };',
        'const style = { transition: "clip-path 0.2s ease" };',
        'const style = { transition: `opacity ${fast}, background-color ${slow}` };',
    ],
    invalid: [
        {
            code: "const style = { transition: `${duration}` as const };",
            errors: [{ messageId: "noTransitionTemplateMissingProp" }],
        },
        {
            code: "const style = { transition: `opacity ${fast}, ${slow}` };",
            errors: [{ messageId: "noTransitionTemplateMissingProp" }],
        },
        {
            code: "const style = { transition: theme.effects.transitionFast as const };",
            errors: [{ messageId: "noTransitionBareToken" }],
        },
    ],
});

runRule("no-unused-binding", require("../no-unused-binding"), {
    valid: ["const value = useSafeString(binding$, \"\");"],
    invalid: [
        {
            code: 'const _currentLocale = useSafeString(binding$, "") as string;',
            errors: [{ messageId: "noUnusedBinding" }],
        },
    ],
});

runRule("no-viewport-units", require("../no-viewport-units"), {
    valid: ['const style = { maxHeight: "90rem" };'],
    invalid: [
        {
            code: "const style = { height: `${panelHeight}vh` };",
            errors: [{ messageId: "noViewportUnits" }],
        },
    ],
});

runRule("require-webkit-filter", require("../require-webkit-filter"), {
    valid: ['const style = { filter: "none", WebkitFilter: "none" };'],
    invalid: [
        {
            code: 'const style = { filter: "grayscale(1)", WebkitFilter: "none" };',
            errors: [{ messageId: "mismatchedValue" }],
        },
    ],
});

runRule("require-min-height-flex", require("../require-min-height-flex"), {
    valid: ['const style = { flexDirection: "column" as const, minHeight: "0" };'],
    invalid: [
        {
            code: 'const style = { flexDirection: "column" as const };',
            errors: [{ messageId: "requireMinHeight" }],
        },
    ],
});

runRule("no-raw-reason-id", require("../no-raw-reason-id"), {
    valid: [
        'const el = <div>{result.status === "failed" && result.reasonId && <span>{l.tDynamic(result.reasonId)}</span>}</div>;',
        "const el = <Widget errorKey={result.reasonId} />;",
    ],
    invalid: [
        {
            code: "const el = <div>{result.reasonId}</div>;",
            errors: [{ messageId: "rawRender" }],
        },
    ],
});

runRule("civic-no-eligibility-threshold-literal", require("../civic-no-eligibility-threshold-literal"), {
    valid: ["const locked = dto.canAct === false;", "const warning = wearPercent >= WARNING_WEAR_THRESHOLD;"],
    invalid: [
        {
            code: "const locked = manpower < 4;",
            errors: [{ messageId: "literalThreshold" }],
        },
    ],
});

runRule("no-pointer-events-lock", require("../no-pointer-events-lock"), {
    valid: [
        'const style = { pointerEvents: "auto" as const };',
        'const style = { /* allow-pointer-events-none: decoration only */ pointerEvents: "none" as const };',
    ],
    invalid: [
        {
            code: 'const style = { pointerEvents: "none" as const };',
            errors: [{ messageId: "pointerEventsLock" }],
        },
        {
            code: 'const style = { pointerEvents: disabled ? "none" : "auto" };',
            errors: [{ messageId: "pointerEventsLock" }],
        },
    ],
});
