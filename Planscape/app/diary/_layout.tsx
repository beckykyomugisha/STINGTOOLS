import { Stack } from 'expo-router';

export default function DiaryLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Site Diary' }} />
      <Stack.Screen name="new" options={{ title: 'New Diary Entry' }} />
      <Stack.Screen name="[id]" options={{ title: 'Diary Entry' }} />
    </Stack>
  );
}
