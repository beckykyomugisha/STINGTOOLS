import { Stack } from 'expo-router';

export default function HeatmapLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: 'Tag Heatmap' }} />
    </Stack>
  );
}
