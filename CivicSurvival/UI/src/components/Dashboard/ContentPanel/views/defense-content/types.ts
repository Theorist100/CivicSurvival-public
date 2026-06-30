import type { createWarViewsStyles } from "../../WarViews.styles";

export type WarViewStyles = ReturnType<typeof createWarViewsStyles>;

export type AsyncAction = {
    execute: () => void;
    isPending: boolean;
};

export type BoforsValidator = {
    canPlace: boolean;
    reasonId: string;
    cost: number;
};

export type GepardValidator = {
    canPlace: boolean;
    reasonId: string;
    cost: number;
};

export type PatriotValidator = {
    canPlace: boolean;
    reasonId: string;
    cost: number;
};

export type CallToArmsValidator = {
    canRun: boolean;
    hasCasualties: boolean;
    reasonId: string;
};
