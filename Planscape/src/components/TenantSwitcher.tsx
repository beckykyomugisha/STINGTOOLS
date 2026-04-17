// TENANT-SWITCH — Adaptive tenant switcher.
//
//   0 memberships → returns null (UI is hidden)
//   1 membership  → badge with tenant name, non-interactive
//   2-5           → badge opens an inline modal list
//   6+            → badge opens a searchable modal

import { useState } from "react";
import {
  View,
  Text,
  Modal,
  TouchableOpacity,
  FlatList,
  TextInput,
  ActivityIndicator,
  Alert,
} from "react-native";
import { useTenantStore } from "@/stores/tenantStore";
import { switchTenant } from "@/api/tenants";
import { pendingCount, clearQueue } from "@/utils/offlineQueue";
import { t } from "@/i18n";

export function TenantSwitcher() {
  const presentation = useTenantStore((s) => s.presentation());
  const memberships = useTenantStore((s) => s.memberships);
  const currentId = useTenantStore((s) => s.currentTenantId);
  const setCurrent = useTenantStore((s) => s.setCurrentTenant);

  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [switching, setSwitching] = useState(false);

  if (presentation === "hidden") return null;

  const current = memberships.find((m) => m.tenantId === currentId);
  const filtered = search
    ? memberships.filter((m) =>
        m.tenantName.toLowerCase().includes(search.toLowerCase())
      )
    : memberships;

  async function handlePick(tenantId: string) {
    if (tenantId === currentId) {
      setOpen(false);
      return;
    }
    // Mid-switch safety (decision 3.3) — drain / warn on queued actions.
    const pending = await pendingCount();
    if (pending > 0) {
      const proceed = await new Promise<boolean>((resolve) => {
        Alert.alert(
          t("common.offline"),
          `${pending} actions still queued for the current organisation. Switch anyway? (pending actions will be cleared)`,
          [
            { text: t("common.cancel"), onPress: () => resolve(false), style: "cancel" },
            { text: t("common.save"), onPress: () => resolve(true) },
          ]
        );
      });
      if (!proceed) return;
    }

    setSwitching(true);
    try {
      await switchTenant(tenantId);
      setCurrent(tenantId);
      // Tenant-scoped caches: clear the queue so we don't replay actions
      // belonging to the previous organisation against the new one. Saved
      // filters are keyed by projectId so they refresh naturally when the
      // project list reloads.
      await clearQueue();
      setOpen(false);
    } catch (err) {
      Alert.alert("Switch failed", String(err));
    } finally {
      setSwitching(false);
    }
  }

  return (
    <>
      <TouchableOpacity
        onPress={() => memberships.length > 1 && setOpen(true)}
        disabled={memberships.length <= 1}
        style={{
          paddingHorizontal: 10,
          paddingVertical: 6,
          borderRadius: 14,
          backgroundColor: "rgba(255,255,255,0.15)",
          flexDirection: "row",
          alignItems: "center",
          maxWidth: 180,
        }}
      >
        <Text
          numberOfLines={1}
          style={{ color: "#fff", fontSize: 13, fontWeight: "600" }}
        >
          {current?.tenantName ?? "…"}
        </Text>
        {memberships.length > 1 && (
          <Text style={{ color: "#fff", marginLeft: 6, fontSize: 13 }}>▾</Text>
        )}
      </TouchableOpacity>

      <Modal visible={open} transparent animationType="slide" onRequestClose={() => setOpen(false)}>
        <View style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.5)", justifyContent: "flex-end" }}>
          <View style={{ backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16, padding: 20, maxHeight: "80%" }}>
            <Text style={{ fontSize: 18, fontWeight: "700", marginBottom: 16 }}>
              Switch organisation
            </Text>

            {presentation === "search" && (
              <TextInput
                value={search}
                onChangeText={setSearch}
                placeholder="Search organisations"
                autoFocus
                style={{
                  borderWidth: 1,
                  borderColor: "#ddd",
                  borderRadius: 8,
                  padding: 10,
                  marginBottom: 12,
                  fontSize: 15,
                }}
              />
            )}

            {switching ? (
              <ActivityIndicator size="large" style={{ padding: 32 }} />
            ) : (
              <FlatList
                data={filtered}
                keyExtractor={(m) => m.tenantId}
                ItemSeparatorComponent={() => <View style={{ height: 1, backgroundColor: "#eee" }} />}
                renderItem={({ item }) => (
                  <TouchableOpacity
                    onPress={() => handlePick(item.tenantId)}
                    style={{
                      paddingVertical: 14,
                      flexDirection: "row",
                      justifyContent: "space-between",
                      alignItems: "center",
                    }}
                  >
                    <View style={{ flex: 1 }}>
                      <Text style={{ fontSize: 15, fontWeight: "600" }}>{item.tenantName}</Text>
                      <Text style={{ fontSize: 12, color: "#666", marginTop: 2 }}>
                        {item.tenantTier} · {item.role}
                        {item.mimEnabled ? " · MIM" : ""}
                      </Text>
                    </View>
                    {item.tenantId === currentId && (
                      <Text style={{ fontSize: 14, color: "#E8912D", fontWeight: "700" }}>✓</Text>
                    )}
                  </TouchableOpacity>
                )}
              />
            )}

            <TouchableOpacity
              onPress={() => setOpen(false)}
              style={{
                marginTop: 16,
                padding: 12,
                backgroundColor: "#eee",
                borderRadius: 8,
                alignItems: "center",
              }}
            >
              <Text style={{ fontWeight: "600" }}>{t("common.close")}</Text>
            </TouchableOpacity>
          </View>
        </View>
      </Modal>
    </>
  );
}
