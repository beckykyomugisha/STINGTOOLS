import { Stack } from "expo-router";

export default function ModelsLayout() {
  return (
    <Stack screenOptions={{ headerShown: true }}>
      <Stack.Screen name="[id]" options={{ title: "Viewer" }} />
    </Stack>
  );
}
