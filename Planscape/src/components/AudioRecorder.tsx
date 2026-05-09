// T3-7 — Voice-to-text dictation button.
//
// Self-contained mic + VU meter component. On stop, persists the recording
// to the offline queue as ATTACH_AUDIO (already wired in offlineQueue.ts).
// The mobile bundle deliberately ships NO STT model — transcription is the
// server's job. We surface a "Transcript will appear once processed" toast
// so the user knows the dictation isn't lost.
//
// TODO-SERVER: see endpoints.ts `uploadAudioNote` — the receiving endpoint
//   /api/projects/{pid}/issues/{iid}/audio-notes is not yet implemented.
//   Until it lands, queued ATTACH_AUDIO actions will 404 and migrate to the
//   failed side-queue, where the conflict-triage screen surfaces them.

import { useEffect, useRef, useState } from 'react';
import {
  Alert,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { Audio } from 'expo-av';
import { theme } from '@/utils/theme';
import { enqueue } from '@/utils/offlineQueue';
import { uploadAudioNote } from '@/api/endpoints';
import NetInfo from '@react-native-community/netinfo';

export interface AudioRecorderProps {
  /** Project under which to register the audio note. */
  projectId: string;
  /** Existing issue id when the recorder is rendered on issue-detail.
   *  When omitted (e.g. issue create flow), the action is queued with a
   *  placeholder `pendingIssueId` and the caller should backfill the id
   *  after the issue creation succeeds. */
  issueId?: string;
  /** Optional opaque tag to scope the recording to a context (e.g. "caption"). */
  contextTag?: string;
  /** Fires when an audio note has been successfully queued or uploaded. */
  onNoteCaptured?: (info: { uri: string; durationMs: number; queued: boolean }) => void;
  /** Disables the mic (e.g. while another recording is in flight). */
  disabled?: boolean;
}

type RecorderState = 'idle' | 'recording' | 'finalising';

const TICK_MS = 200;
const VU_BARS = 24;

export function AudioRecorder({
  projectId,
  issueId,
  contextTag,
  onNoteCaptured,
  disabled,
}: AudioRecorderProps) {
  const [state, setState] = useState<RecorderState>('idle');
  const [elapsedMs, setElapsedMs] = useState(0);
  const [meter, setMeter] = useState(0); // 0..1 normalised
  const recordingRef = useRef<Audio.Recording | null>(null);
  const tickRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const startedAtRef = useRef<number>(0);

  useEffect(() => {
    return () => {
      // Best-effort cleanup if the component unmounts mid-recording —
      // discards the bytes; we don't want a half-finished recording to
      // leak into the queue.
      const r = recordingRef.current;
      if (r) {
        r.stopAndUnloadAsync().catch(() => { /* ignore */ });
        recordingRef.current = null;
      }
      if (tickRef.current) clearInterval(tickRef.current);
    };
  }, []);

  async function onStart() {
    if (disabled || state !== 'idle') return;
    try {
      const perm = await Audio.requestPermissionsAsync();
      if (!perm.granted) {
        Alert.alert(
          'Microphone permission required',
          'Enable microphone access in Settings to dictate notes.',
        );
        return;
      }
      await Audio.setAudioModeAsync({
        allowsRecordingIOS: true,
        playsInSilentModeIOS: true,
      });

      const r = new Audio.Recording();
      await r.prepareToRecordAsync({
        ...Audio.RecordingOptionsPresets.HIGH_QUALITY,
        // Ask for periodic status updates so we can drive the VU meter.
        isMeteringEnabled: true,
      });
      r.setProgressUpdateInterval(TICK_MS);
      r.setOnRecordingStatusUpdate((s) => {
        if (!s.isRecording) return;
        // metering is in dBFS (-160..0). Map to 0..1 for the bar fill.
        const db = (s as { metering?: number }).metering ?? -160;
        const norm = Math.max(0, Math.min(1, (db + 60) / 60));
        setMeter(norm);
      });
      await r.startAsync();
      recordingRef.current = r;
      startedAtRef.current = Date.now();
      setElapsedMs(0);
      tickRef.current = setInterval(() => {
        setElapsedMs(Date.now() - startedAtRef.current);
      }, TICK_MS);
      setState('recording');
    } catch (err) {
      Alert.alert(
        'Recording failed',
        err instanceof Error ? err.message : String(err),
      );
      await safelyStop();
    }
  }

  async function safelyStop(): Promise<{ uri: string; durationMs: number } | null> {
    if (tickRef.current) {
      clearInterval(tickRef.current);
      tickRef.current = null;
    }
    const r = recordingRef.current;
    recordingRef.current = null;
    if (!r) return null;
    try {
      await r.stopAndUnloadAsync();
      const uri = r.getURI();
      const status = await r.getStatusAsync().catch(() => null);
      const durationMs = status && 'durationMillis' in status
        ? (status.durationMillis ?? 0)
        : Date.now() - startedAtRef.current;
      if (!uri) return null;
      return { uri, durationMs };
    } catch {
      return null;
    }
  }

  async function onStop() {
    if (state !== 'recording') return;
    setState('finalising');
    setMeter(0);
    const recording = await safelyStop();
    if (!recording) {
      setState('idle');
      Alert.alert('Recording empty', 'Nothing was captured — try again.');
      return;
    }
    const { uri, durationMs } = recording;
    const fileName = `voice-${Date.now()}.m4a`;
    const contentType = 'audio/mp4';
    const durationSec = Math.max(1, Math.round(durationMs / 1000));

    try {
      const net = await NetInfo.fetch();
      const hasIssue = !!issueId;
      // If we don't have an issue id yet (issue-create flow), we MUST queue —
      // there's nowhere to upload to until the parent screen backfills the id.
      if (net.isConnected && hasIssue) {
        await uploadAudioNote({
          projectId,
          issueId: issueId!,
          uri,
          fileName,
          contentType,
          durationSec,
        });
        Alert.alert(
          'Voice note uploaded',
          'Transcript will appear once processed.',
        );
        onNoteCaptured?.({ uri, durationMs, queued: false });
      } else {
        await enqueue('ATTACH_AUDIO', {
          projectId,
          issueId: issueId ?? '__pending__',
          localUri: uri,
          fileName,
          mimeType: contentType,
          durationSec,
          contextTag,
        });
        Alert.alert(
          hasIssue ? 'Saved offline' : 'Voice note queued',
          hasIssue
            ? 'Voice note will upload when network is back. Transcript will appear once processed.'
            : 'Voice note saved with this draft. It uploads after the issue is created. Transcript will appear once processed.',
        );
        onNoteCaptured?.({ uri, durationMs, queued: true });
      }
    } catch (err) {
      // Foreground retries already exhausted — fall back to the queue so the
      // recording is never lost on a flaky link.
      try {
        await enqueue('ATTACH_AUDIO', {
          projectId,
          issueId: issueId ?? '__pending__',
          localUri: uri,
          fileName,
          mimeType: contentType,
          durationSec,
          contextTag,
        });
        Alert.alert(
          'Upload failed — queued for retry',
          err instanceof Error ? err.message : String(err),
        );
        onNoteCaptured?.({ uri, durationMs, queued: true });
      } catch (queueErr) {
        Alert.alert(
          'Save failed',
          queueErr instanceof Error ? queueErr.message : String(queueErr),
        );
      }
    } finally {
      setState('idle');
      setElapsedMs(0);
    }
  }

  function onPress() {
    if (state === 'idle') void onStart();
    else if (state === 'recording') void onStop();
  }

  const seconds = Math.floor(elapsedMs / 1000);
  const mm = String(Math.floor(seconds / 60)).padStart(2, '0');
  const ss = String(seconds % 60).padStart(2, '0');
  const isRec = state === 'recording';
  const isBusy = state === 'finalising';

  return (
    <View style={styles.row}>
      <TouchableOpacity
        style={[styles.btn, isRec && styles.btnActive, (disabled || isBusy) && styles.btnDisabled]}
        onPress={onPress}
        disabled={disabled || isBusy}
        accessibilityRole="button"
        accessibilityLabel={isRec ? 'Stop recording' : 'Start voice dictation'}
        accessibilityState={{ busy: isBusy, selected: isRec }}
      >
        <Text style={styles.icon}>{isRec ? '⏹' : '🎤'}</Text>
      </TouchableOpacity>
      {isRec && (
        <View style={styles.meterWrap}>
          <View style={styles.vuRow}>
            {Array.from({ length: VU_BARS }).map((_, i) => {
              const filled = i / VU_BARS < meter;
              return (
                <View
                  key={i}
                  style={[
                    styles.vuBar,
                    filled ? styles.vuBarFilled : styles.vuBarEmpty,
                  ]}
                />
              );
            })}
          </View>
          <Text style={styles.timer}>{mm}:{ss}</Text>
        </View>
      )}
      {!isRec && !isBusy && (
        <Text style={styles.hint}>Tap mic to dictate — transcript appears once processed.</Text>
      )}
      {isBusy && <Text style={styles.hint}>Saving…</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: theme.spacing.sm,
  },
  btn: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
    justifyContent: 'center',
    alignItems: 'center',
  },
  btnActive: {
    backgroundColor: theme.colors.danger,
    borderColor: theme.colors.danger,
  },
  btnDisabled: { opacity: 0.5 },
  icon: { fontSize: 20 },
  meterWrap: { flex: 1, marginLeft: theme.spacing.sm },
  vuRow: { flexDirection: 'row', alignItems: 'center', height: 18 },
  vuBar: { width: 4, marginRight: 2, borderRadius: 1 },
  vuBarFilled: { height: 16, backgroundColor: theme.colors.danger },
  vuBarEmpty: { height: 6, backgroundColor: theme.colors.border },
  timer: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
  hint: {
    flex: 1,
    marginLeft: theme.spacing.sm,
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
});
