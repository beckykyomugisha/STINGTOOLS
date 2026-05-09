// T3-17 — Information Deliverables stack.

import { Stack } from 'expo-router';

export default function DeliverablesLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Deliverables' }} />
      <Stack.Screen name="new" options={{ title: 'New Deliverable' }} />
      <Stack.Screen name="[id]" options={{ title: 'Deliverable' }} />
    </Stack>
  );
}
