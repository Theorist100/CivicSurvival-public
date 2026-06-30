/**
 * GridOpsSection - Right column of PowerDashboard
 *
 * Architecture: Fixed Header + Scroll Body + Tabbed Footer
 * - HEAD: City status (always visible)
 * - BODY: District list (scrollable, takes remaining space)
 * - TABS: BACKUP | MARKET switcher
 * - FOOTER: Selected tool panel (fixed height)
 */

import React, { useMemo, useState, useCallback, useRef } from "react";
import { useTheme } from "../../../themes";
import { bindingDataOrDefault, usePowerGrid } from "@hooks/domain";
import { usePowerActions, useRequestAction } from "@hooks/actions";
import { useSafeJsonArray } from "@hooks/useSafeBinding";
import { districts$, isDistrictData } from "@hooks/bindings/coreBindings";
import { DEFAULT_POWER_GRID_DTO } from "../../../types/domainDtos";
import { districtIndexTarget, requestResultForTarget } from "@hooks/useRequest.generated";
import { transformDistrict } from "utils/transformers";
import { DistrictListView, DistrictPassport, CityPassport } from "../districts";
import { MarketSection } from "./MarketSection";
import { BackupReservesSection } from "./BackupReservesSection";
import { IconLightning, IconMoney } from "../../shared/common/Icons";
import { HelpSection } from "../../shared/common/HelpSection";
import { SectionHeader, SegmentedTabs } from "../../shared/ui";
import { useLocale } from "../../../locales";
import { asBuildingCategoryId, asEntityIndex, asScheduleId, type CityScheduleId, type EntityIndex } from "../../../types/semantic";
import { useBetaWave } from "../../../hooks/useBetaWave";

// ============================================================================
// FOOTER TABS
// ============================================================================

type FooterTab = "market" | "backup";

interface FooterTabsProps {
    activeTab: FooterTab;
    onTabChange: (tab: FooterTab) => void;
}

const FooterTabs: React.FC<FooterTabsProps> = ({ activeTab, onTabChange }) => {
    const theme = useTheme();
    const l = useLocale();

    const iconStyle: React.CSSProperties = {
        marginRight: "4rem",
        fontSize: "14rem",
    };

    return (
        <div style={{
            display: "flex",
            borderTop: `2rem solid ${theme.colors.border}`,
            borderBottom: `2rem solid ${theme.colors.border}`,
            backgroundColor: theme.colors.background,
        }}>
            <SegmentedTabs
                options={[
                    { value: "market", label: <><span style={iconStyle}><IconMoney /></span>{l.t("UI_GRID_FOOTER_MARKET")}</> },
                    { value: "backup", label: <><span style={iconStyle}><IconLightning /></span>{l.t("UI_GRID_FOOTER_BACKUP")}</> },
                ]}
                value={activeTab}
                onChange={onTabChange}
                color={theme.colors.accent}
                style={{ width: "100%" }}
            />
        </div>
    );
};

// ============================================================================
// MAIN COMPONENT
// ============================================================================

interface GridOpsSectionContentProps {
    cognitiveOpen: boolean;
}

