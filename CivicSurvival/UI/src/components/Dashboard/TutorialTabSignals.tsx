import { memo, useEffect } from "react";
import type React from "react";
import { notifyOpenGridTab, notifyOpenShadowTab, notifyOpenWarTab } from "../../hooks/bindings/shockActBindings";

interface TutorialTabSignalsProps {
    activeDomain: string;
}

export const TutorialTabSignals: React.FC<TutorialTabSignalsProps> = memo(({ activeDomain }) => {
    useEffect(() => {
        if (activeDomain === "grid") notifyOpenGridTab();
        if (activeDomain === "shadow") notifyOpenShadowTab();
        if (activeDomain === "war") notifyOpenWarTab();
    }, [activeDomain]);

    return null;
});
TutorialTabSignals.displayName = "TutorialTabSignals";
