import React from "react";
import { formatMoney, useAccents } from "@themes";
import { useLocale, type TranslationKey } from "@locales";
import { ActionDropdown } from "@shared/ui";
import { KickbackToggle, RepairOption, RepairPopup } from "./RepairPopup";
import { REPAIR_TYPE } from "./helpers";

interface MunicipalRepair {
    canRun: boolean;
    lockedReasonId: string;
    cost: number;
}

interface ShadowRepair {
    canRun: boolean;
    lockedReasonId: string;
    cost: number;
}

export interface RepairDropdownLabels {
    repairBtn: string;
    municipal: string;
    municipalNote: string;
    shadowOps: string;
    shadowNote: string;
    kickbackLabel: (amount: string) => string;
    municipalDuration: string;
    shadowDuration: string;
    insufficientFundsFallback: TranslationKey;
    shadowInsufficientFallback: TranslationKey;
}

interface RepairDropdownProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    isKickbackEnabled: boolean;
    municipal: MunicipalRepair;
    municipalKickback: MunicipalRepair;
    shadow: ShadowRepair;
    kickbackAmount: number;
    onMunicipalRepair: () => void;
    onShadowRepair: () => void;
    onToggleKickback: () => void;
    isPending?: boolean;
    errorText?: string;
    labels: RepairDropdownLabels;
}

export const RepairDropdown: React.FC<RepairDropdownProps> = ({
    open,
    onOpenChange,
    isKickbackEnabled,
    municipal,
    municipalKickback,
    shadow,
    kickbackAmount,
    onMunicipalRepair,
    onShadowRepair,
    onToggleKickback,
    isPending = false,
    errorText = "",
    labels,
}) => {
    const accents = useAccents();
    const l = useLocale();

    const selectedMunicipal = isKickbackEnabled ? municipalKickback : municipal;
    const shadowLockedReason = shadow.lockedReasonId || labels.shadowInsufficientFallback;

    return (
        <ActionDropdown
            label={labels.repairBtn}
            open={open}
            onOpenChange={onOpenChange}
            color={accents.operations.accent}
            menuWidth="280rem"
            menuStyle={{ padding: 0 }}
        >
            <RepairPopup embedded={true}>
                <RepairOption
                    variant="municipal"
                    label={labels.municipal}
                    note={isPending
                        ? l.t("UI_PROCESSING")
                        : selectedMunicipal.canRun
                        ? labels.municipalNote
                        : l.tDynamic(selectedMunicipal.lockedReasonId || labels.insufficientFundsFallback)}
                    cost={selectedMunicipal.cost}
                    duration={labels.municipalDuration}
                    disabled={isPending || !selectedMunicipal.canRun}
                    onSelect={onMunicipalRepair}
                    addon={
                        <KickbackToggle
                            enabled={isKickbackEnabled}
                            label={labels.kickbackLabel(formatMoney(kickbackAmount))}
                            onToggle={onToggleKickback}
                        />
                    }
                />
                <RepairOption
                    separated={true}
                    variant="shadow"
                    label={labels.shadowOps}
                    note={isPending ? l.t("UI_PROCESSING") : shadow.canRun ? labels.shadowNote : l.tDynamic(shadowLockedReason)}
                    cost={shadow.cost}
                    duration={labels.shadowDuration}
                    disabled={isPending || !shadow.canRun}
                    onSelect={onShadowRepair}
                />
                {errorText && (
                    <div style={{
                        color: accents.crisis.accent,
                        fontSize: "11rem",
                        fontWeight: 700,
                        textAlign: "center",
                    }}>
                        {errorText}
                    </div>
                )}
            </RepairPopup>
        </ActionDropdown>
    );
};

export { REPAIR_TYPE };
