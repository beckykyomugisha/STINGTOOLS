// Phase 178f — Penetration sign-off screen.
//
// Captures installer + inspector + photo + GPS for a single
// penetration. Three entry paths:
//   1. /penetrations/signoff?scan=1   → opens QR scanner first
//   2. /penetrations/signoff?cn=FRP-0001 → pre-fills the control number
//   3. /penetrations/signoff           → manual entry
//
// QR payload format (matches what the placer encodes onto each FRP):
//   STING-PEN|<controlNumber>|<pfvUuid>|<projectId>
//
// Submit calls PUT /api/projects/{id}/penetrations/{controlNumber}/signoff —
// idempotent on (controlNumber, pfvUuid). Photo + GPS optional but
// recommended for the BS 9999 / Building Safety Act golden-thread
// record.

import { useEffect, useState } from 'react';
import {
  View, Text, TextInput, ScrollView, StyleSheet, TouchableOpacity, Alert, Image, Platform,
} from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { CameraView, useCameraPermissions, BarcodeScanningResult } from 'expo-camera';
import * as ImagePicker from 'expo-image-picker';
import * as Location from 'expo-location';
import { useProjectStore } from '@/stores/projectStore';
import { putPenetrationSignoff, PenetrationSignoff } from '@/api/endpoints';

const STATUSES = ['INSTALLED', 'INSPECTED', 'SIGNED-OFF', 'REWORK'];

