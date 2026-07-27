/**
 * Text utilities for Coherent UI whitespace handling
 *
 * Coherent UI swallows whitespace between JSX expressions and text.
 * Example: <span>{count} buildings</span> renders as "123buildings"
 *
 * These utilities ensure proper spacing.
 */

import React from "react";

interface TextProps {
    /** Text content - spaces preserved (null/undefined/boolean coerced via String()) */
    children: string | number | boolean | null | undefined;
    /** HTML tag to use */
    as?: "span" | "div" | "p" | "label";
    /** Additional styles */
    style?: React.CSSProperties;
    /** CSS class */
    className?: string;
}

/**
 * Text component - renders text with preserved whitespace
 *
 * Usage:
 * <Text>{`${count} buildings`}</Text>
 * <Text as="div">{`Power: ${power} MW`}</Text>
 */
export const Text: React.FC<TextProps> = ({
    children,
    as: Tag = "span",
    style,
    className,
}) => (
    <Tag style={style} className={className}>
        {String(children)}
    </Tag>
);

/**
 * Template literal helper for inline text with variables
 *
 * Usage:
 * <span>{t`${count} buildings affected`}</span>
 * <div>{t`Power balance: ${balance} MW`}</div>
 */
export const t = (
    strings: TemplateStringsArray,
    ...values: (string | number | undefined | null)[]
): string => {
    return strings.reduce((result, str, i) => {
        const value = values[i];
        const valueStr = value !== undefined && value !== null ? String(value) : "";
        return result + str + valueStr;
    }, "");
};

/**
 * Space component - explicit whitespace
 *
 * Usage:
 * <span>{count}<Space/>buildings</span>
 */
export const Space: React.FC = () => <>{" "}</>;

/**
 * Spaced - wraps content with spaces around variables
 *
 * Usage:
 * <Spaced before>{count}</Spaced> buildings
 * <Spaced after>Power:</Spaced>{value}
 * <Spaced both>{value}</Spaced>
 */
interface SpacedProps {
    children: React.ReactNode;
    /** Add space before content */
    before?: boolean;
    /** Add space after content */
    after?: boolean;
    /** Add space both before and after */
    both?: boolean;
}

export const Spaced: React.FC<SpacedProps> = ({
    children,
    before,
    after,
    both,
}) => (
    <>
        {(before || both) && "\u00A0"}
        {children}
        {(after || both) && "\u00A0"}
    </>
);
