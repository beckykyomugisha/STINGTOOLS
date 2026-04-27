import { Stack } from 'expo-router';

export default function ConflictsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Sync Conflicts' }} />
    </Stack>
  );
}
