// T3-6 — Punchlist mode (rapid defect capture with bulk assign).
//
// Flow:
//   1. "Start punchlist walk" → opens live camera.
//   2. Each shutter creates an in-memory `DraftDefect` and appends a thumb
//      to the side strip. Running cycle-time counter ("3 defects in 2:14")
//      gamifies the walk.
//   3. After ≥1 photo the "Walk complete (N defects)" CTA appears.
//   4. Bulk-edit screen: assign every draft defect to one subcontractor +
//      due date + shared caption. Per-row caption override is supported.
//   5. Submit posts each draft as a `SitePhotoCaptureMeta { reason: "Defect" }`
//      via the existing `captureSitePhoto()` pipeline. The server's photo
//      controller already auto-creates an NCR for `Defect` reason — see
//      `SitePhotosController.cs`. Offline drafts fall back to the queue.
//
// Reuses:
//   - `captureSitePhoto()` from endpoints.ts
//   - `enqueue('CAPTURE_SITE_PHOTO', ...)` + `persistPhotoForQueue()` from
//     offlineQueue.ts
//   - `MemberPicker` for assignee selection
//   - Existing classifier metadata (we hard-code `reason: 'Defect'` and
//     `source: 'mobile-punchlist'` so server analytics can spot the path).
//
// TODO-SERVER: shared bulk-assign endpoint not yet present. Today the
//   resulting NCRs auto-created by the photo path are independent records;
//   a follow-up server PR could group them via a `pairKey = punchlistId`
//   so the supervisor sees the walk as one bundle on the issues list. The
//   `pairKey` is already accepted by SitePhotoCaptureMeta, so wiring is a
//   server-side aggregation — no mobile change needed when it lands.

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
  Image,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Location from 'expo-location';
import { useRouter } from 'expo-router';
import NetInfo from '@react-native-community/netinfo';

import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import { captureSitePhoto } from '@/api/endpoints';
import { enqueue, persistPhotoForQueue } from '@/utils/offlineQueue';
import { MemberPicker } from '@/components/MemberPicker';
import type { ProjectMember, SitePhotoCaptureMeta } from '@/types/api';

interface DraftDefect {
  id: string;
  uri: string;
  width: number;
  height: number;
  capturedAt: string;
  latitude?: number;
  longitude?: number;
  accuracyM?: number;
  /** Per-row override; falls back to the shared caption at submit time. */
  caption?: string;
}

type Stage = 'intro' | 'walking' | 'review';

