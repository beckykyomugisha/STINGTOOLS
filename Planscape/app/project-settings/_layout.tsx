import { Stack } from 'expo-router';

export default function ProjectSettingsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Project Settings' }} />
      {/* T3-20 — member roster + ACL editor. */}
      <Stack.Screen name="members" options={{ title: 'Project Members' }} />
    </Stack>
  );
}
