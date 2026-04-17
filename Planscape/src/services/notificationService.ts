import * as Notifications from 'expo-notifications';
import * as Device from 'expo-device';
import * as Application from 'expo-application';
import { Platform } from 'react-native';
import { subscribePushToken } from '@/api/endpoints';
import { crashReporter } from './crashReporter';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowAlert: true,
    shouldPlaySound: true,
    shouldSetBadge: true,
  }),
});

async function deviceIdentifier(): Promise<string | null> {
  try {
    if (Platform.OS === 'android') return Application.getAndroidId();
    if (Platform.OS === 'ios') return await Application.getIosIdForVendorAsync();
  } catch (err) {
    crashReporter.warn('notificationService.deviceIdentifier failed', { err: String(err) });
  }
  return null;
}

export const notificationService = {
  async register(): Promise<string | null> {
    if (!Device.isDevice) return null;
    const { status: existing } = await Notifications.getPermissionsAsync();
    let finalStatus = existing;
    if (existing !== 'granted') {
      const { status } = await Notifications.requestPermissionsAsync();
      finalStatus = status;
    }
    if (finalStatus !== 'granted') return null;

    // PUSH-02: getExpoPushTokenAsync returns ExponentPushToken[…] (works in Expo
    // Go + EAS dev builds). For production standalone builds that want direct
    // FCM, swap to getDevicePushTokenAsync() below. The server's FirebasePushService
    // auto-detects the token shape and routes via Expo or native FCM accordingly.
    const tokenData = await Notifications.getExpoPushTokenAsync();
    const token = tokenData.data;

    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', {
        name: 'default',
        importance: Notifications.AndroidImportance.DEFAULT,
      });
    }

    // Map to the server's PushPlatform enum (FCM / APNs / Web). The server
    // inspects the token shape for routing, but the stored Platform column is
    // primarily for observability and must parse as one of the enum names.
    const serverPlatform = Platform.OS === 'ios' ? 'APNs'
      : Platform.OS === 'web' ? 'Web'
      : 'FCM';

    try {
      // NEW-MOB-18: include deviceId, appVersion, model so server can dedup and target.
      await subscribePushToken({
        token,
        platform: serverPlatform,
        deviceId: (await deviceIdentifier()) ?? undefined,
        appVersion: Application.nativeApplicationVersion ?? undefined,
        model: Device.modelName ?? undefined,
      });
    } catch (err) {
      // Server unreachable — token is still cached by expo, retry on next session
      crashReporter.warn('notificationService.subscribe failed', { err: String(err) });
    }
    return token;
  },
};
