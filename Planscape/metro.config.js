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

// Web shims for native-only modules.
config.resolver.resolveRequest = (context, moduleName, platform) => {
  if (platform === 'web' && moduleName === 'react-native-pdf') {
    return {
      filePath: require.resolve('./src/shims/react-native-pdf.web.tsx'),
      type: 'sourceFile',
    };
  }
  if (platform === 'web' && moduleName === 'expo-secure-store') {
    return {
      filePath: require.resolve('./src/shims/expo-secure-store.web.ts'),
      type: 'sourceFile',
    };
  }
  if (platform === 'web' && moduleName === 'expo-notifications') {
    return {
      filePath: require.resolve('./src/shims/expo-notifications.web.ts'),
      type: 'sourceFile',
    };
  }
  if (platform === 'web' && moduleName === 'expo-device') {
    return {
      filePath: require.resolve('./src/shims/expo-device.web.ts'),
      type: 'sourceFile',
    };
  }
  if (platform === 'web' && moduleName === 'expo-application') {
    return {
      filePath: require.resolve('./src/shims/expo-application.web.ts'),
      type: 'sourceFile',
    };
  }
  return context.resolveRequest(context, moduleName, platform);
};

module.exports = config;