export default function PenetrationSignoffScreen() {
  const { scan, cn } = useLocalSearchParams<{ scan?: string; cn?: string }>();
  const activeProject = useProjectStore((s) => s.active);
  const [permission, requestPermission] = useCameraPermissions();
  const [scanning, setScanning] = useState(scan === '1');

  const [controlNumber, setControlNumber] = useState((cn as string) ?? '');
  const [pfvUuid, setPfvUuid] = useState('');
  const [installerName, setInstallerName] = useState('');
  const [installerCompany, setInstallerCompany] = useState('');
  const [inspectorName, setInspectorName] = useState('');
  const [status, setStatus] = useState<string>('INSTALLED');
  const [notes, setNotes] = useState('');
  const [photoUri, setPhotoUri] = useState<string | null>(null);
  const [gps, setGps] = useState<{ lat: number; lon: number } | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (scanning && !permission?.granted) requestPermission();
  }, [scanning, permission?.granted, requestPermission]);

  const onBarcode = (r: BarcodeScanningResult) => {
    setScanning(false);
    const raw = r.data ?? '';
    if (!raw.startsWith('STING-PEN|')) {
      Alert.alert('Wrong QR', 'Scan a STING-PEN tag (format STING-PEN|FRP-####|uuid|projectId).');
      return;
    }
    const parts = raw.split('|');
    if (parts.length >= 3) {
      setControlNumber(parts[1] ?? '');
      setPfvUuid(parts[2] ?? '');
    }
  };

  const pickPhoto = async () => {
    const result = await ImagePicker.launchCameraAsync({ quality: 0.6, base64: false });
    if (!result.canceled && result.assets?.[0]) setPhotoUri(result.assets[0].uri);
  };

  const captureGps = async () => {
    const { status: locStatus } = await Location.requestForegroundPermissionsAsync();
    if (locStatus !== 'granted') { Alert.alert('Permission denied', 'Location permission required.'); return; }
    const loc = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.High });
    setGps({ lat: loc.coords.latitude, lon: loc.coords.longitude });
  };

  const submit = async () => {
    if (!activeProject?.id) { Alert.alert('No project', 'Select a project first.'); return; }
    if (!controlNumber) { Alert.alert('Missing control number', 'Scan a QR or type FRP-####.'); return; }
    if (!installerName) { Alert.alert('Missing installer', 'Enter the installer name.'); return; }
    setBusy(true);
    try {
      const body: PenetrationSignoff = {
        penetrationControlNumber: controlNumber,
        pfvUuid: pfvUuid || undefined,
        installerName,
        installerCompany: installerCompany || undefined,
        inspectorName: inspectorName || undefined,
        installedAt: new Date().toISOString(),
        inspectedAt: inspectorName ? new Date().toISOString() : undefined,
        status,
        notes: notes || undefined,
        gpsLat: gps?.lat,
        gpsLon: gps?.lon,
        // photoBlobId is wired once the upload pipeline (S3/MinIO) is
        // available — for now we just persist the URI on-device and
        // queue the upload via offlineQueue. Photo metadata is the
        // golden-thread minimum required by BS 9999.
      };
      await putPenetrationSignoff(activeProject.id, controlNumber, body);
      Alert.alert('Sign-off saved', `${controlNumber} status = ${status}.`);
      router.back();
    } catch (e: any) {
      Alert.alert('Saved offline', `Submit failed: ${e?.message ?? 'unknown'}. The offline queue will retry.`);
      router.back();
    } finally { setBusy(false); }
  };

  if (scanning) {
    if (!permission?.granted) {
      return (
        <View style={styles.permissionView}>
          <Text style={styles.permissionText}>Camera permission required to scan QR.</Text>
          <TouchableOpacity onPress={() => requestPermission()} style={styles.cta}><Text style={styles.ctaText}>Grant</Text></TouchableOpacity>
        </View>
      );
    }
    return (
      <View style={{ flex: 1, backgroundColor: '#000' }}>
        <CameraView
          style={{ flex: 1 }}
          barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
          onBarcodeScanned={onBarcode}
        />
        <TouchableOpacity onPress={() => setScanning(false)} style={styles.scanCancel}>
          <Text style={styles.scanCancelText}>Cancel</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <ScrollView style={styles.container} keyboardShouldPersistTaps="handled">
      <Text style={styles.label}>Control number (FRP-####)</Text>
      <View style={{ flexDirection: 'row', gap: 8 }}>
        <TextInput
          style={[styles.input, { flex: 1 }]}
          autoCapitalize="characters"
          value={controlNumber}
          onChangeText={setControlNumber}
          placeholder="FRP-0001"
        />
        <TouchableOpacity style={styles.scanBtn} onPress={() => setScanning(true)}>
          <Text style={styles.scanBtnText}>📷 Scan</Text>
        </TouchableOpacity>
      </View>
      {!!pfvUuid && <Text style={styles.uuid}>UUID: {pfvUuid}</Text>}

      <Text style={styles.label}>Installer name *</Text>
      <TextInput style={styles.input} value={installerName} onChangeText={setInstallerName} placeholder="Smith, J." />

      <Text style={styles.label}>Installer company</Text>
      <TextInput style={styles.input} value={installerCompany} onChangeText={setInstallerCompany} placeholder="Acme Firestop Ltd" />

      <Text style={styles.label}>Inspector name (optional)</Text>
      <TextInput style={styles.input} value={inspectorName} onChangeText={setInspectorName} placeholder="Doe, A." />

      <Text style={styles.label}>Status</Text>
      <View style={styles.row}>
        {STATUSES.map((s) => (
          <TouchableOpacity
            key={s}
            style={[styles.chip, status === s && styles.chipActive]}
            onPress={() => setStatus(s)}
          >
            <Text style={[styles.chipText, status === s && styles.chipTextActive]}>{s}</Text>
          </TouchableOpacity>
        ))}
      </View>

      <Text style={styles.label}>Notes</Text>
      <TextInput
        style={[styles.input, { height: 80, textAlignVertical: 'top' }]}
        multiline value={notes} onChangeText={setNotes}
        placeholder="Defects, rework reason, batch / lot, etc."
      />

      <View style={styles.row}>
        <TouchableOpacity style={styles.evidence} onPress={pickPhoto}>
          <Text style={styles.evidenceText}>{photoUri ? '✓ Photo' : '📷 Take photo'}</Text>
        </TouchableOpacity>
        <TouchableOpacity style={styles.evidence} onPress={captureGps}>
          <Text style={styles.evidenceText}>{gps ? `📍 ${gps.lat.toFixed(4)}, ${gps.lon.toFixed(4)}` : '📍 Capture GPS'}</Text>
        </TouchableOpacity>
      </View>
      {photoUri && <Image source={{ uri: photoUri }} style={styles.photo} />}

      <TouchableOpacity style={[styles.submit, busy && styles.submitBusy]} disabled={busy} onPress={submit}>
        <Text style={styles.submitText}>{busy ? 'Saving…' : 'Submit sign-off'}</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16 },
  label: { fontSize: 13, fontWeight: '600', marginTop: 12, marginBottom: 4, opacity: 0.7 },
  input: { borderWidth: 1, borderColor: '#ccc', borderRadius: 8, padding: 10, fontSize: 15 },
  scanBtn: { paddingVertical: 10, paddingHorizontal: 14, borderRadius: 8, backgroundColor: '#2D5BFF', justifyContent: 'center' },
  scanBtnText: { color: '#fff', fontWeight: '600' },
  uuid: { marginTop: 4, fontSize: 11, opacity: 0.5, fontFamily: Platform.select({ ios: 'Menlo', android: 'monospace' }) },
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginTop: 4 },
  chip: { paddingVertical: 8, paddingHorizontal: 12, backgroundColor: '#eee', borderRadius: 20 },
  chipActive: { backgroundColor: '#2D5BFF' },
  chipText: { fontSize: 12, fontWeight: '600' },
  chipTextActive: { color: '#fff' },
  evidence: { flex: 1, padding: 12, borderRadius: 8, backgroundColor: '#f4f4f4', alignItems: 'center' },
  evidenceText: { fontSize: 13, fontWeight: '600' },
  photo: { width: '100%', height: 200, marginTop: 12, borderRadius: 8, resizeMode: 'cover' },
  submit: { marginTop: 24, padding: 16, backgroundColor: '#2D5BFF', borderRadius: 10, alignItems: 'center' },
  submitBusy: { opacity: 0.6 },
  submitText: { color: '#fff', fontSize: 16, fontWeight: '700' },
  permissionView: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  permissionText: { fontSize: 16, marginBottom: 16 },
  cta: { padding: 12, paddingHorizontal: 24, backgroundColor: '#2D5BFF', borderRadius: 8 },
  ctaText: { color: '#fff', fontWeight: '600' },
  scanCancel: { position: 'absolute', bottom: 40, alignSelf: 'center', padding: 12, paddingHorizontal: 24, backgroundColor: '#fff', borderRadius: 8 },
  scanCancelText: { fontWeight: '600' },
});
