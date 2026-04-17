import { Stack } from "expo-router";
export default function MeetingsLayout() {
  return <Stack screenOptions={{ headerShown: true }}>
    <Stack.Screen name="index" options={{ title: "Meetings" }} />
  </Stack>;
}
