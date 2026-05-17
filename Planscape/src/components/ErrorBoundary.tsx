import { Component, ReactNode } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView, Platform } from 'react-native';
import { crashReporter } from '@/services/crashReporter';

interface Props { children: ReactNode }
interface State { error: Error | null; info: string | null }

/**
 * Phase 96 — app-level React error boundary.
 *
 * Before: an unhandled exception in a screen component white-screened the app
 * and the BIM coordinator had to force-kill and reopen. On site, that's
 * several minutes of lost work plus a bad bug report ("the app crashed, I
 * don't know why").
 *
 * After: a recoverable fallback screen with the error message, a stack trace
 * (scrollable, for dev builds), a "Reset" button to clear the error and
 * re-render, and a crash-report forward to the server so we get the
 * diagnostic trail even when the user doesn't message support.
 *
 * This only catches render-phase errors. Async errors in useEffect /
 * event handlers still need their own try/catch — but those at least don't
 * white-screen the whole app.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null, info: null };

  static getDerivedStateFromError(error: Error): State {
    return { error, info: null };
  }

  componentDidCatch(error: Error, info: { componentStack: string }): void {
    // M4 — was calling crashReporter.error, which doesn't exist on the
    // exported surface; first render exception would crash the boundary
    // itself. Use captureError, which IS the canonical entry point.
    crashReporter.captureError(error, {
      where: 'ErrorBoundary',
      componentStack: info.componentStack,
    });
    this.setState({ info: info.componentStack });
  }

  reset = (): void => {
    this.setState({ error: null, info: null });
  };

  render(): ReactNode {
    if (!this.state.error) return this.props.children;

    return (
      <View style={styles.root}>
        <Text style={styles.icon}>⚠️</Text>
        <Text style={styles.title}>Something went wrong</Text>
        <Text style={styles.subtitle}>
          Planscape hit an unexpected error. Your work is safe — queued actions will sync next time you tap Reset.
        </Text>

        <ScrollView style={styles.traceWrap} contentContainerStyle={styles.traceContent}>
          <Text style={styles.traceLabel}>Error</Text>
          <Text style={styles.traceText}>{this.state.error.message}</Text>
          {__DEV__ && this.state.error.stack ? (
            <>
              <Text style={styles.traceLabel}>Stack</Text>
              <Text style={styles.traceText}>{this.state.error.stack}</Text>
            </>
          ) : null}
          {__DEV__ && this.state.info ? (
            <>
              <Text style={styles.traceLabel}>Component</Text>
              <Text style={styles.traceText}>{this.state.info}</Text>
            </>
          ) : null}
        </ScrollView>

        <TouchableOpacity style={styles.button} onPress={this.reset} accessibilityRole="button"
          accessibilityLabel="Reset and continue using the app">
          <Text style={styles.buttonText}>Reset</Text>
        </TouchableOpacity>
      </View>
    );
  }
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: '#1A237E',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
  },
  icon: { fontSize: 48, marginBottom: 12 },
  title: { fontSize: 20, fontWeight: '700', color: '#fff', marginBottom: 6 },
  subtitle: {
    fontSize: 13, color: 'rgba(255,255,255,0.8)', textAlign: 'center',
    lineHeight: 19, marginBottom: 20,
  },
  traceWrap: {
    maxHeight: 240,
    width: '100%',
    backgroundColor: 'rgba(0,0,0,0.35)',
    borderRadius: 8,
    padding: 12,
    marginBottom: 20,
  },
  traceContent: { paddingBottom: 8 },
  traceLabel: {
    fontSize: 10, fontWeight: '700', color: '#E8912D',
    letterSpacing: 0.5, marginTop: 6,
  },
  traceText: {
    fontSize: 11,
    color: '#fff',
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    marginTop: 2,
  },
  button: {
    backgroundColor: '#E8912D',
    paddingHorizontal: 32,
    paddingVertical: 12,
    borderRadius: 8,
  },
  buttonText: { color: '#fff', fontWeight: '700', fontSize: 16 },
});
