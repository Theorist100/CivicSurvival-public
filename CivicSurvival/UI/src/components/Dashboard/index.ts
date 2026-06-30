/**
 * Dashboard module exports
 * Main UI container with DomainTabs + ContentPanel architecture
 */

// Main component
export { Dashboard } from "./Dashboard";
export { createDashboardStyles } from "./Dashboard.styles";

// Sub-components
export { GlobalStatus, createGlobalStatusStyles } from "./GlobalStatus";
export { DomainTabs, DOMAINS, createDomainTabsStyles } from "./DomainTabs";
export type { DomainId, DomainConfig } from "./DomainTabs";
export { ContentPanel, createContentPanelStyles, GRID_VIEWS, WAR_VIEWS, SHADOW_VIEWS } from "./ContentPanel";
export type { ViewId, ViewConfig } from "./ContentPanel";
