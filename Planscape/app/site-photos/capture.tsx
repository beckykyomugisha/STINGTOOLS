// Phase 178 — Site photo capture flow.
//
// Flow stages:
//   1. Permission rationale (one-time per device, suppressed thereafter).
//   2. Live camera (expo-camera) with shutter.
//   3. Reason confirmation strip (six chips, classifier pre-pick).
//   4. Anchor row (level/zone resolved from GPS or project default).
//   5. Optional caption (mandatory only at approval — server enforces).
//   6. Save: post if online, enqueue if offline. Cleans up gracefully.

import { useEffect, useRef, useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  Alert,
  ScrollView,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Location from 'expo-location';
import { useLocalSearchParams, useRouter } from 'expo-router';
import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';

import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  classifyCapture,
  REASONS,
  describeReason,
  reasonRoutesToReview,
  reasonAutoCreatesIssue,
  type ClassifierContext,
} from '@/components/site-photos/classifier';
import { captureSitePhoto, getSpatialStructure, type SpatialStructure } from '@/api/endpoints';
import { enqueue, persistPhotoForQueue, queuedPhotoStats } from '@/utils/offlineQueue';
import { computePairKey } from '@/services/imageService';
import { AudioRecorder } from '@/components/AudioRecorder';
import type { SitePhotoCaptureMeta, SitePhotoReason } from '@/types/api';

const RATIONALE_SEEN_KEY = 'planscape_site_photo_rationale_seen_v1';

type Stage = 'rationale' | 'camera' | 'confirm';

interface PendingShot {
  uri: string;
  width: number;
  height: number;
  capturedAt: string;
  latitude?: number;
  longitude?: number;
  accuracyM?: number;
}

