/**
 * Coherent UI compatibility components
 *
 * Abstractions over Coherent UI (old Chromium) limitations:
 * - gap: CSS gap doesn't work in flexbox → use Flex/Row/Column with gap prop
 * - whitespace: JSX whitespace swallowed → use Text, t``, Space, Spaced
 * - select: <select> crashes → use game's UI.Dropdown (not included here)
 */

export { Flex, Row, Column } from "./Flex";
export { Text, t, Space, Spaced } from "./Text";
