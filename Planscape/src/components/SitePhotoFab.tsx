// Phase 178 — Persistent floating action button for the Six-Reason photo
// capture workflow. Visible on dashboard, diary, and any in-project screen.
// The button is intentionally lightweight (no API calls on mount) so it can
// be dropped into any screen without coupling.

import { TouchableOpacity, Text, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';

export interface SitePhotoFabProps {
  /** Optional anchor — when set, the capture screen pre-selects "Issue"
   *  and links the resulting photo to this issue rather than auto-creating
   *  a new one. */
  anchorIssueId?: string;
  /** Optional element guid — when set, the capture defaults to "Defect"
   *  and the photo is linked to that element via AnchorElementGuid. */
  anchorElementGuid?: string;
  /** Anchor offset from screen edge, default 24px (theme.spacing.lg). */
  bottom?: number;
  right?: number;
  /** When true, render label next to the icon. Useful on diary list. */
  showLabel?: boolean;
}

export function SitePhotoFab(props: SitePhotoFabProps) {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);
  const bottom = props.bottom ?? theme.spacing.lg;
  const right = props.right ?? theme.spacing.lg;

  if (!projectId) return null;

  function onPress() {
    const params: Record<string, string> = { projectId: projectId! };
    if (props.anchorIssueId) params.anchorIssueId = props.anchorIssueId;
    if (props.anchorElementGuid) params.anchorElementGuid = props.anchorElementGuid;
    router.push({ pathname: '/site-photos/capture', params });
  }

  return (
    <TouchableOpacity
      style={[styles.fab, { bottom, right }, props.showLabel ? styles.fabWide : null]}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel="Capture site photo"
      activeOpacity={0.85}
    >
      <View style={styles.row}>
        <Text style={styles.icon}>📷</Text>
        {props.showLabel ? <Text style={styles.label}>Site Photo</Text> : null}
      </View>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  fab: {
    position: 'absolute',
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: theme.colors.accent,
    justifyContent: 'center',
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.25,
    shadowRadius: 8,
    elevation: 6,
  },
  fabWide: {
    width: 'auto',
    paddingHorizontal: 18,
  },
  row: { flexDirection: 'row', alignItems: 'center' },
  icon: { fontSize: 24 },
  label: {
    color: '#fff',
    fontWeight: '600',
    marginLeft: 8,
    fontSize: theme.fontSize.md,
  },
});
