// Placeholder — the Photos tab uses href: '/site-photos/gallery' so Expo
// Router never renders this file, but the file must exist for the Tabs.Screen
// name="site-photos" to be recognised by the file-based router.
import { Redirect } from 'expo-router';
export default function SitePhotosPlaceholder() {
  return <Redirect href="/site-photos/gallery" />;
}