const GridOpsSectionContent: React.FC<GridOpsSectionContentProps> = ({ cognitiveOpen }) => {
    const theme = useTheme();
    const l = useLocale();
    const cognitiveClosed = !cognitiveOpen;
    const powerActions = usePowerActions();
    const gridState = usePowerGrid();
    const rawDistricts = useSafeJsonArray(districts$, [], "districts");

    const districts = useMemo(
        () => Array.isArray(rawDistricts)
            ? rawDistricts.filter(isDistrictData).map(transformDistrict)
            : [],
        [rawDistricts],
    );
    const grid = bindingDataOrDefault(gridState, DEFAULT_POWER_GRID_DTO);
    const citySchedule = grid?.CitySchedule ?? 0;
    const effectiveCityMode = grid?.EffectiveCityMode ?? citySchedule;
    const cityScheduleLocked = grid?.CityScheduleAvailability.CanRun === false;
    const autoDispatchEnabled = grid?.AutoDispatchEnabled || false;
    const autoDispatchSheddedCount = grid?.AutoDispatchSheddedCount ?? 0;
    const autoDispatchBlockedByVip = grid?.AutoDispatchBlockedByVip || false;
    const internetLockedText = cognitiveClosed ? l.t("UI_TOGGLE_FEATURE_CLOSED") : "";
    // Master-Detail state: "list" | "city" | district entity number
    const [selectedDistrictEntity, setSelectedDistrictEntity] = useState<EntityIndex | null>(null);
    const [showCityView, setShowCityView] = useState(false);

    // Find selected district for Detail view
    const selectedDistrict = useMemo(() => {
        if (selectedDistrictEntity === null) return null;
        return districts.find((d) => d.entity === selectedDistrictEntity) || null;
    }, [districts, selectedDistrictEntity]);
    const selectedDistrictKey = selectedDistrict ? String(selectedDistrict.entity) : "";
    const districtActionRef = useRef<() => boolean>(() => false);
    const selectedDistrictActionRef = useRef<() => boolean>(() => false);
    const cityScheduleActionRef = useRef<() => boolean>(() => false);
    const internetActionRef = useRef<() => boolean>(() => false);
    const selectedInternetActionRef = useRef<() => boolean>(() => false);
    const selectedDistrictRequest = useMemo(
        () => selectedDistrictKey
            ? requestResultForTarget("DistrictToggleRequest", grid?.DistrictToggleRequest, districtIndexTarget(selectedDistrictKey))
            : undefined,
        [grid?.DistrictToggleRequest, selectedDistrictKey],
    );
    const selectedInternetRequest = useMemo(
        () => selectedDistrictKey
            ? requestResultForTarget("DistrictInternetToggleRequest", grid?.DistrictInternetToggleRequest, districtIndexTarget(selectedDistrictKey))
            : undefined,
        [grid?.DistrictInternetToggleRequest, selectedDistrictKey],
    );
    const districtAction = useRequestAction(() => districtActionRef.current(), grid?.DistrictToggleRequest);
    const selectedDistrictAction = useRequestAction(() => selectedDistrictActionRef.current(), selectedDistrictRequest);
    const cityScheduleAction = useRequestAction(() => cityScheduleActionRef.current(), grid?.CitySchedulePeriodRequest);
    const internetAction = useRequestAction(() => internetActionRef.current(), grid?.DistrictInternetToggleRequest);
    const selectedInternetAction = useRequestAction(() => selectedInternetActionRef.current(), selectedInternetRequest);
    const autoDispatchAction = useRequestAction(() => {
        powerActions.toggleAutoDispatch();
        return true;
    }, grid?.AutoDispatchToggleRequest);

    const runDistrictAction = useCallback((action: () => boolean) => {
        districtActionRef.current = action;
        return districtAction.execute();
    }, [districtAction]);
    const runSelectedDistrictAction = useCallback((action: () => boolean) => {
        selectedDistrictActionRef.current = action;
        return selectedDistrictAction.execute();
    }, [selectedDistrictAction]);
    const runCityScheduleAction = useCallback((action: () => boolean) => {
        cityScheduleActionRef.current = action;
        return cityScheduleAction.execute();
    }, [cityScheduleAction]);
    const runInternetAction = useCallback((action: () => boolean) => {
        internetActionRef.current = action;
        return internetAction.execute();
    }, [internetAction]);
    const runSelectedInternetAction = useCallback((action: () => boolean) => {
        selectedInternetActionRef.current = action;
        return selectedInternetAction.execute();
    }, [selectedInternetAction]);

    // Footer tab state (default: market)
    const [footerTab, setFooterTab] = useState<FooterTab>("market");

    // ========================================================================
    // HANDLERS
    // ========================================================================

    const handleCityScheduleChange = useCallback((scheduleId: CityScheduleId) => {
        if (scheduleId === -1) {
            runDistrictAction(() => {
                let emitted = false;
                districts.forEach((d) => {
                    if (!d.isVIP && !d.isFullBlackout) {
                        powerActions.toggleDistrictBlackout(asEntityIndex(d.entity));
                        emitted = true;
                    }
                });
                return emitted;
            });
        } else {
            if (cityScheduleLocked) return;
            runDistrictAction(() => {
                let emitted = false;
                districts.forEach((d) => {
                    if (d.isFullBlackout) {
                        powerActions.toggleDistrictBlackout(asEntityIndex(d.entity));
                        emitted = true;
                    }
                });
                return emitted;
            });
            runCityScheduleAction(() => {
                powerActions.setCitySchedule(asScheduleId(scheduleId));
                return true;
            });
        }
    }, [cityScheduleLocked, districts, powerActions, runCityScheduleAction, runDistrictAction]);

    // Mass operations for CityPassport
    const handleSetAllVIP = useCallback((enable: boolean) => {
        runDistrictAction(() => {
            let emitted = false;
            districts.forEach((d) => {
                if (enable !== d.isVIP) {
                    powerActions.toggleVIP(asEntityIndex(d.entity));
                    emitted = true;
                }
            });
            return emitted;
        });
    }, [districts, powerActions, runDistrictAction]);

    const handleSetAllWealthy = useCallback((enable: boolean) => {
        runDistrictAction(() => {
            let emitted = false;
            districts.forEach((d) => {
                if (enable !== d.isVIPBypass) {
                    powerActions.toggleVIPBypass(asEntityIndex(d.entity));
                    emitted = true;
                }
            });
            return emitted;
        });
    }, [districts, powerActions, runDistrictAction]);

    const handleSetAllInternet = useCallback((enable: boolean) => {
        if (cognitiveClosed) return;
        runInternetAction(() => {
            let emitted = false;
            districts.forEach((d) => {
                // internetDisabled is inverted: enable=true means we want internet ON (disabled=false)
                if (enable === d.internetDisabled) {
                    powerActions.toggleInternet(asEntityIndex(d.entity));
                    emitted = true;
                }
            });
            return emitted;
        });
    }, [cognitiveClosed, districts, powerActions, runInternetAction]);

    const handleSetAllMode = useCallback((mode: "on" | "off") => {
        runDistrictAction(() => {
            let emitted = false;
            if (mode === "off") {
                districts.forEach((d) => {
                    if (!d.isVIP && !d.isFullBlackout) {
                        powerActions.toggleDistrictBlackout(asEntityIndex(d.entity));
                        emitted = true;
                    }
                });
            } else {
                districts.forEach((d) => {
                    if (d.isFullBlackout) {
                        powerActions.toggleDistrictBlackout(asEntityIndex(d.entity));
                        emitted = true;
                    }
                });
            }
            return emitted;
        });
    }, [districts, powerActions, runDistrictAction]);

    // ========================================================================
    // RENDER
    // ========================================================================

    // No early return on gridState: grid already falls back to DEFAULT_POWER_GRID_DTO
    // via bindingDataOrDefault above. Hiding the whole section (incl. the independent
    // districts block) because one binding isn't "ready" is a worse bug than showing
    // grid defaults for a frame.

    // City stats for city row
    const totalMW = districts.reduce((sum, d) => sum + d.totalMW, 0);
    const deliveredMW = districts.reduce((sum, d) => sum + d.deliveredMW, 0);

    // City View (full screen passport)
    if (showCityView) {
        return (
            <CityPassport
                districts={districts}
                citySchedule={citySchedule}
                autoDispatchEnabled={autoDispatchEnabled}
                autoDispatchSheddedCount={autoDispatchSheddedCount}
                autoDispatchBlockedByVip={autoDispatchBlockedByVip}
                citySchedulePending={cityScheduleAction.isPending}
                internetLocked={cognitiveClosed}
                internetPending={internetAction.isPending}
                internetLockedText={internetLockedText}
                onBack={() => setShowCityView(false)}
                onSetCitySchedule={(scheduleId) => runCityScheduleAction(() => {
                    powerActions.setCitySchedule(asScheduleId(scheduleId));
                    return true;
                })}
                onToggleAllVIP={handleSetAllVIP}
                onToggleAllWealthy={handleSetAllWealthy}
                onToggleAllInternet={handleSetAllInternet}
                onSetAllMode={handleSetAllMode}
                onToggleAutoDispatch={autoDispatchAction.execute}
            />
        );
    }

    // District Detail View (full screen passport)
    if (selectedDistrict) {
        return (
            <DistrictPassport
                district={selectedDistrict}
                onBack={() => setSelectedDistrictEntity(null)}
                onToggleCategory={(catId) => runSelectedDistrictAction(() => {
                    powerActions.toggleDistrictCategory(asEntityIndex(selectedDistrict.entity), asBuildingCategoryId(catId));
                    return true;
                })}
                onToggleBlackout={() => runSelectedDistrictAction(() => {
                    powerActions.toggleDistrictBlackout(asEntityIndex(selectedDistrict.entity));
                    return true;
                })}
                onSetSchedule={(schedId) => runSelectedDistrictAction(() => {
                    powerActions.setDistrictSchedule(asEntityIndex(selectedDistrict.entity), asScheduleId(schedId));
                    return true;
                })}
                onToggleVIP={() => runSelectedDistrictAction(() => {
                    powerActions.toggleVIP(asEntityIndex(selectedDistrict.entity));
                    return true;
                })}
                onToggleVIPBypass={() => runSelectedDistrictAction(() => {
                    powerActions.toggleVIPBypass(asEntityIndex(selectedDistrict.entity));
                    return true;
                })}
                onToggleInternet={() => runSelectedInternetAction(() => {
                    if (cognitiveClosed) return false;
                    powerActions.toggleInternet(asEntityIndex(selectedDistrict.entity));
                    return true;
                })}
                internetLocked={cognitiveClosed}
                internetPending={selectedInternetAction.isPending}
                internetLockedText={internetLockedText}
            />
        );
    }

    // Master View - Fixed Header + Scroll Body + Tabbed Footer
    return (
        <div style={{
            display: "flex",
            flexDirection: "column",
            height: "100%",
            overflow: "hidden",
        }}>
            {/* District header with help */}
            <SectionHeader
                title={l.t("UI_GRID_DISTRICTS")}
                titleAs="span"
                titleStyle={{ fontSize: "11rem", fontWeight: 700, textTransform: "uppercase", color: theme.colors.textMuted, letterSpacing: "1rem" }}
                style={{ padding: "4rem 8rem 0" }}
                help={<HelpSection id="districts" title={l.t("UI_DISTRICTS")}>{l.t("HELP_DISTRICTS")}</HelpSection>}
            />
            {/* BODY: District list (scrollable, takes all remaining space) */}
            <div style={{
                flex: 1,
                minHeight: 0,
                overflowY: "auto",
                overflowX: "hidden",
            }}>
                <DistrictListView
                    districts={districts}
                    onSelectDistrict={(entity) => setSelectedDistrictEntity(entity)}
                    onToggleBlackout={(entity) => runDistrictAction(() => {
                        powerActions.toggleDistrictBlackout(asEntityIndex(entity));
                        return true;
                    })}
                    onSetMode={(entity, mode) => runDistrictAction(() => {
                        powerActions.setDistrictMode({ entity: asEntityIndex(entity), mode });
                        return true;
                    })}
                    cityTotalMW={totalMW}
                    cityDeliveredMW={deliveredMW}
                    citySchedule={effectiveCityMode}
                    cityScheduleLocked={false}
                    districtsOverrideCity={grid?.DistrictsOverrideCity ?? false}
                    onCityScheduleChange={handleCityScheduleChange}
                    onCityClick={() => setShowCityView(true)}
                />
            </div>

            {/* TABS: BACKUP | MARKET switcher */}
            <FooterTabs activeTab={footerTab} onTabChange={setFooterTab} />

            {/* FOOTER: Fixed height so switching tabs never changes layout */}
            <div style={{
                height: "280rem",
                flexShrink: 0,
                overflowY: "auto",
            }}>
                {footerTab === "market" ? <MarketSection /> : <BackupReservesSection />}
            </div>
        </div>
    );
};

export const GridOpsSection: React.FC = () => {
    const cognitiveWave = useBetaWave("Cognitive");
    return <GridOpsSectionContent cognitiveOpen={!cognitiveWave.isLocked} />;
};
