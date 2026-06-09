// P10 — Invitation acceptance landing.
//
// Entry points:
//   - Universal link: https://planscape.yourco.com/accept-invitation?token=…&email=…
//   - Expo scheme:    planscape://accept-invitation?token=…&email=…
//
// The server sends these links in invite emails. The screen captures the
// token + email, routes the user to a "set password" form, then signs them
// in on the token they just created.

import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  StyleSheet,
  Alert,
} from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { apiFetch, setTokens } from "@/api/client";
import { consumePendingMeeting, meetingLivePath } from "@/services/pendingMeeting";

interface AcceptResponse {
  accessToken: string;
  refreshToken: string;
  userName: string;
}

export default function AcceptInvitationScreen() {
  const router = useRouter();
  const { token, email: emailParam } = useLocalSearchParams<{ token?: string; email?: string }>();
  const [email, setEmail] = useState(emailParam ?? "");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleAccept() {
    setError(null);
    if (!token) { setError("Missing invitation token."); return; }
    if (password.length < 8) { setError("Password must be at least 8 characters."); return; }
    if (password !== confirm) { setError("Passwords don't match."); return; }

    setSubmitting(true);
    try {
      // Server exchanges the invitation token for an auth token + user.
      const resp = await apiFetch<AcceptResponse>("/api/auth/accept-invitation", {
        method: "POST",
        body: JSON.stringify({ token, email, password }),
      });
      await setTokens(resp.accessToken, resp.refreshToken);
      Alert.alert("Welcome!", `Signed in as ${resp.userName}`);
      // P1 — if activation was triggered by a meeting deep link, jump into it.
      const pending = await consumePendingMeeting();
      if (pending) {
        router.replace(meetingLivePath(pending.projectId, pending.meetingId));
      } else {
        router.replace("/(tabs)");
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Invitation could not be accepted.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Accept invitation</Text>
      <Text style={styles.sub}>Set a password to finish setting up your account.</Text>

      <Text style={styles.label}>Email</Text>
      <TextInput
        value={email}
        onChangeText={setEmail}
        autoCapitalize="none"
        keyboardType="email-address"
        style={styles.input}
        editable={!emailParam}
        placeholder="you@company.com"
      />

      <Text style={styles.label}>New password</Text>
      <TextInput
        value={password}
        onChangeText={setPassword}
        secureTextEntry
        style={styles.input}
        placeholder="at least 8 characters"
      />

      <Text style={styles.label}>Confirm password</Text>
      <TextInput
        value={confirm}
        onChangeText={setConfirm}
        secureTextEntry
        style={styles.input}
        placeholder="repeat the password"
      />

      {error && <Text style={styles.error}>{error}</Text>}

      <TouchableOpacity
        onPress={handleAccept}
        disabled={submitting}
        style={[styles.button, submitting && { opacity: 0.6 }]}
      >
        {submitting
          ? <ActivityIndicator color="#fff" />
          : <Text style={styles.buttonText}>Accept &amp; sign in</Text>}
      </TouchableOpacity>

      <Text style={styles.hint}>
        If you weren't expecting this, you can safely close this screen.
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 24, justifyContent: "center", backgroundColor: "#fff" },
  title: { fontSize: 24, fontWeight: "700", color: "#1A237E", marginBottom: 4 },
  sub: { fontSize: 14, color: "#666", marginBottom: 24 },
  label: { fontSize: 12, color: "#555", marginBottom: 4, marginTop: 12, textTransform: "uppercase", letterSpacing: 0.5 },
  input: { borderWidth: 1, borderColor: "#ddd", borderRadius: 8, padding: 12, fontSize: 15 },
  error: { color: "#d32f2f", marginTop: 12, fontSize: 13 },
  button: { marginTop: 24, backgroundColor: "#E8912D", paddingVertical: 14, borderRadius: 8, alignItems: "center" },
  buttonText: { color: "#fff", fontWeight: "700", fontSize: 15 },
  hint: { marginTop: 24, fontSize: 12, color: "#999", textAlign: "center" },
});
