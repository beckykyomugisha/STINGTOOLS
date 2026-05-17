import { Stack } from "expo-router";

export default function BoqLayout() {
  return (
    <Stack>
      <Stack.Screen name="index" options={{ title: "Bill of Quantities" }} />
      <Stack.Screen name="[id]" options={{ title: "BOQ Document" }} />
    </Stack>
  );
}
