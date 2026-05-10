// Phase 178f — Penetration commissioning sign-off layout.
import { Stack } from 'expo-router';

export default function PenetrationsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Penetrations' }} />
      <Stack.Screen name="signoff" options={{ title: 'Sign-off' }} />
    </Stack>
  );
}
