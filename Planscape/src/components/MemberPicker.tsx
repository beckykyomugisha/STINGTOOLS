import { useEffect, useState, useMemo } from 'react';
import {
  View, Text, StyleSheet, Modal, FlatList, TouchableOpacity,
  TextInput, ActivityIndicator,
} from 'react-native';
import { theme } from '@/utils/theme';
import { listProjectMembers } from '@/api/endpoints';
import type { ProjectMember } from '@/types/api';

interface Props {
  visible: boolean;
  projectId: string;
  selectedEmail?: string;
  onSelect: (member: ProjectMember | null) => void;
  onClose: () => void;
}

export function MemberPicker({ visible, projectId, selectedEmail, onSelect, onClose }: Props) {
  const [members, setMembers] = useState<ProjectMember[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    if (!visible || !projectId) return;
    setLoading(true);
    setError(null);
    listProjectMembers(projectId)
      .then(setMembers)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Load failed'))
      .finally(() => setLoading(false));
  }, [visible, projectId]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return members;
    return members.filter(m =>
      m.displayName.toLowerCase().includes(q) ||
      m.email.toLowerCase().includes(q) ||
      (m.iso19650Role && m.iso19650Role.toLowerCase().includes(q)),
    );
  }, [members, search]);

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.card}>
          <Text style={styles.title}>Assign to member</Text>
          <TextInput
            style={styles.input}
            placeholder="Search by name, email, or role"
            placeholderTextColor={theme.colors.disabled}
            value={search}
            onChangeText={setSearch}
            accessibilityLabel="Search project members"
          />
          <TouchableOpacity
            style={styles.unassignRow}
            onPress={() => { onSelect(null); onClose(); }}
            accessibilityRole="button"
            accessibilityLabel="Unassign"
          >
            <Text style={styles.unassignText}>Unassigned</Text>
            {!selectedEmail && <Text style={styles.tickText}>✓</Text>}
          </TouchableOpacity>
          {loading ? (
            <ActivityIndicator color={theme.colors.accent} style={{ marginTop: 16 }} />
          ) : error ? (
            <Text style={styles.errorText}>{error}</Text>
          ) : (
            <FlatList
              data={filtered}
              keyExtractor={m => m.userId}
              renderItem={({ item }) => (
                <TouchableOpacity
                  style={styles.row}
                  onPress={() => { onSelect(item); onClose(); }}
                  accessibilityRole="button"
                  accessibilityLabel={`Assign to ${item.displayName}`}
                >
                  <View style={{ flex: 1 }}>
                    <Text style={styles.name}>{item.displayName}</Text>
                    <Text style={styles.email}>{item.email}</Text>
                    {item.iso19650Role ? (
                      <Text style={styles.role}>{item.iso19650Role}</Text>
                    ) : null}
                  </View>
                  {selectedEmail === item.email && <Text style={styles.tickText}>✓</Text>}
                </TouchableOpacity>
              )}
              ListEmptyComponent={<Text style={styles.empty}>No members match.</Text>}
            />
          )}
          <TouchableOpacity style={styles.close} onPress={onClose}>
            <Text style={styles.closeText}>Cancel</Text>
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: { flex: 1, backgroundColor: 'rgba(0,0,0,0.5)', justifyContent: 'flex-end' },
  card: {
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: 16, borderTopRightRadius: 16,
    padding: 16, maxHeight: '85%',
  },
  title: { fontSize: 18, fontWeight: '600', color: theme.colors.text, marginBottom: 12 },
  input: {
    borderWidth: 1, borderColor: theme.colors.border, borderRadius: 8,
    paddingHorizontal: 12, paddingVertical: 8, color: theme.colors.text,
    marginBottom: 8,
  },
  unassignRow: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  unassignText: { fontSize: 15, color: theme.colors.textSecondary, fontStyle: 'italic' },
  row: {
    flexDirection: 'row', alignItems: 'center',
    paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  name: { fontSize: 15, fontWeight: '600', color: theme.colors.text },
  email: { fontSize: 13, color: theme.colors.textSecondary, marginTop: 2 },
  role: { fontSize: 12, color: theme.colors.accent, marginTop: 2 },
  tickText: { fontSize: 18, color: theme.colors.accent, marginLeft: 12 },
  empty: { textAlign: 'center', color: theme.colors.textSecondary, marginTop: 24 },
  errorText: { color: theme.colors.danger, marginTop: 16, textAlign: 'center' },
  close: {
    marginTop: 12, paddingVertical: 12, borderRadius: 8,
    borderWidth: 1, borderColor: theme.colors.border, alignItems: 'center',
  },
  closeText: { color: theme.colors.textSecondary, fontWeight: '600' },
});
