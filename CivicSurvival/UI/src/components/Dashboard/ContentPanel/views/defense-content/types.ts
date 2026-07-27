import type { createWarViewsStyles } from "../../WarViews.styles";

export type WarViewStyles = ReturnType<typeof createWarViewsStyles>;

export type AsyncAction = {
    execute: () => void;
    isPending: boolean;
};

export type CallToArmsValidator = {
    canRun: boolean;
    hasCasualties: boolean;
    reasonId: string;
};
