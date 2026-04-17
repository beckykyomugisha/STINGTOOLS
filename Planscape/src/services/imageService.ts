import * as ImagePicker from 'expo-image-picker';
import * as ImageManipulator from 'expo-image-manipulator';

export interface CapturedImage {
  uri: string;
  width: number;
  height: number;
  type: string;
  fileName?: string;
  fileSize?: number;
}

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
      exif: true,
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
    return {
      uri: result.uri,
      width: result.width,
      height: result.height,
      type: 'image/jpeg',
    };
  },
};
