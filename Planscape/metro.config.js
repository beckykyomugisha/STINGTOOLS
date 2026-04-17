// MODEL-VIEWER — teach Metro that .html and .js-as-asset files in
// assets/viewer/ are bundled resources, not source modules.
//
// Without this:
//   - `require("../../assets/viewer/viewer.html")` returns a weird module ref
//   - three.min.js / GLTFLoader.js get tree-shaken as JS source

const { getDefaultConfig } = require("expo/metro-config");

const config = getDefaultConfig(__dirname);

// Treat HTML + GLTF + GLB as static assets the bundler copies verbatim.
// three.min.js and the loaders ship as sibling files under assets/viewer/
// and are referenced from viewer.html via relative script tags — they do
// NOT need to be in Metro's transformed JS source tree.
config.resolver.assetExts = [
  ...config.resolver.assetExts,
  "html",
  "gltf",
  "glb",
  "bin",
  "ifc",
];

module.exports = config;
