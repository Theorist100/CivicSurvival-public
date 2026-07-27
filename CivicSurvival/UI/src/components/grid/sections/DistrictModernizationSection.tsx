import React, { memo, useMemo } from "react";
import { Column, Row } from "../../coherent";
import { useLocale } from "../../../locales";
import { formatCostArg, useAccents, useTheme } from "../../../themes";
import { usePowerActions, useRequestAction } from "@hooks/actions";
import { bindingDataOrDefault, useBackupPower, useShadowPrograms } from "@hooks/domain";
import { districtIndexTarget, requestResultForTarget } from "@hooks/useRequest.generated";
import { type ContractorType } from "../../../types/semantic";
import { type RequestResult } from "../../../types/dtoSubTypes";
import { type ShadowProgramEntry } from "../../../types/domainDtos.generated";
import { DEFAULT_BACKUP_POWER_DTO } from "../../../types/domainDtos";
import { createStyles } from "./SectionStyles";
import { HoverTipTarget } from "../../shared/common/HoverTip";

const HONEST_CONTRACTOR = 0 as ContractorType;
const CORRUPT_CONTRACTOR = 1 as ContractorType;

interface ModernizationRowProps {
    program: ShadowProgramEntry;
    request: RequestResult;
}

const DistrictModernizationRow: React.FC<ModernizationRowProps> = memo(({ program, request }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const powerActions = usePowerActions();
    const rowRequest = useMemo(
        () => requestResultForTarget("ModernizationRequest", request, districtIndexTarget(program.DistrictIndex)),
        [program.DistrictIndex, request],
    );

    const honestAction = useRequestAction(
        () => {
            powerActions.launchDistrictModernization(program.DistrictIndex, HONEST_CONTRACTOR);
            return true;
        },
        rowRequest
    );
    const corruptAction = useRequestAction(
        () => {
            powerActions.launchDistrictModernization(program.DistrictIndex, CORRUPT_CONTRACTOR);
            return true;
        },
        rowRequest
    );

    const requestPending = rowRequest?.Status === "pending" || honestAction.isPending || corruptAction.isPending;
    const errorText = rowRequest?.Status === "failed" && rowRequest.ReasonId ? l.tDynamic(rowRequest.ReasonId) : "";
    const honestDisabled = requestPending || !program.CanModernizeHonest;
    const corruptDisabled = requestPending || !program.CanModernizeCorrupt;

    const buttonStyle = (enabled: boolean, color: string): React.CSSProperties => ({
        flex: 1,
        minHeight: "32rem",
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        border: `2rem solid ${enabled ? color : theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        background: enabled ? color : theme.colors.paper,
        color: enabled ? theme.colors.white : theme.colors.textMuted,
        cursor: enabled ? "pointer" : "not-allowed",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        textAlign: "center",
    });

    return (
        <div style={{
            padding: theme.spacing.sm,
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadius,
            background: theme.colors.paper,
        }}>
            <Row justify="space-between" align="center">
                <span style={{ color: theme.colors.textPrimary, fontWeight: 700, fontSize: theme.typography.sizeSM }}>
                    {program.DistrictName}
                </span>
                <span style={{ color: theme.colors.textMuted, fontFamily: theme.typography.fontFamilyMono, fontSize: theme.typography.sizeSM }}>
                    {formatCostArg(program.EstimatedCost)}
                </span>
            </Row>

            <Row gap={theme.spacing.xs} style={{ marginTop: theme.spacing.xs }}>
                <HoverTipTarget text={!program.CanModernizeHonest && program.ModernizeHonestLockedReasonId ? l.tDynamic(program.ModernizeHonestLockedReasonId) : l.t("BACKUP_CONTRACTOR_HONEST_TIP")}>
                    <button
                        style={buttonStyle(!honestDisabled, accents.resilience.accent)}
                        disabled={honestDisabled}
                        onClick={!honestDisabled ? honestAction.execute : undefined}
                    >
                        {requestPending ? l.t("UI_PROCESSING") : l.t("BACKUP_CONTRACTOR_HONEST")}
                    </button>
                </HoverTipTarget>
                <HoverTipTarget text={!program.CanModernizeCorrupt && program.ModernizeCorruptLockedReasonId ? l.tDynamic(program.ModernizeCorruptLockedReasonId) : l.t("BACKUP_CONTRACTOR_CORRUPT_TIP")}>
                    <button
                        style={buttonStyle(!corruptDisabled, accents.schemes.accent)}
                        disabled={corruptDisabled}
                        onClick={!corruptDisabled ? corruptAction.execute : undefined}
                    >
                        {requestPending ? l.t("UI_PROCESSING") : l.t("BACKUP_CONTRACTOR_CORRUPT")}
                    </button>
                </HoverTipTarget>
            </Row>

            <Row justify="space-between" align="center" style={{ marginTop: theme.spacing.xs }}>
                <span style={{ color: theme.colors.textMuted, fontSize: theme.typography.sizeXS }}>
                    {l.t("BACKUP_KICKBACK")}: {formatCostArg(program.KickbackEarned)}
                </span>
                <span style={{ color: program.FireCount > 0 ? accents.crisis.accent : theme.colors.textMuted, fontSize: theme.typography.sizeXS }}>
                    {l.t("BACKUP_FIRES")}: {program.FireCount}
                </span>
            </Row>

            {errorText && (
                <div style={{
                    marginTop: theme.spacing.xs,
                    color: accents.crisis.accent,
                    fontSize: theme.typography.sizeXS,
                    fontWeight: 700,
                }}>
                    {errorText}
                </div>
            )}
        </div>
    );
});

DistrictModernizationRow.displayName = "DistrictModernizationRow";

export const DistrictModernizationSection: React.FC = () => {
    const theme = useTheme();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const backup = bindingDataOrDefault(useBackupPower(), DEFAULT_BACKUP_POWER_DTO);
    const programs = useShadowPrograms();

    if (programs.length === 0) return null;

    return (
        <Column gap={theme.spacing.xs} style={{ ...s.backup.container, marginTop: theme.spacing.sm }}>
            <div style={s.backup.title}>{l.t("BACKUP_CHOOSE_CONTRACTOR")}</div>
            {programs.map((program) => (
                <DistrictModernizationRow
                    key={program.DistrictIndex}
                    program={program}
                    request={backup.ModernizationRequest}
                />
            ))}
        </Column>
    );
};