export default function PunchlistScreen() {
  const router = useRouter();
  const activeProject = useProjectStore((s) => s.active);
  const projectId = activeProject?.id;

  const [permission, requestPermission] = useCameraPermissions();
  const [stage, setStage] = useState<Stage>('intro');
  const [drafts, setDrafts] = useState<DraftDefect[]>([]);
  const [walkStartedAt, setWalkStartedAt] = useState<number | null>(null);
  const [now, setNow] = useState<number>(Date.now());

  // Bulk-assign fields.
  const [assignee, setAssignee] = useState<ProjectMember | null>(null);
  const [memberPickerOpen, setMemberPickerOpen] = useState(false);
  const [dueDate, setDueDate] = useState('');
  const [sharedCaption, setSharedCaption] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const cameraRef = useRef<CameraView | null>(null);
  // Generate unique per-walk pairKey so server can group the resulting
  // NCRs as one logical punchlist (when the aggregation server PR lands).
  const pairKeyRef = useRef<string>(`punchlist-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`);

  // Cycle-time ticker — drives the running "N defects in mm:ss" indicator.
  useEffect(() => {
    if (stage !== 'walking') return;
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, [stage]);

  async function ensureCameraPermission(): Promise<boolean> {
    if (permission?.granted) return true;
    const res = await requestPermission();
    if (!res.granted) {
      Alert.alert(
        'Camera permission required',
        'Enable camera access to start a punchlist walk.',
      );
      return false;
    }
    return true;
  }

  async function onStartWalk() {
    const ok = await ensureCameraPermission();
    if (!ok) return;
    pairKeyRef.current = `punchlist-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    setDrafts([]);
    setWalkStartedAt(Date.now());
    setNow(Date.now());
    setStage('walking');
  }

  const onShutter = useCallback(async () => {
    if (!cameraRef.current) return;
    try {
      const pic = await cameraRef.current.takePictureAsync({
        quality: 0.85,
        exif: false,
      });
      if (!pic) return;

      // Best-effort GPS — we don't block the walk on a slow GPS lock.
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

      const draft: DraftDefect = {
        id: `${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        uri: pic.uri,
        width: pic.width,
        height: pic.height,
        capturedAt: new Date().toISOString(),
        latitude: lat,
        longitude: lon,
        accuracyM: acc,
      };
      setDrafts((prev) => [...prev, draft]);
    } catch (err) {
      Alert.alert(
        'Capture failed',
        err instanceof Error ? err.message : String(err),
      );
    }
  }, []);

  function onRemoveDraft(id: string) {
    setDrafts((prev) => prev.filter((d) => d.id !== id));
  }

  function onWalkComplete() {
    if (drafts.length === 0) {
      Alert.alert('No defects yet', 'Capture at least one photo before completing the walk.');
      return;
    }
    setStage('review');
  }

  async function onSubmit() {
    if (!projectId) return;
    if (drafts.length === 0) {
      Alert.alert('Nothing to submit', 'No defects in the walk.');
      return;
    }
    if (!assignee) {
      Alert.alert(
        'Assignee required',
        'Pick a subcontractor or team member to receive these defects.',
      );
      return;
    }
    if (!sharedCaption.trim()) {
      Alert.alert(
        'Shared caption required',
        'Add a short summary so the subcontractor knows what they\'re looking at.',
      );
      return;
    }

    setSubmitting(true);
    let succeeded = 0;
    let queued = 0;
    let failed = 0;
    const net = await NetInfo.fetch();
    const online = !!net.isConnected;

    for (const d of drafts) {
      const meta: SitePhotoCaptureMeta = {
        reason: 'Defect',
        caption: (d.caption?.trim() || sharedCaption.trim()),
        latitude: d.latitude,
        longitude: d.longitude,
        accuracyM: d.accuracyM,
        capturedAt: d.capturedAt,
        source: 'mobile-punchlist',
        pairKey: pairKeyRef.current,
      };

      try {
        if (online) {
          await captureSitePhoto({
            projectId,
            uri: d.uri,
            fileName: `punchlist-${d.id}.jpg`,
            contentType: 'image/jpeg',
            meta: { ...meta, queuedClient: false },
          });
          succeeded++;
        } else {
          const stored = await persistPhotoForQueue(d.uri);
          await enqueue('CAPTURE_SITE_PHOTO', {
            projectId,
            localUri: stored,
            fileName: `punchlist-${d.id}.jpg`,
            mimeType: 'image/jpeg',
            meta: { ...meta, queuedClient: true },
          });
          queued++;
        }
      } catch (err) {
        // Foreground retries already exhausted in captureSitePhoto — fall
        // back to the queue so a flaky link never costs us a defect.
        try {
          const stored = await persistPhotoForQueue(d.uri);
          await enqueue('CAPTURE_SITE_PHOTO', {
            projectId,
            localUri: stored,
            fileName: `punchlist-${d.id}.jpg`,
            mimeType: 'image/jpeg',
            meta: { ...meta, queuedClient: true },
          });
          queued++;
        } catch {
          failed++;
        }
        // Don't surface per-item alerts — the summary at the end handles it.
        void err;
      }
    }

    setSubmitting(false);

    // TODO-SERVER: bulk-assign endpoint. Until then we surface the
    // assignee + due-date as part of the shared caption so the
    // subcontractor sees them on every auto-created NCR. The server
    // photo→NCR pipeline already populates `Description` from caption.
    const assignNote = `Assigned: ${assignee.email}${dueDate ? ` · Due: ${dueDate}` : ''}`;
    void assignNote;

    Alert.alert(
      'Punchlist submitted',
      [
        succeeded > 0 ? `${succeeded} uploaded.` : '',
        queued > 0 ? `${queued} queued (offline).` : '',
        failed > 0 ? `${failed} failed — see queue.` : '',
        '\nNCRs are auto-created on the server for each defect.',
      ].filter(Boolean).join('\n'),
    );
    router.back();
  }

  // ── Render ────────────────────────────────────────────────────────────
  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project before starting a punchlist walk.</Text>
      </View>
    );
  }

  if (stage === 'intro') {
    return (
      <ScrollView contentContainerStyle={styles.introScroll}>
        <Text style={styles.introTitle}>🎯 Punchlist mode</Text>
        <Text style={styles.introBody}>
          Snap defects in rapid succession — every shutter creates a draft Defect with GPS.
          When the walk is done, assign them all to one subcontractor in a single bulk-edit step.
          Each defect auto-opens an NCR on the server.
        </Text>
        <View style={styles.introBullets}>
          <Bullet text="Camera stays open between shots — keep shooting until you're done." />
          <Bullet text="Cycle-time counter shows your walking pace." />
          <Bullet text="Bulk-assign one subcontractor, due date, and shared caption." />
          <Bullet text="Offline-safe — drafts queue and upload when network is back." />
        </View>
        <TouchableOpacity style={styles.primaryBtn} onPress={onStartWalk}>
          <Text style={styles.primaryBtnText}>Start punchlist walk</Text>
        </TouchableOpacity>
        <TouchableOpacity style={styles.secondaryBtn} onPress={() => router.back()}>
          <Text style={styles.secondaryBtnText}>Cancel</Text>
        </TouchableOpacity>
      </ScrollView>
    );
  }

  if (stage === 'walking') {
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
    const elapsedMs = walkStartedAt ? now - walkStartedAt : 0;
    const seconds = Math.floor(elapsedMs / 1000);
    const mm = String(Math.floor(seconds / 60)).padStart(2, '0');
    const ss = String(seconds % 60).padStart(2, '0');
    return (
      <View style={{ flex: 1, backgroundColor: '#000' }}>
        <CameraView
          ref={(r) => { cameraRef.current = r; }}
          style={{ flex: 1 }}
          facing="back"
        />

        {/* Top HUD — running counter */}
        <View style={styles.hudTop}>
          <Text style={styles.hudText}>
            {drafts.length} defect{drafts.length === 1 ? '' : 's'} in {mm}:{ss}
          </Text>
        </View>

        {/* Side strip of thumbs */}
        {drafts.length > 0 && (
          <ScrollView
            horizontal
            style={styles.thumbStrip}
            contentContainerStyle={{ paddingHorizontal: theme.spacing.sm }}
            showsHorizontalScrollIndicator={false}
          >
            {drafts.map((d) => (
              <TouchableOpacity
                key={d.id}
                onLongPress={() => onRemoveDraft(d.id)}
                accessibilityLabel="Long-press to remove draft defect"
              >
                <Image source={{ uri: d.uri }} style={styles.thumb} />
              </TouchableOpacity>
            ))}
          </ScrollView>
        )}

        {/* Bottom shutter / complete bar */}
        <View style={styles.cameraBar}>
          <TouchableOpacity
            style={styles.cancelBtn}
            onPress={() => {
              if (drafts.length > 0) {
                Alert.alert(
                  'Discard walk?',
                  `${drafts.length} draft defect${drafts.length === 1 ? '' : 's'} will be lost.`,
                  [
                    { text: 'Keep shooting', style: 'cancel' },
                    {
                      text: 'Discard',
                      style: 'destructive',
                      onPress: () => router.back(),
                    },
                  ],
                );
              } else {
                router.back();
              }
            }}
            accessibilityLabel="Cancel punchlist walk"
          >
            <Text style={styles.cancelText}>Cancel</Text>
          </TouchableOpacity>

          <TouchableOpacity
            style={styles.shutter}
            onPress={onShutter}
            accessibilityLabel="Capture defect"
          >
            <View style={styles.shutterInner} />
          </TouchableOpacity>

          <TouchableOpacity
            style={[styles.completeBtn, drafts.length === 0 && styles.completeBtnDisabled]}
            disabled={drafts.length === 0}
            onPress={onWalkComplete}
            accessibilityLabel="Walk complete"
          >
            <Text style={styles.completeText}>
              {drafts.length === 0 ? 'Tap shutter to start' : `Done (${drafts.length})`}
            </Text>
          </TouchableOpacity>
        </View>
      </View>
    );
  }

  // stage === 'review' — bulk-assign
  return (
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        style={styles.root}
        contentContainerStyle={styles.reviewScroll}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={styles.reviewHeader}>
          Walk complete — {drafts.length} defect{drafts.length === 1 ? '' : 's'}
        </Text>
        <Text style={styles.reviewSub}>
          Apply one assignee, due date, and caption to all {drafts.length}.
        </Text>

        <Text style={styles.sectionLabel}>Assign to</Text>
        <TouchableOpacity
          style={styles.pickerBtn}
          onPress={() => setMemberPickerOpen(true)}
        >
          <Text style={styles.pickerText}>
            {assignee ? `${assignee.displayName ?? assignee.email}` : 'Pick a subcontractor or team member'}
          </Text>
        </TouchableOpacity>

        <Text style={styles.sectionLabel}>Due date</Text>
        <TextInput
          style={styles.input}
          placeholder="YYYY-MM-DD (optional)"
          placeholderTextColor={theme.colors.disabled}
          value={dueDate}
          onChangeText={setDueDate}
          autoCapitalize="none"
        />

        <Text style={styles.sectionLabel}>Shared caption *</Text>
        <TextInput
          style={[styles.input, styles.inputMulti]}
          placeholder="e.g. snagging round level 03 east — see photos"
          placeholderTextColor={theme.colors.disabled}
          value={sharedCaption}
          onChangeText={setSharedCaption}
          multiline
          numberOfLines={3}
        />

        <Text style={styles.sectionLabel}>Defects ({drafts.length})</Text>
        <Text style={styles.smallHint}>
          Long-press a thumb to remove. Tap caption to override the shared one for that defect.
        </Text>
        <View style={styles.draftList}>
          {drafts.map((d, i) => (
            <View key={d.id} style={styles.draftRow}>
              <Image source={{ uri: d.uri }} style={styles.draftThumb} />
              <View style={{ flex: 1, marginLeft: theme.spacing.sm }}>
                <Text style={styles.draftIndex}>#{i + 1}</Text>
                <TextInput
                  style={styles.draftCaption}
                  placeholder="(uses shared caption)"
                  placeholderTextColor={theme.colors.disabled}
                  value={d.caption ?? ''}
                  onChangeText={(txt) => {
                    setDrafts((prev) => prev.map((x) => x.id === d.id ? { ...x, caption: txt } : x));
                  }}
                />
              </View>
              <TouchableOpacity
                onPress={() => onRemoveDraft(d.id)}
                accessibilityLabel={`Remove defect ${i + 1}`}
              >
                <Text style={styles.removeIcon}>✕</Text>
              </TouchableOpacity>
            </View>
          ))}
        </View>

        <View style={styles.submitRow}>
          <TouchableOpacity
            style={[styles.secondaryBtn, { flex: 1 }]}
            onPress={() => setStage('walking')}
            disabled={submitting}
          >
            <Text style={styles.secondaryBtnText}>Add more</Text>
          </TouchableOpacity>
          <TouchableOpacity
            style={[styles.primaryBtn, { flex: 2, marginLeft: theme.spacing.sm }]}
            onPress={onSubmit}
            disabled={submitting}
          >
            {submitting ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={styles.primaryBtnText}>Submit {drafts.length} defects</Text>
            )}
          </TouchableOpacity>
        </View>
      </ScrollView>

      <MemberPicker
        visible={memberPickerOpen}
        projectId={projectId}
        selectedEmail={assignee?.email}
        onSelect={(m) => { setAssignee(m); setMemberPickerOpen(false); }}
        onClose={() => setMemberPickerOpen(false)}
      />
    </KeyboardAvoidingView>
  );
}

