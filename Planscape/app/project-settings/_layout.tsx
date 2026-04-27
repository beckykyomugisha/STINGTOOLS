import { Stack } from 'expo-router';

export default function ProjectSettingsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Project Settings' }} />
    </Stack>
  );
}
