'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { Room, RoomEvent, Track, type Participant, type RemoteTrack, type LocalTrack } from 'livekit-client';
import { AppShell } from '@/components/AppShell';
import { startLiveSession, getLiveKitToken } from '@/lib/data';
import { API_BASE, ApiError } from '@/lib/api';
import { useAuth } from '@/lib/auth';

export const dynamic = 'force-dynamic';

const VIEWER_URL = process.env.NEXT_PUBLIC_VIEWER_URL || `${API_BASE}/viewer.html`;

export default function LiveMeetingPage() {
  const params = useParams<{ id: string; meetingId: string }>();
  const projectId = params.id;
  const meetingId = params.meetingId;
  const router = useRouter();
  const { user } = useAuth();

  const tilesRef = useRef<HTMLDivElement>(null);
  const roomRef = useRef<Room | null>(null);

  const [sessionId, setSessionId] = useState<string | null>(null);
  const [modelId, setModelId] = useState<string | null>(null);
  const [status, setStatus] = useState('Connecting…');
  const [avDisabled, setAvDisabled] = useState<string | null>(null); // set when LiveKit isn't configured
  const [micOn, setMicOn] = useState(true);
  const [camOn, setCamOn] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // ── tile DOM helpers (LiveKit tracks attach to media elements imperatively) ──
  const addVideoTile = useCallback((track: RemoteTrack | LocalTrack, label: string, muted: boolean) => {
    const host = tilesRef.current;
    if (!host) return;
    const wrap = document.createElement('div');
    wrap.dataset.sid = track.sid ?? `${label}-${Math.random()}`;
    wrap.className = 'relative overflow-hidden rounded-lg bg-black ring-1 ring-slate-700';
    const el = track.attach() as HTMLVideoElement;
    el.muted = muted;
    el.className = 'h-full w-full object-cover';
    el.style.aspectRatio = '16 / 9';
    const tag = document.createElement('span');
    tag.textContent = label;
    tag.className = 'absolute bottom-1 left-1 rounded bg-black/60 px-1.5 py-0.5 text-[10px] text-white';
    wrap.appendChild(el);
    wrap.appendChild(tag);
    host.appendChild(wrap);
  }, []);

  const removeTile = useCallback((sid?: string) => {
    if (!sid || !tilesRef.current) return;
    tilesRef.current.querySelector(`[data-sid="${CSS.escape(sid)}"]`)?.remove();
  }, []);

  useEffect(() => {
    let disposed = false;
    let room: Room | null = null;

    (async () => {
      try {
        const session = await startLiveSession(projectId, meetingId, { displayName: user?.email });
        if (disposed) return;
        setSessionId(session.sessionId);
        setModelId(session.modelId ?? null);

        let conn: { token: string; url: string } | null = null;
        try {
          const t = await getLiveKitToken(projectId, session.sessionId, user?.email);
          conn = { token: t.token, url: t.url };
        } catch (e) {
          // 501 = LiveKit not configured → keep the shared-model co-presence,
          // just without audio/video.
          if (e instanceof ApiError && e.status === 501) {
            setAvDisabled('Video is not enabled on this server — shared 3D model only.');
            setStatus('Shared model (no A/V)');
            return;
          }
          throw e;
        }
        if (disposed || !conn) return;

        room = new Room({ adaptiveStream: true, dynacast: true });
        roomRef.current = room;

        room
          .on(RoomEvent.TrackSubscribed, (track, _pub, participant: Participant) => {
            if (track.kind === Track.Kind.Video) addVideoTile(track, participant.identity || 'Guest', false);
            else track.attach(); // remote audio → autoplay
          })
          .on(RoomEvent.TrackUnsubscribed, (track) => {
            track.detach().forEach((el) => el.remove());
            removeTile(track.sid);
          })
          .on(RoomEvent.LocalTrackPublished, (pub) => {
            if (pub.track && pub.kind === Track.Kind.Video) addVideoTile(pub.track, 'You', true);
          })
          .on(RoomEvent.LocalTrackUnpublished, (pub) => removeTile(pub.trackSid))
          .on(RoomEvent.Disconnected, () => {
            if (!disposed) setStatus('Disconnected');
          });

        await room.connect(conn.url, conn.token);
        if (disposed) return;
        setStatus('Live');
        await room.localParticipant.setMicrophoneEnabled(true);
        await room.localParticipant.setCameraEnabled(true);
      } catch (e) {
        if (!disposed) setError(e instanceof Error ? e.message : 'Failed to join the live session');
      }
    })();

    return () => {
      disposed = true;
      roomRef.current = null;
      void room?.disconnect();
    };
  }, [projectId, meetingId, user?.email, addVideoTile, removeTile]);

  async function toggleMic() {
    const lp = roomRef.current?.localParticipant;
    if (!lp) return;
    const next = !micOn;
    await lp.setMicrophoneEnabled(next);
    setMicOn(next);
  }
  async function toggleCam() {
    const lp = roomRef.current?.localParticipant;
    if (!lp) return;
    const next = !camOn;
    await lp.setCameraEnabled(next);
    setCamOn(next);
  }
  function leave() {
    void roomRef.current?.disconnect();
    router.push(`/projects/${projectId}/meetings/${meetingId}`);
  }

  // Viewer iframe co-presence (SignalR meeting-sync, independent of LiveKit):
  // ?meeting=<sessionId> activates camera-follow / roster / shared section etc.
  const viewerSrc = sessionId
    ? `${VIEWER_URL}?project=${projectId}&meeting=${sessionId}${modelId ? `&model=${modelId}` : ''}`
    : null;

  return (
    <AppShell>
      <div className="mb-3 flex items-center justify-between gap-3">
        <Link href={`/projects/${projectId}/meetings/${meetingId}`} className="text-sm text-slate-400 hover:underline">
          ← Meeting
        </Link>
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-500">{status}</span>
          {!avDisabled && (
            <>
              <button
                onClick={toggleMic}
                className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-50"
              >
                {micOn ? 'Mute' : 'Unmute'}
              </button>
              <button
                onClick={toggleCam}
                className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-50"
              >
                {camOn ? 'Stop video' : 'Start video'}
              </button>
            </>
          )}
          <button onClick={leave} className="rounded bg-red-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-red-700">
            Leave
          </button>
        </div>
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {avDisabled && <p className="mb-3 rounded bg-amber-50 px-3 py-2 text-sm text-amber-700">{avDisabled}</p>}

      {/* Participant video tiles (empty when A/V disabled) */}
      <div ref={tilesRef} className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4" />

      {/* Shared 3D model with live co-presence */}
      <div className="overflow-hidden rounded-lg ring-1 ring-slate-200" style={{ height: '60vh' }}>
        {viewerSrc ? (
          <iframe src={viewerSrc} title="Shared model" className="h-full w-full border-0" allow="fullscreen" />
        ) : (
          <div className="grid h-full place-items-center text-slate-400">Starting session…</div>
        )}
      </div>
    </AppShell>
  );
}
