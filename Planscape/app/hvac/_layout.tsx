// Phase 188 (Tier 3) — mobile HVAC sub-tab layout.
import { Stack } from 'expo-router';

export default function HvacLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index"     options={{ title: 'HVAC' }} />
      <Stack.Screen name="snapshots" options={{ title: 'HVAC Snapshots' }} />
      <Stack.Screen name="drift"     options={{ title: 'Drift Detail' }} />
    </Stack>
  );
}
