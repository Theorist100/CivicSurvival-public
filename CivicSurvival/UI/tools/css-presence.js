const { ReplaceSource } = require("webpack").sources;

exports.CSSPresencePlugin = class CSSPresencePlugin {
  apply(compiler) {
    compiler.hooks.compilation.tap("CSSPresencePlugin", (compilation) => {
      compilation.hooks.processAssets.tap(
        {
          name: "CSSPresencePlugin",
          stage: compilation.PROCESS_ASSETS_STAGE_ADDITIONS,
        },
        () => {
          const cssFiles = Object.keys(compilation.assets).filter((asset) =>
            asset.endsWith(".css")
          );
          const hasCSS = cssFiles.length > 0;

          // Inject the `hasCSS` export into the main module source
          for (const chunk of compilation.chunks) {
            for (const file of chunk.files) {
              if (file.endsWith(".mjs")) {
                const asset = compilation.getAsset(file);
                const source = String(asset.source.source());
                const exportMatches = [...source.matchAll(/\bexport\s*\{/g)];
                const exportMatch = exportMatches[exportMatches.length - 1];

                if (!exportMatch) {
                  continue;
                }

                const exportOffset = exportMatch.index;
                const replaceSource = new ReplaceSource(asset.source);
                replaceSource.replace(
                  exportOffset,
                  exportOffset + exportMatch[0].length - 1,
                  `const hasCSS = ${hasCSS}; export { hasCSS,`
                );

                compilation.updateAsset(file, replaceSource);
              }
            }
          }
        }
      );
    });
  }
};
