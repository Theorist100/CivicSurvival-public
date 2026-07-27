/**
 * Content Views - Domain-specific content components
 * Split from ContentPanel.tsx to avoid god object
 */

// WAR domain views (HYBRID OPS)
export { WarContent } from "./WarContent";
export { DefenseContent } from "./DefenseContent";
export { IntelContent } from "./IntelContent";
export { CognitiveWarfareContent } from "./CognitiveWarfareContent";
export { IpsoContent } from "./IpsoContent";

// WAR ROOM domain views
export { WarRoomSummaryContent } from "./war-room/WarRoomSummaryContent";

// SHADOW domain views
export { ShadowSplitContent } from "./ShadowSplitContent";

// DONORS domain views
export { DonorsContent } from "./DonorsContent";

// GRID domain views
export { GridMainContent } from "./GridMainContent";
export { InfrastructureContent } from "./InfrastructureContent";

// NEWS domain views
export { NewsMainContent } from "./NewsMainContent";

// OPS domain views (Global Operations)
export { GlobalOperationsContent } from "./GlobalOperationsContent";
