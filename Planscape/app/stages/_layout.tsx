import { Stack } from 'expo-router';

export default function StagesLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Stage Gates' }} />
      <Stack.Screen name="deliverables" options={{ title: 'Deliverables' }} />
    </Stack>
  );
}
