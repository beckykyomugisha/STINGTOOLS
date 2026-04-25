import * as ImagePicker from 'expo-image-picker';
import * as ImageManipulator from 'expo-image-manipulator';
import NetInfo from '@react-native-community/netinfo';

export interface CapturedImage {
  uri: string;
  width: number;
  height: number;
  type: string;
  fileName?: string;
  fileSize?: number;
}

/**
 * Phase 96 — MOB-privacy. `exif: false` on capture so OS camera does NOT embed
 * GPS/timestamp/device-serial metadata in the JPEG, and `compress()` re-encodes
 * the file through expo-image-manipulator (which also drops EXIF) before the
 * upload. Site photos on shared devices no longer leak personal location data
 * into the binary. GPS coordinates still reach the server via explicit
 * X-Latitude/X-Longitude headers from the issue create flow — that path is
 * tenant-scoped and audit-logged, so the server keeps authoritative geodata
 * while the raw JPEG stays clean.
 */

export interface WifiDecision {
  allow: boolean;
  reason: 'ok' | 'small' | 'wifi' | 'blocked_cellular' | 'unknown';
  sizeMB: number;
  netType: string | null;
}

/** 5 MB threshold — beyond this we nudge the user to wait for Wi-Fi. */
const LARGE_UPLOAD_THRESHOLD_BYTES = 5 * 1024 * 1024;

export const imageService = {
  async requestCameraPermission(): Promise<boolean> {
    const { status } = await ImagePicker.requestCameraPermissionsAsync();
    return status === 'granted';
  },

  async requestLibraryPermission(): Promise<boolean> {
    const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
    return status === 'granted';
  },

  async captureFromCamera(): Promise<CapturedImage | null> {
    const granted = await this.requestCameraPermission();
    if (!granted) return null;
    const result = await ImagePicker.launchCameraAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      quality: 0.9,
      // Phase 96 — strip EXIF on capture. GPS already reaches the server via
      // explicit header params; there's no reason to embed it in the JPEG too.
      exif: false,
    });
    if (result.canceled || !result.assets?.[0]) return null;
    const a = result.assets[0];
    return {
      uri: a.uri,
      width: a.width,
      height: a.height,
      type: a.mimeType ?? 'image/jpeg',
      fileName: a.fileName ?? undefined,
      fileSize: a.fileSize ?? undefined,
    };
  },

  async pickFromLibrary(): Promise<CapturedImage | null> {
    const granted = await this.requestLibraryPermission();
    if (!granted) return null;
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ImagePicker.MediaTypeOptions.Images,
      quality: 0.9,
      exif: false,
    });
    if (result.canceled || !result.assets?.[0]) return null;
    const a = result.assets[0];
    return {
      uri: a.uri,
      width: a.width,
      height: a.height,
      type: a.mimeType ?? 'image/jpeg',
      fileName: a.fileName ?? undefined,
      fileSize: a.fileSize ?? undefined,
    };
  },

  async compress(uri: string, maxWidth = 1920, quality = 0.7): Promise<CapturedImage> {
    const result = await ImageManipulator.manipulateAsync(
      uri,
      [{ resize: { width: maxWidth } }],
      { compress: quality, format: ImageManipulator.SaveFormat.JPEG },
    );
    // expo-image-manipulator's encoder does not copy EXIF from the source,
    // so a compressed image is guaranteed metadata-free.
    return {
      uri: result.uri,
      width: result.width,
      height: result.height,
      type: 'image/jpeg',
    };
  },

  /**
   * Phase 96 — pre-upload network check. Returns `allow: false` when the user
   * is on cellular AND the image is above the large-upload threshold so the
   * caller can queue the upload offline until Wi-Fi is back. We don't auto-
   * block — the decision is the caller's; this just surfaces the facts.
   */
  async classifyUpload(sizeBytes: number | undefined): Promise<WifiDecision> {
    const sizeMB = sizeBytes ? sizeBytes / (1024 * 1024) : 0;
    let netType: string | null = null;
    try {
      const net = await NetInfo.fetch();
      netType = net.type;
      if (!net.isConnected) return { allow: false, reason: 'unknown', sizeMB, netType };
      if (net.type === 'wifi' || net.type === 'ethernet') {
        return { allow: true, reason: 'wifi', sizeMB, netType };
      }
      if (!sizeBytes || sizeBytes < LARGE_UPLOAD_THRESHOLD_BYTES) {
        return { allow: true, reason: 'small', sizeMB, netType };
      }
      // Large file on cellular — let caller prompt user
      return { allow: false, reason: 'blocked_cellular', sizeMB, netType };
    } catch {
      // NetInfo failed — fail open rather than blocking legitimate uploads.
      return { allow: true, reason: 'unknown', sizeMB, netType };
    }
  },
};
