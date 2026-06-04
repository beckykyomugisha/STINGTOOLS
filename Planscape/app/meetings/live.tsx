// app/meetings/live.tsx — WS3e — mobile live meeting A/V (LiveKit).
//
// ⚠️ NATIVE BUILD REQUIRED. @livekit/react-native + @livekit/react-native-webrtc
// ship native modules — this screen does NOT run in plain Expo Go. Build an Expo
// dev client (`npx expo run:ios` / `run:android`, or an EAS dev build) to test.
//
// Mirrors the web flow (livekit-av.js): fetch a LiveKit token → connect to the room
// → publish camera + mic → render participant video tiles → mic/camera/leave +
// presenter screen-share. The SignalR MeetingHub still owns model co-presence + the
// active surface; this screen follows SurfaceChanged to foreground the screen-share.
//
// Route: /meetings/live?project=<pid>&session=<sid>&presenter=<0|1>

import { useEffect, useState, useCallback } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ActivityIndicator, ScrollView, Platform } from 'react-native';
import { useLocalSearchParams, useRouter, Stack } from 'expo-router';
import * as signalR from '@microsoft/signalr';
import {
  LiveKitRoom, useTracks, VideoTrack, registerGlobals, AudioSession,
  useLocalParticipant,
} from '@livekit/react-native';
import { Track } from 'livekit-client';
import { getLiveKitToken } from '@/api/endpoints';
import { getToken } from '@/api/client';

// Register the WebRTC globals once on module load (required before any room use).
registerGlobals();

const API_BASE = (process.env.EXPO_PUBLIC_API_BASE || process.env.EXPO_PUBLIC_PLANSCAPE_API || 'http://localhost:5000').replace(/\/$/, '');

