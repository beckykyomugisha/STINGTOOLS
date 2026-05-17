// Web stub — expo-notifications uses native push APIs unavailable in browsers.
export const AndroidImportance = { DEFAULT: 3, HIGH: 4, MAX: 5, MIN: 1, NONE: 0 };

export function setNotificationHandler(_handler: unknown): void {}

export async function getPermissionsAsync() {
  return { status: 'denied' as const, granted: false, canAskAgain: false, ios: undefined, android: undefined };
}

export async function requestPermissionsAsync() {
  return { status: 'denied' as const, granted: false, canAskAgain: false, ios: undefined, android: undefined };
}

export async function getExpoPushTokenAsync(_options?: unknown): Promise<{ data: string }> {
  return { data: '' };
}

export async function setNotificationChannelAsync(_id: string, _channel: unknown): Promise<null> {
  return null;
}

export function addNotificationReceivedListener(_listener: unknown): { remove(): void } {
  return { remove() {} };
}

export function addNotificationResponseReceivedListener(_listener: unknown): { remove(): void } {
  return { remove() {} };
}

export function removeNotificationSubscription(_sub: unknown): void {}

export async function getBadgeCountAsync(): Promise<number> { return 0; }
export async function setBadgeCountAsync(_count: number): Promise<boolean> { return false; }
export async function dismissAllNotificationsAsync(): Promise<void> {}
export async function getLastNotificationResponseAsync(): Promise<null> { return null; }
