/**
 * Scenario Components Index
 *
 * Individual modals are no longer re-exported here: they are private to their
 * files (each exports only a `*Def` ModalDefinition) and reach the screen solely
 * through MODAL_REGISTRY / IntroOverlay. The overlay is the public surface.
 */

export { IntroOverlay } from "./IntroOverlay";
