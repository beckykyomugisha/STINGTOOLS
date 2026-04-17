import { Stack } from "expo-router";
export default function WarningsLayout() {
  return <Stack screenOptions={{ headerShown: true }}>
    <Stack.Screen name="index" options={{ title: "Warnings" }} />
  </Stack>;
}
