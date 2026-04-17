import { Stack } from "expo-router";
export default function TransmittalsLayout() {
  return <Stack screenOptions={{ headerShown: true }}>
    <Stack.Screen name="index" options={{ title: "Transmittals" }} />
  </Stack>;
}