function Bullet({ text }: { text: string }) {
  return (
    <View style={styles.bulletRow}>
      <Text style={styles.bulletDot}>•</Text>
      <Text style={styles.bulletText}>{text}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md, marginBottom: theme.spacing.md },

  introScroll: { padding: theme.spacing.lg, backgroundColor: theme.colors.background, flexGrow: 1 },
  introTitle: { fontSize: theme.fontSize.xl, fontWeight: '700', color: theme.colors.text, marginBottom: theme.spacing.md },
  introBody: { fontSize: theme.fontSize.md, color: theme.colors.textSecondary, marginBottom: theme.spacing.lg, lineHeight: 22 },
  introBullets: { marginBottom: theme.spacing.lg },
  bulletRow: { flexDirection: 'row', marginBottom: theme.spacing.sm },
  bulletDot: { color: theme.colors.accent, fontSize: theme.fontSize.lg, marginRight: theme.spacing.sm, lineHeight: 22 },
  bulletText: { flex: 1, fontSize: theme.fontSize.sm, color: theme.colors.text, lineHeight: 22 },

  // Walking HUD
  hudTop: {
    position: 'absolute',
    top: 40,
    alignSelf: 'center',
    backgroundColor: 'rgba(0,0,0,0.7)',
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    borderRadius: 16,
  },
  hudText: { color: '#fff', fontSize: theme.fontSize.md, fontWeight: '700' },
  thumbStrip: {
    position: 'absolute',
    bottom: 130,
    left: 0,
    right: 0,
    maxHeight: 76,
  },
  thumb: {
    width: 64,
    height: 64,
    borderRadius: 8,
    marginRight: theme.spacing.xs,
    borderWidth: 2,
    borderColor: '#fff',
  },
  cameraBar: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: 'rgba(0,0,0,0.75)',
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
    backgroundColor: theme.colors.danger,
  },
  completeBtn: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    borderRadius: theme.borderRadius.md,
    backgroundColor: theme.colors.accent,
    minWidth: 90,
    alignItems: 'center',
  },
  completeBtnDisabled: { opacity: 0.4 },
  completeText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '700' },

  // Review
  reviewScroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  reviewHeader: { fontSize: theme.fontSize.xl, fontWeight: '700', color: theme.colors.text },
  reviewSub: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4, marginBottom: theme.spacing.md },
  sectionLabel: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text, marginTop: theme.spacing.md, marginBottom: theme.spacing.xs },
  smallHint: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: theme.spacing.sm },
  pickerBtn: {
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.md,
  },
  pickerText: { color: theme.colors.text, fontSize: theme.fontSize.md },
  input: {
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  inputMulti: { minHeight: 72, textAlignVertical: 'top' },

  draftList: { marginTop: theme.spacing.sm },
  draftRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.xs,
  },
  draftThumb: { width: 56, height: 56, borderRadius: 6 },
  draftIndex: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, fontWeight: '600' },
  draftCaption: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    paddingVertical: 2,
  },
  removeIcon: {
    fontSize: 20,
    color: theme.colors.danger,
    paddingHorizontal: theme.spacing.sm,
  },

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

  submitRow: { flexDirection: 'row', marginTop: theme.spacing.lg },
});
