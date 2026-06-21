'use client';

import { useEffect, useState, type FormEvent } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { getIssue, updateIssue, listComments, addComment } from '@/lib/data';
import type { BimIssue, IssueComment, IssueStatus } from '@/lib/types';

const STATUSES: IssueStatus[] = ['OPEN', 'IN_PROGRESS', 'RESOLVED', 'CLOSED'];

export default function IssueDetailPage() {
  const params = useParams<{ id: string; issueId: string }>();
  const projectId = params.id;
  const issueId = params.issueId;

  const [issue, setIssue] = useState<BimIssue | null>(null);
  const [comments, setComments] = useState<IssueComment[]>([]);
  const [newComment, setNewComment] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getIssue(projectId, issueId)
      .then(setIssue)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load issue'));
    listComments(projectId, issueId).then(setComments).catch(() => {});
  }, [projectId, issueId]);

  async function changeStatus(s: IssueStatus) {
    if (!issue || issue.status === s) return;
    const prev = issue.status;
    setIssue({ ...issue, status: s });
    try {
      await updateIssue(projectId, issueId, { status: s });
    } catch {
      setIssue({ ...issue, status: prev });
      setError('Failed to update status');
    }
  }

  async function postComment(e: FormEvent) {
    e.preventDefault();
    const body = newComment.trim();
    if (!body) return;
    setNewComment('');
    try {
      const c = await addComment(projectId, issueId, body);
      setComments((cs) => [...cs, c]);
    } catch {
      setError('Failed to add comment');
      setNewComment(body);
    }
  }

  return (
    <AppShell>
      <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
        ← Back to issues
      </Link>

      {error && <p className="my-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!issue && !error && <p className="mt-3 text-slate-400">Loading…</p>}

      {issue && (
        <>
          <h1 className="mt-1 text-xl font-semibold">{issue.title}</h1>
          <div className="mt-1 text-xs text-slate-400">
            {issue.type} · {issue.priority}
            {issue.discipline ? ` · ${issue.discipline}` : ''}
            {issue.assignee ? ` · ${issue.assignee}` : ''}
          </div>

          {issue.description && (
            <p className="mt-4 whitespace-pre-wrap rounded-lg bg-white p-4 text-sm ring-1 ring-slate-200">
              {issue.description}
            </p>
          )}

          <div className="mt-4">
            <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-400">Status</span>
            <div className="flex flex-wrap gap-2">
              {STATUSES.map((s) => (
                <button
                  key={s}
                  onClick={() => changeStatus(s)}
                  className={`rounded-full px-3 py-1 text-xs ${
                    issue.status === s ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200'
                  }`}
                >
                  {s.replace('_', ' ')}
                </button>
              ))}
            </div>
          </div>

          <section className="mt-8">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-400">Comments</h2>
            <ul className="space-y-2">
              {comments.length === 0 && <li className="text-sm text-slate-400">No comments yet.</li>}
              {comments.map((c) => (
                <li key={c.id} className="rounded-lg bg-white p-3 text-sm ring-1 ring-slate-200">
                  <div className="mb-1 flex items-center justify-between text-xs text-slate-400">
                    <span>{c.authorName ?? 'User'}</span>
                    <span>{c.createdAt ? new Date(c.createdAt).toLocaleString() : ''}</span>
                  </div>
                  <p className="whitespace-pre-wrap">{c.body}</p>
                </li>
              ))}
            </ul>

            <form onSubmit={postComment} className="mt-3 flex gap-2">
              <input
                value={newComment}
                onChange={(e) => setNewComment(e.target.value)}
                placeholder="Add a comment…"
                className="flex-1 rounded border border-slate-300 px-3 py-2 outline-none focus:border-blue-500"
              />
              <button
                type="submit"
                disabled={!newComment.trim()}
                className="rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60"
              >
                Post
              </button>
            </form>
          </section>
        </>
      )}
    </AppShell>
  );
}
