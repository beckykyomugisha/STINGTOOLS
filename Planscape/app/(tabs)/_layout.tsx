import { Tabs } from 'expo-router';
import { Text } from 'react-native';
import { useEffect } from 'react';
import { theme } from '@/utils/theme';
import { TenantSwitcher } from '@/components/TenantSwitcher';
import { useTenantStore } from '@/stores/tenantStore';
import { useNotificationStore } from '@/stores/notificationStore';
import { fetchMemberships } from '@/api/tenants';

function TabIcon({ label, focused }: { label: string; focused: boolean }) {
  return (
    <Text
      style={{
        fontSize: 20,
        color: focused ? theme.colors.accent : theme.colors.textSecondary,
      }}
    >
      {label}
    </Text>
  );
}

export default function TabLayout() {
  const setMemberships = useTenantStore((s) => s.setMemberships);
  const hydrateNotifications = useNotificationStore((s) => s.hydrate);

  // Phase 96 — read each feature's unread count directly so the badge numbers
  // update live when pushes arrive / are tapped. Using separate selectors
  // keeps re-renders narrow (a dashboard-only push won't re-render issues).
  const issuesUnread = useNotificationStore((s) => s.byFeature.issues ?? 0);

  useEffect(() => {
    // TENANT-SWITCH — one call on tab-layout mount populates the store. The
    // TenantSwitcher component decides whether to render itself based on the
    // resulting count (0/1 = hidden, 2-5 = list, 6+ = search).
    fetchMemberships()
      .then(setMemberships)
      .catch(() => { /* non-fatal — switcher just stays hidden */ });
    // Phase 96 — hydrate persisted notification counts so the badge survives
    // cold start.
    hydrateNotifications();
  }, [setMemberships, hydrateNotifications]);

  return (
    <Tabs
      screenOptions={{
        headerStyle: { backgroundColor: theme.colors.primary },
        headerTintColor: theme.colors.surface,
        headerTitleStyle: { fontWeight: '600' },
        headerRight: () => <TenantSwitcher />,
        tabBarActiveTintColor: theme.colors.accent,
        tabBarInactiveTintColor: theme.colors.textSecondary,
        tabBarStyle: {
          backgroundColor: theme.colors.surface,
          borderTopColor: theme.colors.border,
        },
      }}
    >
      {/*
        Tab 1 — Projects home. Points to app/projects/ (the project list/grid).
        The (tabs)/index.tsx file contains the active-project dashboard that
        users land on after selecting a project. The tab bar uses href to link
        directly to /projects so users always see the project list first; after
        picking a project and navigating to the dashboard, the tab bar item
        remains selected because /projects/[id] is a child of the projects route.
      */}
      <Tabs.Screen
        name="index"
        options={{
          title: 'Projects',
          href: '/projects',
          tabBarIcon: ({ focused }) => <TabIcon label="🏗" focused={focused} />,
        }}
      />

      {/*
        Tab 2 — Issues. The most-used feature in the field; deserves a dedicated
        tab so coordinators can jump directly without opening a project first.
        Badge driven by push notifications (Phase 96).
      */}
      <Tabs.Screen
        name="issues"
        options={{
          title: 'Issues',
          tabBarIcon: ({ focused }) => <TabIcon label="⚠" focused={focused} />,
          tabBarBadge: issuesUnread > 0 ? (issuesUnread > 99 ? '99+' : issuesUnread) : undefined,
          tabBarBadgeStyle: { backgroundColor: theme.colors.danger, color: '#fff', fontSize: 10 },
        }}
      />

      {/*
        Tab 3 — QR / barcode scanner. On-site entry point for element lookup,
        issue pre-fill from a scanned asset tag, or punchlist check-in.
      */}
      <Tabs.Screen
        name="models"
        options={{
          title: 'Models',
          tabBarIcon: ({ focused }) => <TabIcon label="🧊" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="scanner"
        options={{
          title: 'Scanner',
          tabBarIcon: ({ focused }) => <TabIcon label="📷" focused={focused} />,
        }}
      />

      {/* My Actions inbox */}
      <Tabs.Screen
        name="myactions"
        options={{
          title: 'My Actions',
          href: '/inbox',
          tabBarIcon: ({ focused }) => <TabIcon label="📋" focused={focused} />,
        }}
      />

      {/* Hidden screens — routable but absent from tab bar */}
      <Tabs.Screen
        name="documents"
        options={{
          href: null,
          title: 'Documents',
        }}
      />
      <Tabs.Screen
        name="models"
        options={{
          href: null,
          title: 'Models',
        }}
      />
      {/* Gap H — coordinate alignment management screen */}
      <Tabs.Screen
        name="alignment"
        options={{
          title: 'Alignment',
          href: '/alignment' as any,
          tabBarIcon: ({ focused }) => <TabIcon label="🔧" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          href: null,
          title: 'Settings',
        }}
      />
      <Tabs.Screen
        name="ifc"
        options={{
          title: 'IFC',
          tabBarIcon: ({ focused }) => <TabIcon label="🗄" focused={focused} />,
        }}
      />
      {/*
        Phase 94 — issue-detail is routable via router.push('/issue-detail?id=<id>')
        but hidden from the bottom tab bar (`href: null`). Tab bar stays visible
        while coordinators drill into an issue so they can bounce to Issues or
        Scanner without popping the stack.
      */}
      <Tabs.Screen
        name="issue-detail"
        options={{
          href: null,
          title: 'Issue',
        }}
      />
      {/* Phase 178 — Photos tab links to the site-photos gallery stack.
          The tab navigates via href so the gallery lives outside (tabs)/
          but is still reachable from the bottom bar. */}
      <Tabs.Screen
        name="site-photos"
        options={{
          title: 'Photos',
          href: '/site-photos/gallery' as any,
          tabBarIcon: ({ focused }) => <TabIcon label="📸" focused={focused} />,
        }}
      />
      {/*
        Phase 94 — issue-detail is routable via router.push('/issue-detail?id=<id>')
        but hidden from the bottom tab bar (`href: null`). Tab bar stays visible
        while coordinators drill into an issue so they can bounce to Dashboard or
        Scanner without popping the stack.
      */}
      <Tabs.Screen
        name="issue-detail"
        options={{
          href: null,
          title: 'Issue',
        }}
      />
    </Tabs>
  );
}
