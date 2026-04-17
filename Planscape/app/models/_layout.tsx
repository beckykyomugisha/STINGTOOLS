import { Stack } from "expo-router";

export default function ModelsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="index" options={{ title: "3D models" }} />
      <Stack.Screen name="[id]" options={{ title: "Viewer" }} />
    </Stack>
  );
}
