// M2 — replaces app.json with a config-driven build so the production
// host comes from PLANSCAPE_HOST instead of being hard-coded.
//
// Set the host per environment via:
//   • EAS profile env (eas.json -> profile.env.PLANSCAPE_HOST)
//   • shell env when running `expo start` / `expo prebuild` locally
//
// The previous `app.json` shipped with `planscape.example` everywhere,
// which sent every universal link, every API call and every push hub
// connection to a domain that doesn't exist. Builds will fail closed if
// no host is set on a production profile so this can't be forgotten.

const PLACEHOLDER = 'planscape.example';
const host = (process.env.PLANSCAPE_HOST || '').trim() || PLACEHOLDER;
const apiBase = process.env.EXPO_PUBLIC_API_BASE || `https://${host}/api`;
const hubUrl  = process.env.EXPO_PUBLIC_HUB_URL  || `https://${host}/hubs/notifications`;

if (host === PLACEHOLDER && process.env.EAS_BUILD_PROFILE === 'production') {
  // Fail loudly during a production EAS build instead of silently
  // shipping the placeholder to TestFlight / Play.
  throw new Error(
    'PLANSCAPE_HOST is not set for the production profile — refusing to build with placeholder host.'
  );
}

module.exports = ({ config }) => ({
  ...config,
  expo: {
    name: 'Planscape',
    slug: 'planscape',
    version: '1.0.0',
    orientation: 'portrait',
    icon: './assets/icon.png',
    userInterfaceStyle: 'automatic',
    scheme: 'planscape',
    splash: {
      image: './assets/splash.png',
      resizeMode: 'contain',
      backgroundColor: '#1A237E',
    },
    assetBundlePatterns: ['**/*'],
    ios: {
      supportsTablet: true,
      bundleIdentifier: 'com.planscape.app',
      associatedDomains: [`applinks:${host}`],
      infoPlist: {
        NSCameraUsageDescription:
          'Planscape uses the camera to scan QR codes on BIM assets and capture site photos for issues.',
        NSPhotoLibraryUsageDescription:
          'Planscape attaches photos from your library to construction issues.',
        NSPhotoLibraryAddUsageDescription:
          'Planscape may save annotated site photos to your library.',
        NSLocationWhenInUseUsageDescription:
          'Planscape tags your construction issues with site location and validates project geofences.',
        NSMicrophoneUsageDescription:
          'Planscape may capture short voice notes attached to issues.',
      },
    },
    android: {
      adaptiveIcon: {
        foregroundImage: './assets/adaptive-icon.png',
        backgroundColor: '#1A237E',
      },
      package: 'com.planscape.app',
      permissions: [
        'CAMERA',
        'ACCESS_COARSE_LOCATION',
        'ACCESS_FINE_LOCATION',
        'READ_EXTERNAL_STORAGE',
        'WRITE_EXTERNAL_STORAGE',
        'RECORD_AUDIO',
      ],
      intentFilters: [
        {
          action: 'VIEW',
          autoVerify: true,
          data: [
            { scheme: 'https', host, pathPrefix: '/accept-invitation' },
            { scheme: 'https', host, pathPrefix: '/reset-password' },
            { scheme: 'https', host, pathPrefix: '/issues' },
            { scheme: 'https', host, pathPrefix: '/documents' },
          ],
          category: ['BROWSABLE', 'DEFAULT'],
        },
      ],
    },
    plugins: [
      'expo-router',
      'expo-secure-store',
      [
        'expo-camera',
        {
          cameraPermission:
            'Planscape uses the camera to scan QR codes on BIM assets and capture site photos.',
        },
      ],
      [
        'expo-image-picker',
        {
          photosPermission:
            'Planscape attaches photos from your library to construction issues.',
          cameraPermission:
            'Planscape captures site photos for construction issues.',
        },
      ],
      [
        'expo-location',
        {
          locationAlwaysAndWhenInUsePermission:
            'Planscape tags your construction issues with site location.',
          isIosBackgroundLocationEnabled: false,
          isAndroidBackgroundLocationEnabled: false,
        },
      ],
      [
        'expo-notifications',
        { icon: './assets/notification-icon.png', color: '#E8912D' },
      ],
    ],
    extra: {
      apiBase,
      hubUrl,
    },
  },
});