export default function CaptureSitePhotoScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{
    projectId?: string;
    anchorIssueId?: string;
    anchorElementGuid?: string;
    context?: string;
  }>();
  const activeProject = useProjectStore((s) => s.active);
  const projectId = params.projectId ?? activeProject?.id;

  // Camera + location permissions are a separate concern from the
  // pre-prompt rationale: the rationale is _our_ explanation, the OS
  // permission is the real gate. We always show the rationale FIRST so the
  // user has context before the OS dialog appears.
  const [permission, requestPermission] = useCameraPermissions();
  const [stage, setStage] = useState<Stage>('rationale');
  const [rationaleLoading, setRationaleLoading] = useState(true);

  const [shot, setShot] = useState<PendingShot | null>(null);
  const [reason, setReason] = useState<SitePhotoReason>('Reference');
  const [classifierConfidence, setClassifierConfidence] = useState(0);
  const [classifierSignals, setClassifierSignals] = useState<Record<string, unknown>>({});
  const [caption, setCaption] = useState('');
  const [levelCode, setLevelCode] = useState('');
  const [zoneCode, setZoneCode] = useState('');
  const [saving, setSaving] = useState(false);
  const [queuedHint, setQueuedHint] = useState<string | null>(null);
  const [spatialData, setSpatialData] = useState<SpatialStructure | null>(null);

  const cameraRef = useRef<CameraView | null>(null);

  // ── Rationale gate ───────────────────────────────────────────────────
  useEffect(() => {
    (async () => {
      const seen = await AsyncStorage.getItem(RATIONALE_SEEN_KEY);
      if (seen === '1') {
        // We've shown the rationale before — go straight to the camera path.
        // Permission may still be denied; OS will surface its own dialog.
        await ensureCameraPermission();
        setStage('camera');
      }
      setRationaleLoading(false);
    })();
  }, []);

  // ── Spatial structure (levels + zones) ──────────────────────────────
  // Fetched once on mount so the confirm-stage chip pickers are populated.
  // Failure is non-fatal — the chips simply won't appear and the ISO label
  // still renders with whatever the user typed / defaulted to.
  useEffect(() => {
    if (!projectId) return;
    getSpatialStructure(projectId)
      .then((data) => {
        setSpatialData(data);
        // Pre-select defaults: GF for level, Z01 for zone.
        if (!levelCode && data.levels.length > 0) {
          const gf = data.levels.find((l) => l.code === 'GF');
          setLevelCode(gf ? gf.code : data.levels[0].code);
        }
        if (!zoneCode && data.zones.length > 0) {
          const z01 = data.zones.find((z) => z.code === 'Z01');
          setZoneCode(z01 ? z01.code : data.zones[0].code);
        }
      })
      .catch(() => { /* non-fatal — chips hidden, free-text fallback */ });
  }, [projectId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function ensureCameraPermission(): Promise<boolean> {
    if (permission?.granted) return true;
    const res = await requestPermission();
    if (!res.granted) {
      Alert.alert(
        'Camera permission required',
        'Enable camera access in Settings to capture site photos.',
      );
      return false;
    }
    return true;
  }

  async function onAcceptRationale() {
    await AsyncStorage.setItem(RATIONALE_SEEN_KEY, '1');
    const ok = await ensureCameraPermission();
    if (!ok) return;
    setStage('camera');
  }

  // ── Capture ──────────────────────────────────────────────────────────
  const onShutter = useCallback(async () => {
    if (!cameraRef.current) return;
    try {
      // takePictureAsync exists on CameraView — exif: false so OS doesn't
      // bake GPS into the JPEG; we attach GPS via the multipart fields.
      const pic = await cameraRef.current.takePictureAsync({
        quality: 0.85,
        exif: false,
      });
      if (!pic) return;

      // Best-effort GPS — capture latency matters more than precision.
      let lat: number | undefined;
      let lon: number | undefined;
      let acc: number | undefined;
      try {
        const perm = await Location.getForegroundPermissionsAsync();
        if (perm.granted) {
          const pos = await Location.getCurrentPositionAsync({
            accuracy: Location.Accuracy.Balanced,
          });
          lat = pos.coords.latitude;
          lon = pos.coords.longitude;
          acc = pos.coords.accuracy ?? undefined;
        }
      } catch { /* GPS off — server still accepts the photo */ }

      const taken: PendingShot = {
        uri: pic.uri,
        width: pic.width,
        height: pic.height,
        capturedAt: new Date().toISOString(),
        latitude: lat,
        longitude: lon,
        accuracyM: acc,
      };
      setShot(taken);

      // Compute perceptual hash for duplicate-detection (pairKey) — fire and
      // forget on the capture path; failure is non-fatal.
      computePairKey(pic.uri).then(setPairKey).catch(() => setPairKey(null));

      // Run classifier and pre-select the reason chip.
      const ctx = paramsContext(params.context);
      const classifier = classifyCapture({
        context: ctx,
        hasAnchorIssueId: !!params.anchorIssueId,
        hasAnchorElementGuid: !!params.anchorElementGuid,
        hour: new Date().getHours(),
        hasGps: lat !== undefined,
        hasActiveWorkPackage: false, // wired later if/when project filters land
      });
      setReason(classifier.reason);
      setClassifierConfidence(classifier.confidence);
      setClassifierSignals(classifier.signals);

      // Surface queue saturation hint up-front.
      try {
        const q = await queuedPhotoStats();
        if (q.warn) {
          setQueuedHint(`${q.count} photos still queued — connect to Wi-Fi to flush.`);
        }
      } catch { /* non-fatal */ }

      setStage('confirm');
    } catch (err) {
      Alert.alert(
        'Capture failed',
        err instanceof Error ? err.message : String(err),
      );
    }
  }, [params.anchorElementGuid, params.anchorIssueId, params.context]);

  // ── Save ─────────────────────────────────────────────────────────────
  async function onSave() {
    if (!shot || !projectId) return;
    setSaving(true);
    const meta: SitePhotoCaptureMeta = {
      reason,
      caption: caption.trim() || undefined,
      levelCode: levelCode.trim() || undefined,
      zoneCode: zoneCode.trim() || undefined,
      latitude: shot.latitude,
      longitude: shot.longitude,
      accuracyM: shot.accuracyM,
      classifierConfidence,
      classifierSignals,
      capturedAt: shot.capturedAt,
      source: 'mobile',
      anchorIssueId: params.anchorIssueId,
      anchorElementGuid: params.anchorElementGuid,
    };

    try {
      const net = await NetInfo.fetch();
      if (net.isConnected) {
        await captureSitePhoto({
          projectId,
          uri: shot.uri,
          fileName: `site-photo-${Date.now()}.jpg`,
          contentType: 'image/jpeg',
          meta: { ...meta, queuedClient: false },
          pairKey: pairKey ?? undefined,
        });
        Alert.alert(
          'Photo saved',
          reasonAutoCreatesIssue(reason)
            ? 'Photo uploaded and a corresponding issue has been opened.'
            : reasonRoutesToReview(reason)
              ? 'Photo uploaded and queued for PM review.'
              : 'Photo saved to internal gallery.',
        );
        router.back();
      } else {
        // Offline path — copy bytes to stable storage and enqueue.
        const stored = await persistPhotoForQueue(shot.uri);
        await enqueue('CAPTURE_SITE_PHOTO', {
          projectId,
          localUri: stored,
          fileName: `site-photo-${Date.now()}.jpg`,
          mimeType: 'image/jpeg',
          meta: { ...meta, queuedClient: true },
          pairKey: pairKey ?? undefined,
        });
        Alert.alert(
          'Saved offline',
          'Photo will upload automatically when network is back.',
        );
        router.back();
      }
    } catch (err) {
      // Foreground retries already exhausted (3xx/5xx) — fall back to queue.
      try {
        const stored = await persistPhotoForQueue(shot.uri);
        await enqueue('CAPTURE_SITE_PHOTO', {
          projectId,
          localUri: stored,
          fileName: `site-photo-${Date.now()}.jpg`,
          mimeType: 'image/jpeg',
          meta: { ...meta, queuedClient: true },
          pairKey: pairKey ?? undefined,
        });
        Alert.alert(
          'Upload failed — queued for retry',
          err instanceof Error ? err.message : String(err),
        );
        router.back();
      } catch (queueErr) {
        Alert.alert(
          'Save failed',
          queueErr instanceof Error ? queueErr.message : String(queueErr),
        );
      }
    } finally {
      setSaving(false);
    }
  }

  // ── Render ───────────────────────────────────────────────────────────
  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project before capturing photos.</Text>
      </View>
    );
  }

  if (rationaleLoading) {
    return (
      <View style={styles.loading}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  if (stage === 'rationale') {
    return (
      <View style={styles.root}>
        <ScrollView contentContainerStyle={styles.rationaleScroll}>
          <Text style={styles.rationaleHeader}>Why we ask for these permissions</Text>
          <RationaleRow
            icon="📷"
            title="Camera"
            text="Capture progress, defect, safety, and as-built photos directly into the project record."
          />
          <RationaleRow
            icon="📍"
            title="Location"
            text="Stamps each photo with site coordinates so the team can spot which floor or block it came from."
          />
          <RationaleRow
            icon="🖼"
            title="Photos library"
            text="Lets you attach an existing image when the moment has already passed."
          />
          <Text style={styles.rationaleFooter}>
            Photos with reason &ldquo;Issue&rdquo;, &ldquo;Defect&rdquo;, or &ldquo;Safety&rdquo; auto-open an issue.
            Anything tagged &ldquo;Reference&rdquo; stays internal — the client never sees it.
          </Text>
          <TouchableOpacity style={styles.primaryBtn} onPress={onAcceptRationale}>
            <Text style={styles.primaryBtnText}>Continue</Text>
          </TouchableOpacity>
          <TouchableOpacity style={styles.secondaryBtn} onPress={() => router.back()}>
            <Text style={styles.secondaryBtnText}>Not now</Text>
          </TouchableOpacity>
        </ScrollView>
      </View>
    );
  }

  if (stage === 'camera') {
    if (!permission?.granted) {
      return (
        <View style={styles.empty}>
          <Text style={styles.emptyText}>Camera permission required.</Text>
          <TouchableOpacity style={styles.primaryBtn} onPress={ensureCameraPermission}>
            <Text style={styles.primaryBtnText}>Grant access</Text>
          </TouchableOpacity>
        </View>
      );
    }
    return (
      <View style={{ flex: 1, backgroundColor: '#000' }}>
        <CameraView
          ref={(r) => { cameraRef.current = r; }}
          style={{ flex: 1 }}
          facing="back"
        />
        <View style={styles.cameraBar}>
          <TouchableOpacity
            style={styles.cancelBtn}
            onPress={() => router.back()}
            accessibilityLabel="Cancel capture"
          >
            <Text style={styles.cancelText}>Cancel</Text>
          </TouchableOpacity>
          <TouchableOpacity
            style={styles.shutter}
            onPress={onShutter}
            accessibilityLabel="Take photo"
          >
            <View style={styles.shutterInner} />
          </TouchableOpacity>
          <View style={{ width: 60 }} />
        </View>
      </View>
    );
  }

  // stage === 'confirm'
  return (
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        style={styles.root}
        contentContainerStyle={styles.confirmScroll}
        keyboardShouldPersistTaps="handled"
      >
        {queuedHint ? (
          <View style={styles.warnBanner}>
            <Text style={styles.warnText}>{queuedHint}</Text>
          </View>
        ) : null}

        {/* Reason chip strip */}
        <Text style={styles.sectionLabel}>
          Reason
          {classifierConfidence > 0 ? (
            <Text style={styles.confidenceText}> · auto-suggested ({Math.round(classifierConfidence * 100)}%)</Text>
          ) : null}
        </Text>
        <View style={styles.reasonStrip}>
          {REASONS.map((r) => (
            <TouchableOpacity
              key={r}
              style={[styles.reasonChip, reason === r && styles.reasonChipActive]}
              onPress={() => setReason(r)}
              accessibilityRole="radio"
              accessibilityState={{ selected: reason === r }}
              accessibilityLabel={`Reason ${r}`}
            >
              <Text style={[styles.reasonChipText, reason === r && styles.reasonChipTextActive]}>
                {r}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
        <Text style={styles.reasonHint}>{describeReason(reason)}</Text>
        {reasonRoutesToReview(reason) ? (
          <Text style={styles.routeHint}>→ Will be queued for PM review on save.</Text>
        ) : reasonAutoCreatesIssue(reason) ? (
          <Text style={styles.routeHint}>→ Will auto-open an issue and notify watchers.</Text>
        ) : (
          <Text style={styles.routeHint}>→ Stays in the internal gallery only.</Text>
        )}

        {/* Anchor row — level / zone chip pickers */}
        <Text style={styles.sectionLabel}>Location</Text>

        {/* Level chips */}
        <Text style={styles.anchorLabel}>Level</Text>
        {spatialData && spatialData.levels.length > 0 ? (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            style={styles.chipScroll}
            contentContainerStyle={styles.chipScrollContent}
          >
            {spatialData.levels.map((lvl) => (
              <TouchableOpacity
                key={lvl.code}
                style={[styles.isoChip, levelCode === lvl.code && styles.isoChipActive]}
                onPress={() => setLevelCode(lvl.code)}
                accessibilityRole="radio"
                accessibilityState={{ selected: levelCode === lvl.code }}
                accessibilityLabel={`Level ${lvl.label}`}
              >
                <Text style={[styles.isoChipText, levelCode === lvl.code && styles.isoChipTextActive]}>
                  {lvl.code}
                </Text>
                {levelCode === lvl.code ? (
                  <Text style={styles.isoChipSubtitle}>{lvl.label}</Text>
                ) : null}
              </TouchableOpacity>
            ))}
          </ScrollView>
        ) : (
          <TextInput
            style={styles.anchorInput}
            placeholder="L01, GF, B1"
            placeholderTextColor={theme.colors.disabled}
            value={levelCode}
            onChangeText={setLevelCode}
            autoCapitalize="characters"
            autoCorrect={false}
          />
        )}

        {/* Zone chips */}
        <Text style={[styles.anchorLabel, { marginTop: theme.spacing.sm }]}>Zone</Text>
        {spatialData && spatialData.zones.length > 0 ? (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            style={styles.chipScroll}
            contentContainerStyle={styles.chipScrollContent}
          >
            {spatialData.zones.map((z) => (
              <TouchableOpacity
                key={z.code}
                style={[styles.isoChip, zoneCode === z.code && styles.isoChipActive]}
                onPress={() => setZoneCode(z.code)}
                accessibilityRole="radio"
                accessibilityState={{ selected: zoneCode === z.code }}
                accessibilityLabel={`Zone ${z.label}`}
              >
                <Text style={[styles.isoChipText, zoneCode === z.code && styles.isoChipTextActive]}>
                  {z.code}
                </Text>
                {zoneCode === z.code ? (
                  <Text style={styles.isoChipSubtitle}>{z.label}</Text>
                ) : null}
              </TouchableOpacity>
            ))}
          </ScrollView>
        ) : (
          <TextInput
            style={styles.anchorInput}
            placeholder="Z01, EXT"
            placeholderTextColor={theme.colors.disabled}
            value={zoneCode}
            onChangeText={setZoneCode}
            autoCapitalize="characters"
            autoCorrect={false}
          />
        )}

        {/* ISO 19650 assembled code preview */}
        <Text style={styles.isoLabel}>
          ISO ref: {reason ? reason.slice(0, 1).toUpperCase() : 'XX'}-{levelCode || 'XX'}-{zoneCode || 'XX'}
        </Text>

        {shot?.latitude !== undefined ? (
          <Text style={styles.gpsHint}>
            GPS captured · ±{Math.round(shot.accuracyM ?? 0)}m
          </Text>
        ) : (
          <Text style={styles.gpsHint}>No GPS — server falls back to project default.</Text>
        )}

        {/* Optional caption */}
        <Text style={styles.sectionLabel}>Caption (optional)</Text>
        <TextInput
          style={styles.captionInput}
          placeholder="Optional now — required when a PM publishes the photo."
          placeholderTextColor={theme.colors.disabled}
          value={caption}
          onChangeText={setCaption}
          multiline
          numberOfLines={3}
        />
        {/* T3-7 — Voice-to-text dictation alongside the caption. The
            recording is queued as ATTACH_AUDIO; if this capture has an
            anchor issue id we link it directly, otherwise it queues under
            "__pending__" and surfaces in conflict triage.
            The /audio-notes server endpoint is live; ATTACH_AUDIO actions
            replay cleanly via replayAction in offlineQueue.ts. */}
        <AudioRecorder
          projectId={projectId}
          issueId={params.anchorIssueId}
          contextTag="site-photo-caption"
        />

        {/* Save row */}
        <View style={styles.saveRow}>
          <TouchableOpacity
            style={[styles.secondaryBtn, { flex: 1 }]}
            onPress={() => { setShot(null); setStage('camera'); }}
            disabled={saving}
          >
            <Text style={styles.secondaryBtnText}>Retake</Text>
          </TouchableOpacity>
          <TouchableOpacity
            style={[styles.primaryBtn, { flex: 2, marginLeft: theme.spacing.sm }]}
            onPress={onSave}
            disabled={saving}
          >
            {saving ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={styles.primaryBtnText}>Save photo</Text>
            )}
          </TouchableOpacity>
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function paramsContext(s: string | string[] | undefined): ClassifierContext {
  const v = Array.isArray(s) ? s[0] : s;
  switch (v) {
    case 'dashboard':
    case 'diary':
    case 'issue-context':
    case 'element-context':
    case 'gallery':
      return v;
    default:
      return 'unknown';
  }
}

function RationaleRow({ icon, title, text }: { icon: string; title: string; text: string }) {
  return (
    <View style={styles.rationaleRow}>
      <Text style={styles.rationaleIcon}>{icon}</Text>
      <View style={{ flex: 1 }}>
        <Text style={styles.rationaleTitle}>{title}</Text>
        <Text style={styles.rationaleText}>{text}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md, marginBottom: theme.spacing.md },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },

  // Rationale screen
  rationaleScroll: { padding: theme.spacing.lg },
  rationaleHeader: {
    fontSize: theme.fontSize.xl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.lg,
  },
  rationaleRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  rationaleIcon: { fontSize: 28, marginRight: theme.spacing.md },
  rationaleTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  rationaleText: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 2 },
  rationaleFooter: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: theme.spacing.md,
    marginBottom: theme.spacing.lg,
  },

  // Camera screen
  cameraBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: 'rgba(0,0,0,0.7)',
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.lg,
  },
  cancelBtn: { padding: theme.spacing.sm },
  cancelText: { color: '#fff', fontSize: theme.fontSize.md },
  shutter: {
    width: 72, height: 72, borderRadius: 36,
    backgroundColor: 'rgba(255,255,255,0.2)',
    justifyContent: 'center', alignItems: 'center',
    borderWidth: 4, borderColor: '#fff',
  },
  shutterInner: {
    width: 52, height: 52, borderRadius: 26,
    backgroundColor: '#fff',
  },

  // Confirm screen
  confirmScroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  sectionLabel: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
    marginTop: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  confidenceText: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, fontWeight: '400' },
  reasonStrip: {
    flexDirection: 'row',
    flexWrap: 'wrap',
  },
  reasonChip: {
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: theme.colors.border,
    backgroundColor: theme.colors.surface,
    marginRight: theme.spacing.xs,
    marginBottom: theme.spacing.xs,
  },
  reasonChipActive: {
    backgroundColor: theme.colors.accent,
    borderColor: theme.colors.accent,
  },
  reasonChipText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
  reasonChipTextActive: { color: '#fff' },
  reasonHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4 },
  routeHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.primary,
    marginTop: 4,
    fontStyle: 'italic',
  },

  anchorLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: 2 },
  anchorInput: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  // ISO chip pickers (level + zone)
  chipScroll: { marginBottom: 4 },
  chipScrollContent: { paddingRight: theme.spacing.sm },
  isoChip: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: theme.colors.border,
    backgroundColor: theme.colors.surface,
    marginRight: theme.spacing.xs,
    alignItems: 'center',
    minWidth: 48,
  },
  isoChipActive: {
    backgroundColor: theme.colors.primary,
    borderColor: theme.colors.primary,
  },
  isoChipText: {
    color: theme.colors.text,
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
  },
  isoChipTextActive: { color: '#fff' },
  isoChipSubtitle: {
    color: 'rgba(255,255,255,0.8)',
    fontSize: 9,
    marginTop: 1,
  },
  isoLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.primary,
    fontWeight: '500',
    marginTop: theme.spacing.xs,
    marginBottom: 2,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  gpsHint: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },
  captionInput: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    padding: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
    minHeight: 72,
    textAlignVertical: 'top',
  },

  saveRow: { flexDirection: 'row', marginTop: theme.spacing.lg },
  primaryBtn: {
    backgroundColor: theme.colors.accent,
    paddingVertical: 14,
    borderRadius: theme.borderRadius.md,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: theme.spacing.md,
  },
  primaryBtnText: { color: '#fff', fontWeight: '600', fontSize: theme.fontSize.md },
  secondaryBtn: {
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingVertical: 14,
    borderRadius: theme.borderRadius.md,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: theme.spacing.md,
  },
  secondaryBtnText: { color: theme.colors.text, fontWeight: '600', fontSize: theme.fontSize.md },
  warnBanner: {
    backgroundColor: '#FFF3E0',
    borderColor: theme.colors.warning,
    borderWidth: 1,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  warnText: { color: theme.colors.warning, fontSize: theme.fontSize.sm, fontWeight: '600' },
});
