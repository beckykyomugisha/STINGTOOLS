import { Stack } from "expo-router";

export default function ModelChecksLayout() {
  return (
    <Stack>
      <Stack.Screen name="index" options={{ title: "Model Checks" }} />
      <Stack.Screen name="[id]" options={{ title: "Check Run" }} />
    </Stack>
  );
}
