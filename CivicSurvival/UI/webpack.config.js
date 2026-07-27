const fs = require("fs");
const path = require("path");
const webpack = require("webpack");
require("dotenv").config({ path: path.resolve(__dirname, ".env") });
const MOD = require("./mod.json");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const CopyPlugin = require("copy-webpack-plugin");
const { CSSPresencePlugin } = require("./tools/css-presence");
const TerserPlugin = require("terser-webpack-plugin");
const TsconfigPathsPlugin = require("tsconfig-paths-webpack-plugin");
const { sentryWebpackPlugin } = require("@sentry/webpack-plugin");
const gray = (text) => `\x1b[90m${text}\x1b[0m`;

const CSII_USERDATAPATH = process.env.CSII_USERDATAPATH;

if (!CSII_USERDATAPATH) {
  throw "CSII_USERDATAPATH environment variable is not set, ensure the CSII Modding Toolchain is installed correctly";
}

// Forward slashes only. OUTPUT_DIR feeds glob contexts (sentryWebpackPlugin
// sourcemaps.assets / filesToDeleteAfterUpload) where the glob engine treats
// "\" as an escape rather than a separator — a backslash path silently matches
// nothing ("Didn't find any matching sources for debug ID upload"). Node accepts
// "/" on Windows everywhere, and output.path runs through path.resolve() which
// re-normalizes to the platform separator, so every use site stays correct.
const OUTPUT_DIR = `${CSII_USERDATAPATH}\\Mods\\${MOD.id}`.replace(/\\/g, "/");

// Authoritative mod version comes from CivicSurvival.csproj <Version> (the same
// value that Paradox publishes and that the compiled DLL reports). mod.json's
// version is webpack-banner-only and may lag. Fall back to it if csproj read fails.
function readCsprojVersion() {
    try {
        const csprojPath = path.resolve(__dirname, "..", "CivicSurvival.csproj");
        const xml = fs.readFileSync(csprojPath, "utf8");
        const match = xml.match(/<Version>([^<]+)<\/Version>/);
        return match ? match[1].trim() : MOD.version;
    } catch {
        return MOD.version;
    }
}

const CIVIC_MOD_VERSION = readCsprojVersion();

// Source-map upload is opt-in via env vars. When unset (most local builds),
// the plugin is omitted entirely and webpack runs identically to before.
const SENTRY_AUTH_TOKEN = process.env.SENTRY_AUTH_TOKEN;
const SENTRY_ORG = process.env.SENTRY_ORG;
const SENTRY_PROJECT = process.env.SENTRY_PROJECT;

const banner = `
 * Cities: Skylines II UI Module
 *
 * Id: ${MOD.id}
 * Author: ${MOD.author}
 * Version: ${MOD.version}
 * Dependencies: ${MOD.dependencies.join(",")}
`;

