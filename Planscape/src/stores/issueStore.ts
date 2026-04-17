import { create } from 'zustand';
import { BimIssue } from '../types';

interface IssueState {
  issues: Record<string, BimIssue>;
  loading: boolean;
  error: string | null;
  setIssues: (issues: BimIssue[]) => void;
  upsertIssue: (issue: BimIssue) => void;
  removeIssue: (id: string) => void;
  setLoading: (loading: boolean) => void;
  setError: (error: string | null) => void;
  getByProject: (projectId: string) => BimIssue[];
}

export const useIssueStore = create<IssueState>((set, get) => ({
  issues: {},
  loading: false,
  error: null,
  setIssues: (issues) => set({
    issues: Object.fromEntries(issues.map(i => [i.id, i])),
  }),
  upsertIssue: (issue) => set(state => ({
    issues: { ...state.issues, [issue.id]: issue },
  })),
  removeIssue: (id) => set(state => {
    const next = { ...state.issues };
    delete next[id];
    return { issues: next };
  }),
  setLoading: (loading) => set({ loading }),
  setError: (error) => set({ error }),
  getByProject: (projectId) =>
    Object.values(get().issues).filter(i => i.projectId === projectId),
}));
