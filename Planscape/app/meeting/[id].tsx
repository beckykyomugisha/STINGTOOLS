// app/meeting/[id].tsx — P1 meeting-invite deep-link entry.
//
// Handles planscape://meeting/{id}?project={pid} (from a meeting-invite push,
// email, or web fallback opened on the phone). If the user is signed in we go
// straight to the live meeting; if not we stash the target and send them through
// login / set-password, after which they land back in the meeting (the
// "(password)" flow). The live screen resolves the meetingId to a live session
// and auto-joins A/V.

import { useEffect } from 'react';
import { View, ActivityIndicator, StyleSheet } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { getToken } from '@/api/client';
import { setPendingMeeting, meetingLivePath } from '@/services/pendingMeeting';

export default function MeetingDeepLink() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string; project?: string }>();
  const meetingId = String(params.id || '');
  const projectId = String(params.project || '');

  useEffect(() => {
    let active = true;
    (async () => {
      if (!meetingId) { router.replace('/(tabs)'); return; }
      const token = await getToken().catch(() => null);
      if (!active) return;
      if (token) {
        // Signed in → straight into the meeting (membership enforced server-side).
        router.replace(meetingLivePath(projectId, meetingId));
      } else {
        // Not activated / signed out → remember the target, go authenticate.
        await setPendingMeeting({ projectId, meetingId });
        router.replace('/login');
      }
    })();
    return () => { active = false; };
  }, [meetingId, projectId]);

  return (
    <View style={styles.root}>
      <ActivityIndicator color="#3b82f6" />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#0e1014' },
});