module.exports = (_env, argv) => {
  const isDev = argv.mode === "development" || process.env.MODE === "dev";
  const enableSentryUpload = !isDev && SENTRY_AUTH_TOKEN && SENTRY_ORG && SENTRY_PROJECT;

  return {
  mode: isDev ? "development" : "production",
  // Production builds emit a hidden source map ONLY when uploading to Sentry —
  // the //# sourceMappingURL comment is stripped so subscribers never see a
  // reference. The .map file itself is uploaded by sentryWebpackPlugin and
  // must be deleted from the published artifact (see release-preflight).
  devtool: isDev ? "cheap-module-source-map" : (enableSentryUpload ? "hidden-source-map" : false),
  stats: "none",
  entry: {
    [MOD.id]: "./src/index.tsx",
  },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: {
          loader: "ts-loader",
          options: {
            // The repo tsconfig is noEmit-safe for CLI type checks. Webpack still needs JS output.
            compilerOptions: {
              noEmit: false,
            },
          },
        },
        exclude: /node_modules/,
      },
      {
        test: /\.s?css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:3]",
              },
            },
          },
          {
            loader: "sass-loader",
            options: {
              api: "modern",
            },
          },
        ],
      },
      {
        // Raster art ships INSIDE the bundle as data URIs — never as files. File
        // serving through the shared ui-mods host has two field-proven failure
        // modes: the host map dropping our key (v0.2.1) and a mid-session mod-dir
        // wipe on a Paradox update 404-ing every image while the already-loaded
        // bundle keeps running (bug report #37, 2026-07-14). svg is excluded on
        // purpose: glyphs are inline JSX (iconGlyphs.tsx, CIVIC-UI-054).
        test: /\.(png|jpe?g|gif)$/i,
        type: "asset/inline",
      },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    plugins: [new TsconfigPathsPlugin()],
    alias: {
      "mod.json": path.resolve(__dirname, "mod.json"),
    },
  },
  output: {
    path: path.resolve(__dirname, OUTPUT_DIR),
    clean: {
      // UI builds only own bundled JS/CSS. cs-icons/ stays in the owned set so
      // the stale file-served icon folder from pre-inline builds gets cleaned.
      keep: (asset) => {
        const normalizedAsset = asset.replace(/\\/g, "/");
        return !/^(.+\.mjs(\.map)?|.+\.css(\.map)?|cs-icons\/.+)$/.test(normalizedAsset);
      },
    },
    library: {
      type: "module",
    },
    publicPath: `coui://${MOD.id}/`,
  },
  optimization: {
    minimize: !isDev,
    minimizer: [
      new TerserPlugin({
        terserOptions: {
          sourceMap: true,
        },
        extractComments: {
          banner: () => banner,
        },
      }),
    ],
  },
  performance: {
    hints: false, // CS2 mod — web performance recommendations don't apply
  },
  experiments: {
    outputModule: true,
  },
  plugins: [
    new webpack.DefinePlugin({
      __CIVIC_DEVTOOLS__: JSON.stringify(isDev),
      __CIVIC_MOD_VERSION__: JSON.stringify(CIVIC_MOD_VERSION),
      "process.env.NODE_ENV": JSON.stringify(isDev ? "development" : "production"),
      // Sentry DSN injected from .env (public write-only ingest key). Empty when
      // unset → the runtime SDK stays disabled (see crashReporter.ts).
      "process.env.SENTRY_DSN": JSON.stringify(process.env.SENTRY_DSN ?? ""),
    }),
    ...(!isDev ? [new webpack.IgnorePlugin({ resourceRegExp: /^components\/devtools\/BalanceDebugPanel$/ })] : []),
    new MiniCssExtractPlugin(),
    new CSSPresencePlugin(),
    new CopyPlugin({
      patterns: [
        // No image assets are copied: svg glyphs are inline JSX (iconGlyphs.tsx),
        // raster thumbnails are inline data URIs (buildingThumbnails.ts) — the
        // bundle is the only delivery channel for all art.
        // mod.json carries the UI module metadata (id/author/version) that CS2
        // reads when registering the UIModuleAsset. Without it the module loads
        // with all-null metadata and never mounts (no UI). Nothing else deploys
        // it — the toolchain's DeployWIP wipes Mods and only re-copies $(OutDir),
        // where mod.json isn't present — so webpack must emit it next to the .mjs.
        {
          from: "mod.json",
          to: "mod.json",
        },
      ],
    }),
    {
      apply(compiler) {
        let runCount = 0;
        compiler.hooks.done.tap("AfterDonePlugin", (stats) => {
          console.log(stats.toString({ colors: true }));
          console.log(`\n🔨 ${!runCount++ ? "Built" : "Updated"} ${MOD.id}`);
          console.log("   " + gray(OUTPUT_DIR) + "\n");
        });
      },
    },
    ...(enableSentryUpload ? [
      sentryWebpackPlugin({
        org: SENTRY_ORG,
        project: SENTRY_PROJECT,
        authToken: SENTRY_AUTH_TOKEN,
        release: { name: CIVIC_MOD_VERSION },
        sourcemaps: {
          assets: `${OUTPUT_DIR}/**/*.{mjs,mjs.map,js,js.map}`,
          // Delete uploaded .map files from disk after upload so the published
          // artifact never ships debug symbols to Paradox subscribers.
          filesToDeleteAfterUpload: `${OUTPUT_DIR}/**/*.map`,
        },
        telemetry: false,
      }),
    ] : []),
  ],
};
};
