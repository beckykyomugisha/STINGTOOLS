import * as Location from 'expo-location';

export interface LocationResult {
  latitude: number;
  longitude: number;
  accuracy: number | null;
  timestamp: number;
}

export const locationService = {
  async requestPermission(): Promise<boolean> {
    const { status } = await Location.requestForegroundPermissionsAsync();
    return status === 'granted';
  },

  async getCurrent(): Promise<LocationResult | null> {
    const granted = await this.requestPermission();
    if (!granted) return null;
    try {
      const pos = await Location.getCurrentPositionAsync({
        accuracy: Location.Accuracy.Balanced,
      });
      return {
        latitude: pos.coords.latitude,
        longitude: pos.coords.longitude,
        accuracy: pos.coords.accuracy,
        timestamp: pos.timestamp,
      };
    } catch {
      return null;
    }
  },
};
