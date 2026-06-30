import { memo, useEffect } from "react";
import type React from "react";
import { notifyOpenGridTab, notifyOpenShadowTab } from "../../hooks/bindings/shockActBindings";

interface TutorialTabSignalsProps {
    activeDomain: string;
}

export const TutorialTabSignals: React.FC<TutorialTabSignalsProps> = memo(({ activeDomain }) => {
    useEffect(() => {
        if (activeDomain === "grid") notifyOpenGridTab();
        if (activeDomain === "shadow") notifyOpenShadowTab();
    }, [activeDomain]);

    return null;
});
TutorialTabSignals.displayName = "TutorialTabSignals";
