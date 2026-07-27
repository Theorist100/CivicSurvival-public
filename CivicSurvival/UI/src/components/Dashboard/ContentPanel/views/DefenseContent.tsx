/**
 * DefenseContent - Air Defense Command + Manpower
 * HYBRID OPS domain → DEFENSE view
 * Layout: Two columns - Left (Manpower + Policy) | Right (AA Command + Build)
 * Note: Intelligence & Security moved to IntelContent
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents } from "@themes";
import { useRequestAction } from "@hooks/actions";
import { useDefenseActions } from "../../../../hooks/actions";
import { useDefenseData } from "../../../../hooks/domain";
import { AACommandColumn } from "./defense-content/AACommandColumn";
import { ManpowerPolicyColumn } from "./defense-content/ManpowerPolicyColumn";
import { createWarViewsStyles } from "../WarViews.styles";

type DefenseData = ReturnType<typeof useDefenseData>;
type DefenseActions = ReturnType<typeof useDefenseActions>;

const DefenseContentReady = memo(({ actions, data }: { actions: DefenseActions; data: DefenseData }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);
    const { aa, manpower } = data;

    // Two pending-tracked resupply actions: Patriot (its own button) and the gun group (one
    // button for all gun types). Both correlate against the single EmergencyResupplyRequest
    // result — only one resupply batch is in flight at a time, so the shared result drives
    // whichever button was pressed.
    // Rockets group: one button restocks every missile type off cooldown (Patriot + Hawk),
    // the rocket-kind mirror of the guns button.
    const patriotResupplyAction = useRequestAction(
        () => { actions.emergencyResupplyRockets(); return true; },
        aa.EmergencyResupplyRequest,
    );
    const gunsResupplyAction = useRequestAction(
        () => { actions.emergencyResupplyGuns(); return true; },
        aa.EmergencyResupplyRequest,
    );
    const callToArmsAction = useRequestAction(
        () => {
            actions.callToArms();
            return true;
        },
        manpower.CallToArmsRequest,
    );
    const conscriptionAction = useRequestAction(
        () => {
            actions.toggleConscription();
            return true;
        },
        manpower.ConscriptionToggleRequest,
    );
    const patriotDroneToggleAction = useRequestAction(
        () => {
            actions.togglePatriotDroneIntercept(!aa.PatriotInterceptsDrones);
            return true;
        },
        aa.PatriotDroneToggleRequest,
    );

    return (
        <div style={{
            display: "flex",
            height: "100%",
            overflow: "hidden" as const,
            position: "relative" as const,
        }}>
            <ManpowerPolicyColumn
                aa={aa}
                actions={actions}
                manpower={manpower}
                manpowerColor={data.manpowerColor}
                callToArms={data.callToArms}
                callToArmsAction={callToArmsAction}
                conscriptionAction={conscriptionAction}
                patriotDroneToggleAction={patriotDroneToggleAction}
                styles={s}
            />

            <AACommandColumn
                aa={aa}
                actions={actions}
                ammoRows={data.ammoRows}
                isAAActive={data.isAAActive}
                roster={data.roster}
                patriotResupply={data.patriotResupply}
                gunsResupply={data.gunsResupply}
                patriotResupplyAction={patriotResupplyAction}
                gunsResupplyAction={gunsResupplyAction}
                styles={s}
            />
        </div>
    );
});
DefenseContentReady.displayName = "DefenseContentReady";

export const DefenseContent = memo(() => {
    const data = useDefenseData();
    const actions = useDefenseActions();
    return <DefenseContentReady actions={actions} data={data} />;
});

DefenseContent.displayName = "DefenseContent";