export default function LiveMeetingScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ project?: string; session?: string }>();
  const projectId = String(params.project || '');
  const sessionId = String(params.session || '');

  const [conn, setConn] = useState<{ url: string; token: string; isPresenter: boolean } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [surface, setSurface] = useState<string>('model');

  // Start the audio session for the duration of the call.
  useEffect(() => {
    let active = true;
    AudioSession.startAudioSession().catch(() => {});
    if (projectId && sessionId) {
      getLiveKitToken(projectId, sessionId, {})
        .then((t) => { if (active) setConn({ url: t.url, token: t.token, isPresenter: t.isPresenter }); })
        .catch((e: any) => { if (active) setError(e?.message === '501' ? 'A/V is not enabled for this server.' : 'Could not join A/V.'); });
    }
    return () => { active = false; AudioSession.stopAudioSession().catch(() => {}); };
  }, [projectId, sessionId]);

  // Follow the presenter's active surface over the MeetingHub (co-presence plane).
  useEffect(() => {
    if (!projectId || !sessionId) return;
    let hub: signalR.HubConnection | null = null;
    (async () => {
      const token = await getToken().catch(() => '');
      hub = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE}/hubs/meeting?access_token=${encodeURIComponent(token || '')}`)
        .withAutomaticReconnect()
        .build();
      hub.on('SurfaceChanged', (s: { surface?: string }) => setSurface(s?.surface || 'model'));
      try { await hub.start(); await hub.invoke('JoinSession', sessionId, 'mobile'); } catch { /* degrade to A/V only */ }
    })();
    return () => { try { hub?.stop(); } catch { /* noop */ } };
  }, [projectId, sessionId]);

  if (error) {
    return (
      <Centered>
        <Text style={styles.err}>{error}</Text>
        <TouchableOpacity style={styles.btn} onPress={() => router.back()}><Text style={styles.btnTxt}>Back</Text></TouchableOpacity>
      </Centered>
    );
  }
  if (!conn) {
    return <Centered><ActivityIndicator color="#3b82f6" /><Text style={styles.dim}>Joining A/V…</Text></Centered>;
  }

  return (
    <View style={styles.root}>
      <Stack.Screen options={{ title: 'Live meeting', headerShown: true }} />
      <LiveKitRoom serverUrl={conn.url} token={conn.token} connect audio video options={{ adaptiveStream: true }}>
        <RoomView surface={surface} onLeave={() => router.back()} />
      </LiveKitRoom>
    </View>
  );
}

function RoomView({ surface, onLeave }: { surface: string; onLeave: () => void }) {
  // Camera + screen-share tracks for every participant (local + remote).
  const tracks = useTracks([Track.Source.Camera, Track.Source.ScreenShare], { onlySubscribed: false });
  const camera = tracks.filter((t) => t.source === Track.Source.Camera && t.publication?.track);
  const screen = tracks.find((t) => t.source === Track.Source.ScreenShare && t.publication?.track);
  const showScreen = surface === 'screen' && !!screen;

  return (
    <View style={styles.room}>
      {showScreen ? (
        <VideoTrack trackRef={screen} style={styles.screen} objectFit="contain" />
      ) : (
        <ScrollView contentContainerStyle={styles.grid}>
          {camera.map((t) => (
            <VideoTrack key={t.participant.sid + t.source} trackRef={t} style={styles.tile} objectFit="cover" />
          ))}
          {camera.length === 0 && <Text style={styles.dim}>No camera streams yet…</Text>}
        </ScrollView>
      )}
      <Controls onLeave={onLeave} />
    </View>
  );
}

function Controls({ onLeave }: { onLeave: () => void }) {
  const { localParticipant, isCameraEnabled, isMicrophoneEnabled, isScreenShareEnabled } = useLocalParticipant();
  const toggleMic = useCallback(() => { localParticipant?.setMicrophoneEnabled(!isMicrophoneEnabled); }, [localParticipant, isMicrophoneEnabled]);
  const toggleCam = useCallback(() => { localParticipant?.setCameraEnabled(!isCameraEnabled); }, [localParticipant, isCameraEnabled]);
  const toggleScreen = useCallback(() => { localParticipant?.setScreenShareEnabled(!isScreenShareEnabled); }, [localParticipant, isScreenShareEnabled]);
  return (
    <View style={styles.controls}>
      <Ctrl label={isMicrophoneEnabled ? '🎤' : '🔇'} on={isMicrophoneEnabled} onPress={toggleMic} />
      <Ctrl label={isCameraEnabled ? '📹' : '🚫'} on={isCameraEnabled} onPress={toggleCam} />
      <Ctrl label="🖥" on={isScreenShareEnabled} onPress={toggleScreen} />
      <Ctrl label="✖" on={false} danger onPress={onLeave} />
    </View>
  );
}

function Ctrl({ label, on, danger, onPress }: { label: string; on: boolean; danger?: boolean; onPress: () => void }) {
  return (
    <TouchableOpacity onPress={onPress}
      style={[styles.ctrl, on && styles.ctrlOn, danger && styles.ctrlDanger]}>
      <Text style={styles.ctrlTxt}>{label}</Text>
    </TouchableOpacity>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return <View style={[styles.root, styles.center]}>{children}</View>;
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: '#0e1014' },
  center: { alignItems: 'center', justifyContent: 'center', gap: 12 },
  room: { flex: 1 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, padding: 8, justifyContent: 'center' },
  tile: { width: 168, height: 126, borderRadius: 8, backgroundColor: '#11141a' },
  screen: { flex: 1, backgroundColor: '#000', margin: 8, borderRadius: 8 },
  controls: { flexDirection: 'row', gap: 12, justifyContent: 'center', paddingVertical: 14, backgroundColor: 'rgba(0,0,0,0.5)' },
  ctrl: { width: 48, height: 48, borderRadius: 24, alignItems: 'center', justifyContent: 'center', backgroundColor: 'rgba(255,255,255,0.14)' },
  ctrlOn: { backgroundColor: 'rgba(55,194,114,0.85)' },
  ctrlDanger: { backgroundColor: 'rgba(208,80,80,0.85)' },
  ctrlTxt: { fontSize: 18 },
  dim: { color: '#9aa3b2', fontSize: 13 },
  err: { color: '#fca5a5', fontSize: 14, textAlign: 'center', paddingHorizontal: 24 },
  btn: { backgroundColor: '#3b82f6', paddingHorizontal: 20, paddingVertical: 10, borderRadius: 8 },
  btnTxt: { color: '#fff', fontWeight: '600' },
});
