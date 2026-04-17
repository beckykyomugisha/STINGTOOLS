import { Tabs } from 'expo-router';
import { Text } from 'react-native';
import { useEffect } from 'react';
import { theme } from '@/utils/theme';
import { TenantSwitcher } from '@/components/TenantSwitcher';
import { useTenantStore } from '@/stores/tenantStore';
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

  useEffect(() => {
    // TENANT-SWITCH — one call on tab-layout mount populates the store. The
    // TenantSwitcher component decides whether to render itself based on the
    // resulting count (0/1 = hidden, 2-5 = list, 6+ = search).
    fetchMemberships()
      .then(setMemberships)
      .catch(() => { /* non-fatal — switcher just stays hidden */ });
  }, [setMemberships]);

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
        }}
      />
      <Tabs.Screen
        name="issues"
        options={{
          title: 'Issues',
          tabBarIcon: ({ focused }) => <TabIcon label="⚠" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="documents"
        options={{
          title: 'Documents',
          tabBarIcon: ({ focused }) => <TabIcon label="📄" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="scanner"
        options={{
          title: 'Scanner',
          tabBarIcon: ({ focused }) => <TabIcon label="📷" focused={focused} />,
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          title: 'Settings',
          tabBarIcon: ({ focused }) => <TabIcon label="⚙" focused={focused} />,
        }}
      />
    </Tabs>
  );
}
