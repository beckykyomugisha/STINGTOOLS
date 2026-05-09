// Healthcare Pack H-21 — mobile sub-tab layout.
import { Stack } from 'expo-router';

export default function HealthcareLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Healthcare' }} />
      <Stack.Screen name="mgas-checklist" options={{ title: 'MGPS Verification' }} />
      <Stack.Screen name="pressure-live" options={{ title: 'Pressure (live)' }} />
      <Stack.Screen name="water-flush" options={{ title: 'Water Flushing' }} />
      <Stack.Screen name="anti-ligature-audit" options={{ title: 'Anti-Ligature Audit' }} />
      <Stack.Screen name="rds-viewer" options={{ title: 'Room Data Sheet' }} />
    </Stack>
  );
}
