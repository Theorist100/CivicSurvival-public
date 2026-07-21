declare module '*.scss';
declare module '*.css';
// No '*.svg' declaration on purpose: svg glyphs are inline JSX (iconGlyphs.tsx),
// an svg import would mean a file-served icon came back (CIVIC-UI-054).
// Raster imports resolve to data URIs (webpack asset/inline) — typed as string.
declare module '*.png' { const dataUri: string; export default dataUri; }
declare module '*.jpg' { const dataUri: string; export default dataUri; }
declare module '*.gif' { const dataUri: string; export default dataUri; }