/**
 * Sparkline and PowerChart components for the History tab.
 */

import React, { useMemo } from "react";
import { useTheme } from "../../themes";
import type { HistoryPoint, PowerHistoryData } from "../../hooks/state/useDebugState";
import type { createStyles } from "./debugPanelShared";

const CHART_WIDTH = 240;
const CHART_PADDING = 4;

// ============================================================================
// SPARKLINE
// ============================================================================

interface SparklineProps {
    data: HistoryPoint[];
    color: string;
    label: string;
    height?: number;
    showValue?: boolean;
    styles: ReturnType<typeof createStyles>;
}

export const Sparkline: React.FC<SparklineProps> = ({
    data,
    color,
    label,
    height = 40,
    showValue = true,
    styles,
}) => {
    const theme = useTheme();

    const { path, minVal, maxVal, currentVal } = useMemo(() => {
        if (!data || data.length === 0) {
            return { path: "", minVal: 0, maxVal: 100, currentVal: 0 };
        }

        const values = data.map((p) => p.v);
        const min = Math.min(...values);
        const max = Math.max(...values);
        const range = max - min || 1;

        const points = data.map((p, i) => {
            const x = CHART_PADDING + (i / (data.length - 1 || 1)) * (CHART_WIDTH - 2 * CHART_PADDING);
            const y = height - CHART_PADDING - ((p.v - min) / range) * (height - 2 * CHART_PADDING);
            return `${x},${y}`;
        });

        return {
            path: `M${points.join(" L")}`,
            minVal: min,
            maxVal: max,
            currentVal: values[values.length - 1] ?? 0,
        };
    }, [data, height]);

    return (
        <div style={styles.sparklineContainer}>
            <div style={styles.sparklineHeader}>
                <span style={styles.sparklineSecondaryText}>{label}</span>
                {showValue && (
                    <span style={styles.sparklineBoldValue(color)}>
                        {currentVal.toFixed(1)}
                    </span>
                )}
            </div>
            <svg
                width={CHART_WIDTH}
                height={height}
                style={styles.svgContainer}
            >
                {data.length > 1 ? (
                    <path
                        d={path}
                        fill="none"
                        stroke={color}
                        strokeWidth="2"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                    />
                ) : (
                    <text
                        x={CHART_WIDTH / 2}
                        y={height / 2}
                        textAnchor="middle"
                        fill={theme.colors.textMuted}
                        fontSize="10"
                    >
                        Waiting for data...
                    </text>
                )}
            </svg>
            {data.length > 1 && (
                <div style={styles.sparklineFooter}>
                    <span>{minVal.toFixed(0)}</span>
                    <span>{maxVal.toFixed(0)}</span>
                </div>
            )}
        </div>
    );
};

// ============================================================================
// POWER CHART (dual line)
// ============================================================================

interface PowerChartProps {
    data: PowerHistoryData;
    height?: number;
    styles: ReturnType<typeof createStyles>;
}

export const PowerChart: React.FC<PowerChartProps> = ({ data, height = 50, styles }) => {
    const theme = useTheme();

    const { prodPath, consPath } = useMemo(() => {
        const prod = data.production || [];
        const cons = data.consumption || [];

        if (prod.length === 0 && cons.length === 0) {
            return { prodPath: "", consPath: "" };
        }

        const allValues = [...prod.map((p) => p.v), ...cons.map((p) => p.v)];
        const min = Math.min(...allValues);
        const max = Math.max(...allValues);
        const range = max - min || 1;

        const makePath = (points: HistoryPoint[]) => {
            if (points.length === 0) return "";
            const coords = points.map((p, i) => {
                const x = CHART_PADDING + (i / (points.length - 1 || 1)) * (CHART_WIDTH - 2 * CHART_PADDING);
                const y = height - CHART_PADDING - ((p.v - min) / range) * (height - 2 * CHART_PADDING);
                return `${x},${y}`;
            });
            return `M${coords.join(" L")}`;
        };

        return {
            prodPath: makePath(prod),
            consPath: makePath(cons),
        };
    }, [data, height]);

    const prodCurrent = data.production?.[data.production.length - 1]?.v ?? 0;
    const consCurrent = data.consumption?.[data.consumption.length - 1]?.v ?? 0;

    return (
        <div style={styles.sparklineContainer}>
            <div style={styles.sparklineHeader}>
                <span style={styles.sparklineSecondaryText}>Power (24h)</span>
                <span>
                    <span style={styles.successText}>{prodCurrent.toFixed(0)}</span>
                    <span style={styles.mutedText}>{" / "}</span>
                    <span style={styles.crisisText}>{consCurrent.toFixed(0)}</span>
                </span>
            </div>
            <svg
                width={CHART_WIDTH}
                height={height}
                style={styles.svgContainer}
            >
                {data.production?.length > 1 || data.consumption?.length > 1 ? (
                    <>
                        {prodPath && (
                            <path
                                d={prodPath}
                                fill="none"
                                stroke={theme.colors.success}
                                strokeWidth="2"
                                strokeLinecap="round"
                            />
                        )}
                        {consPath && (
                            <path
                                d={consPath}
                                fill="none"
                                stroke={theme.colors.error}
                                strokeWidth="2"
                                strokeLinecap="round"
                            />
                        )}
                    </>
                ) : (
                    <text
                        x={CHART_WIDTH / 2}
                        y={height / 2}
                        textAnchor="middle"
                        fill={theme.colors.textMuted}
                        fontSize="10"
                    >
                        Waiting for data...
                    </text>
                )}
            </svg>
            <div style={styles.chartLegend}>
                <span style={styles.successText}>Production</span>
                <span style={styles.crisisText}>Consumption</span>
            </div>
        </div>
    );
};
