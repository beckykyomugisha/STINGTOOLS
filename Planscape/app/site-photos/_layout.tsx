// Phase 178 — Site photo route group. Wraps capture / review / gallery.
// Headers stay visible (unlike (tabs) which hides them) so the back button
// works naturally on every screen in this stack.

import { Stack } from 'expo-router';
import { theme } from '@/utils/theme';

export default function SitePhotosLayout() {
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: theme.colors.primary },
        headerTintColor: theme.colors.surface,
        headerTitleStyle: { fontWeight: '600' },
        contentStyle: { backgroundColor: theme.colors.background },
      }}
    >
      <Stack.Screen name="capture" options={{ title: 'Capture Photo' }} />
      <Stack.Screen name="review" options={{ title: 'Review Photos' }} />
      <Stack.Screen name="gallery" options={{ title: 'Project Gallery' }} />
      {/* T3-4 — daily digest preview, opened from the gallery header. */}
      <Stack.Screen name="digest" options={{ title: "Today's Digest" }} />
      {/* Phase 179 — albums + checklists + annotations. */}
      <Stack.Screen name="albums" options={{ title: 'Albums' }} />
      <Stack.Screen name="album-detail" options={{ title: 'Album' }} />
      <Stack.Screen name="checklists" options={{ title: 'Photo Checklists' }} />
      <Stack.Screen name="checklist-detail" options={{ title: 'Checklist' }} />
      <Stack.Screen name="annotate" options={{ title: 'Annotate Photo' }} />
    </Stack>
  );
}
