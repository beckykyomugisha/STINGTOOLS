// Healthcare Pack H-21 — MGPS NFPA 99 §5.1.12 verification checklist (mobile).
import { useState } from 'react';
import { View, Text, ScrollView, StyleSheet, TouchableOpacity, Alert } from 'react-native';
import { useProjectStore } from '@/stores/projectStore';
import { postMgasVerification } from '@/api/endpoints';

const STEPS = [
  'Pre-purge with oil-free dry nitrogen',
  'Cross-connection test (each gas in turn)',
  'Particulate test at every TU',
  'Purity test per BS EN ISO 7396-1',
  'Pressure decay / standing test',
  'Indexing / NIST / DISS test',
  'Labelling test at TU + ZVB + alarm',
  'Area alarm operability under simulated fault',
  'Master alarm operability under simulated fault',
  'Source / plant changeover test',
  'Emergency reserve test',
  'Sign-off by ASSE 6030 verifier',
];

export default function MgasChecklistScreen() {
  const { activeProject } = useProjectStore();
  const [results, setResults] = useState<Record<number, 'PASS'|'FAIL'|null>>({});
  const setStep = (i: number, v: 'PASS'|'FAIL') =>
    setResults(prev => ({ ...prev, [i]: v }));

  const submit = async () => {
    const passes = Object.values(results).filter(v => v === 'PASS').length;
    const fails  = Object.values(results).filter(v => v === 'FAIL').length;
    if (!activeProject?.id) { Alert.alert('No project', 'Select a project first.'); return; }
    const checkResults: Record<string, boolean> = {};
    STEPS.forEach((s, i) => { if (results[i] === 'PASS') checkResults[s] = true; if (results[i] === 'FAIL') checkResults[s] = false; });
    try {
      await postMgasVerification(activeProject.id, {
        zone: 'ALL', gasCode: 'ALL', verifierName: 'mobile-user',
        overallPass: fails === 0, passCount: passes, failCount: fails,
        checkResultsJson: JSON.stringify(checkResults),
      });
      Alert.alert('Saved', `pass=${passes} fail=${fails} — submitted to server.`);
    } catch (e: any) {
      // Offline-queue fallback would land here. For now surface the error.
      Alert.alert('Save failed', e?.message ?? 'Submit later when online.');
    }
  };

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.h1}>MGPS Verification</Text>
      <Text style={styles.h2}>{activeProject?.name ?? '—'}</Text>
      {STEPS.map((s, i) => (
        <View key={i} style={styles.row}>
          <Text style={styles.stepText}>{i+1}. {s}</Text>
          <View style={styles.btns}>
            <TouchableOpacity style={[styles.btn, results[i]==='PASS' && styles.pass]} onPress={() => setStep(i, 'PASS')}>
              <Text style={styles.btnText}>PASS</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.btn, results[i]==='FAIL' && styles.fail]} onPress={() => setStep(i, 'FAIL')}>
              <Text style={styles.btnText}>FAIL</Text>
            </TouchableOpacity>
          </View>
        </View>
      ))}
      <TouchableOpacity style={styles.submit} onPress={submit}>
        <Text style={styles.submitText}>Save Verification</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16 },
  h1: { fontSize: 22, fontWeight: '700' },
  h2: { fontSize: 13, color: '#666', marginBottom: 16 },
  row: { backgroundColor: '#fff', padding: 12, borderRadius: 6, marginBottom: 8 },
  stepText: { fontSize: 14, marginBottom: 8 },
  btns: { flexDirection: 'row', gap: 10 },
  btn: { paddingHorizontal: 16, paddingVertical: 8, borderRadius: 4, backgroundColor: '#eee' },
  pass: { backgroundColor: '#43A047' },
  fail: { backgroundColor: '#D50000' },
  btnText: { color: '#fff', fontWeight: '600' },
  submit: { backgroundColor: '#1976D2', padding: 14, borderRadius: 6, marginTop: 12, alignItems: 'center' },
  submitText: { color: '#fff', fontWeight: '700' },
});
