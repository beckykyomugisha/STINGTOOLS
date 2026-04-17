import { Stack } from "expo-router";
export default function WorkflowsLayout() {
  return <Stack screenOptions={{ headerShown: true }}>
    <Stack.Screen name="index" options={{ title: "Workflow runs" }} />
  </Stack>;
}
