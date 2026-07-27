/**
 * Font stacks for every theme and standalone modal style.
 *
 * Only fonts shipped in Content\Game\UI\Fonts resolve in GameFace; unknown families
 * fall back to an engine font that lacks U+2014 (em dash) and other glyphs.
 *
 * 'Noto Sans SC' carries the CJK glyphs: the Latin/Cyrillic faces have none, and
 * GameFace resolves missing glyphs by walking this list — the same way vanilla does
 * it (index.css declares --fontFamily as an explicit "Noto Sans SC","Noto Sans",…
 * chain per locale). Dropping it renders every Chinese string as tofu boxes.
 */
export const FONT_STACK_SANS = "'Noto Sans', 'Noto Sans SC', Overpass, sans-serif";

/**
 * The only shipped monospace (Perfect DOS VGA 437) is a pixel font that reads
 * poorly — the mono SLOT stays (swap point for a future custom font), but it
 * resolves to the clean shipped sans.
 */
export const FONT_STACK_MONO = "'Noto Sans', 'Noto Sans SC', Overpass, sans-serif";

/**
 * Newspaper look (Herald). The game ships no serif font, so the generic keeps the
 * engine's serif mapping for Latin — which has no CJK glyphs, hence the explicit
 * 'Noto Sans SC' behind it.
 */
export const FONT_STACK_SERIF = "serif, 'Noto Sans SC'";
