/**
 * Flex - Flexbox container with gap support for Coherent UI
 *
 * Coherent UI (old Chromium) doesn't support CSS `gap` for flexbox.
 * This component emulates gap using margins on children.
 */

import React from "react";

interface FlexProps {
    /** Gap between children (e.g., "8rem", "12rem") */
    gap?: string | undefined;
    /** Flex direction */
    direction?: "row" | "column";
    /** Justify content */
    justify?: React.CSSProperties["justifyContent"] | undefined;
    /** Align items */
    align?: React.CSSProperties["alignItems"] | undefined;
    /** Flex wrap */
    wrap?: React.CSSProperties["flexWrap"] | undefined;
    /** Additional styles */
    style?: React.CSSProperties | undefined;
    /** CSS class */
    className?: string | undefined;
    children: React.ReactNode;
}

const flattenFragments = (children: React.ReactNode): React.ReactNode[] => {
    const result: React.ReactNode[] = [];
    React.Children.toArray(children).forEach((child) => {
        if (React.isValidElement(child) && child.type === React.Fragment) {
            result.push(...flattenFragments(child.props.children));
        } else {
            result.push(child);
        }
    });
    return result;
};

const getMarginFromShorthand = (
    style: React.CSSProperties,
    marginProp: "marginRight" | "marginBottom"
): React.CSSProperties["marginRight"] | React.CSSProperties["marginBottom"] | undefined => {
    const margin = style.margin;
    if (typeof margin !== "string" && typeof margin !== "number") return undefined;
    if (typeof margin === "number") return margin;

    const parts = margin.trim().split(/\s+/u);
    if (parts.length === 0) return undefined;
    if (marginProp === "marginRight") return parts[1] ?? parts[0];
    return parts[2] ?? parts[0];
};

const combineMargin = (
    existing: React.CSSProperties["marginRight"] | React.CSSProperties["marginBottom"] | undefined,
    gap: string
): string => {
    if (existing == null || existing === "") return gap;
    if (existing === "auto") return "auto";
    if (typeof existing === "number") return `calc(${existing}rem + ${gap})`;
    return `calc(${existing} + ${gap})`;
};

const withGapMargin = (
    style: React.CSSProperties | undefined,
    marginProp: "marginRight" | "marginBottom",
    gap: string
): React.CSSProperties => {
    const base = style ?? {};
    const existing = base[marginProp] ?? getMarginFromShorthand(base, marginProp);
    return {
        ...base,
        [marginProp]: combineMargin(existing, gap),
    };
};

export const Flex: React.FC<FlexProps> = ({
    gap,
    direction = "row",
    justify,
    align,
    wrap,
    style,
    className,
    children,
}) => {
    const childArray = flattenFragments(children);
    const isColumn = direction === "column";
    const marginProp = isColumn ? "marginBottom" : "marginRight";

    // Coherent rejects undefined CSS values — only include properties that are actually set
    const containerStyle: React.CSSProperties = {
        display: "flex",
        flexDirection: direction,
        ...(justify != null && { justifyContent: justify }),
        ...(align != null && { alignItems: align }),
        ...(wrap != null && { flexWrap: wrap }),
        ...style,
    };

    if (!gap) {
        return (
            <div style={containerStyle} className={className}>
                {children}
            </div>
        );
    }

    return (
        <div style={containerStyle} className={className}>
            {childArray.map((child, index) => {
                const isLast = index === childArray.length - 1;
                const needsMargin = !isLast;

                // Use child's key if available, otherwise fallback to index
                const childKey = React.isValidElement(child) && child.key != null
                    ? child.key
                    : `flex-child-${index}`;

                if (React.isValidElement<{ style?: React.CSSProperties }>(child)) {
                    return React.cloneElement(child, {
                        style: {
                            ...child.props.style,
                            ...(needsMargin ? withGapMargin(child.props.style, marginProp, gap) : {}),
                        },
                    });
                }

                return (
                    <div key={childKey} style={{
                        ...(needsMargin ? { [marginProp]: gap } : {}),
                    }}>
                        {child}
                    </div>
                );
            })}
        </div>
    );
};

/**
 * Row - Horizontal flex shortcut
 */
export const Row: React.FC<Omit<FlexProps, "direction">> = (props) => (
    <Flex {...props} direction="row" />
);

/**
 * Column - Vertical flex shortcut
 */
export const Column: React.FC<Omit<FlexProps, "direction">> = (props) => (
    <Flex {...props} direction="column" />
);
