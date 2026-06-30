import { assertNever } from "../../../../../utils/exhaustive";

export type OperationType = "economic" | "military" | "infrastructure";

export interface Operation {
    id: string;
    name: string;
    type: OperationType;
    description: string;
    modifiers: string[];
    objective: string;
    reward: string;
    mayorsJoined: number;
    timeLeft: string;
}

export const MOCK_OPERATIONS: Operation[] = [
    {
        id: "arctic-snap",
        name: "ARCTIC SNAP",
        type: "economic",
        description: "Extreme cold wave hits the region. Heating demand surges.",
        modifiers: ["Heating demand +80%", "Coal imports frozen"],
        objective: "Survive 3 days on reserves + renewables",
        reward: "Green Energy efficiency +25%",
        mayorsJoined: 1247,
        timeLeft: "4d 6h",
    },
    {
        id: "swarm-defense",
        name: "SWARM DEFENSE",
        type: "military",
        description: "Intelligence reports massive UAV buildup. Expect increased activity.",
        modifiers: ["UAV spawn rate +200%", "AA ammo cost -15%"],
        objective: "Intercept 50 hostile drones",
        reward: "Patriot system -40% in ALLIES",
        mayorsJoined: 3891,
        timeLeft: "2d 18h",
    },
    {
        id: "outbreak-response",
        name: "OUTBREAK RESPONSE",
        type: "infrastructure",
        description: "Flu epidemic spreading rapidly. Hospitals at critical capacity.",
        modifiers: ["Hospital load +150%", "Citizen happiness -10%"],
        objective: "Build 3 hospitals OR healthcare budget 150%",
        reward: "Happiness +15% for 1 week",
        mayorsJoined: 892,
        timeLeft: "5d 0h",
    },
];

export const getTypeLabel = (type: OperationType): string => {
    switch (type) {
        case "economic": return "ECONOMIC";
        case "military": return "MILITARY";
        case "infrastructure": return "INFRASTRUCTURE";
        default: return assertNever(type, "getTypeLabel.type");
    }
};
