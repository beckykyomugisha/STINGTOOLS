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
  const docsUnread = useNotificationStore((s) => s.byFeature.documents ?? 0);
  const dashUnread = useNotificationStore((s) => s.byFeature.dashboard ?? 0);

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
      <Tabs.Screen
        name="index"
        options={{
          title: 'Dashboard',
          tabBarIcon: ({ focused }) => <TabIcon label="📊" focused={focused} />,
          // Phase 96 — compliance/SLA pushes bump the dashboard badge.
          tabBarBadge: dashUnread > 0 ? (dashUnread > 99 ? '99+' : dashUnread) : undefined,
          tabBarBadgeStyle: { backgroundColor: theme.colors.danger, color: '#fff', fontSize: 10 },
        }}
      />
      <Tabs.Screen
        name="issues"
        options={{
          title: 'Issues',
          tabBarIcon: ({ focused }) => <TabIcon label="⚠" focused={focused} />,
          tabBarBadge: issuesUnread > 0 ? (issuesUnread > 99 ? '99+' : issuesUnread) : undefined,
          tabBarBadgeStyle: { backgroundColor: theme.colors.danger, color: '#fff', fontSize: 10 },
        }}
      />
      <Tabs.Screen
        name="documents"
        options={{
          title: 'Documents',
          tabBarIcon: ({ focused }) => <TabIcon label="📄" focused={focused} />,
          tabBarBadge: docsUnread > 0 ? (docsUnread > 99 ? '99+' : docsUnread) : undefined,
          tabBarBadgeStyle: { backgroundColor: theme.colors.danger, color: '#fff', fontSize: 10 },
        }}
      />
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
      {/* Feature gap 2 — Cost Dashboard */}
      <Tabs.Screen
        name="cost-dashboard"
        options={{
          title: 'Costs',
          tabBarIcon: ({ focused }) => <TabIcon label="📈" focused={focused} />,
        }}
      />
      {/* GAP-C — Schedule / P6 Live Link */}
      <Tabs.Screen
        name="schedule"
        options={{
          title: 'Schedule',
          tabBarIcon: ({ focused }) => <TabIcon label="📅" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          title: 'Settings',
          tabBarIcon: ({ focused }) => <TabIcon label="⚙" focused={focused} />,
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
