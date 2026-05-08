import { Stack } from 'expo-router';

export default function InboxLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'My Actions' }} />
      <Stack.Screen name="approvals" options={{ title: 'Document Approvals' }} />
    </Stack>
  );
}
