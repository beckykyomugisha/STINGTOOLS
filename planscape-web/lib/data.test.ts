import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as data from './data';
import { setToken } from './api';

type Call = { url: string; init: RequestInit };

let lastBody: unknown = { ok: true };
let f: ReturnType<typeof vi.fn>;

function calls(): Call[] {
  return f.mock.calls.map((c) => ({ url: String(c[0]), init: (c[1] ?? {}) as RequestInit }));
}
function bodyOf(init: RequestInit) {
  return typeof init.body === 'string' ? JSON.parse(init.body) : init.body;
}

beforeEach(() => {
  window.localStorage.clear();
  setToken('T');
  lastBody = { ok: true };
  f = vi.fn().mockImplementation(async () => ({ ok: true, status: 200, json: async () => lastBody }) as Response);
  vi.stubGlobal('fetch', f);
});
afterEach(() => vi.restoreAllMocks());

describe('issues', () => {
  it('listIssues unwraps both array and {items}', async () => {
    lastBody = [{ id: '1' }];
    expect(await data.listIssues('p')).toHaveLength(1);
    lastBody = { items: [{ id: '2' }, { id: '3' }] };
    expect(await data.listIssues('p', 'OPEN')).toHaveLength(2);
    expect(calls()[1].url).toContain('/api/projects/p/issues?status=OPEN');
  });

  it('createIssue POSTs the body', async () => {
    await data.createIssue('p', { title: 'T' });
    const c = calls()[0];
    expect(c.init.method).toBe('POST');
    expect(c.url).toContain('/api/projects/p/issues');
    expect(bodyOf(c.init)).toEqual({ title: 'T' });
  });

  it('updateIssue PUTs', async () => {
    await data.updateIssue('p', 'i', { status: 'CLOSED' });
    const c = calls()[0];
    expect(c.init.method).toBe('PUT');
    expect(c.url).toContain('/api/projects/p/issues/i');
  });

  it('addComment POSTs {body}', async () => {
    await data.addComment('p', 'i', 'hi');
    expect(bodyOf(calls()[0].init)).toEqual({ body: 'hi' });
  });
});

describe('clashes', () => {
  it('listClashes builds the query string', async () => {
    lastBody = { items: [], total: 0 };
    await data.listClashes('p', { status: 'NEW', severity: 'CRITICAL' });
    expect(calls()[0].url).toContain('status=NEW');
    expect(calls()[0].url).toContain('severity=CRITICAL');
  });
  it('updateClash PATCHes', async () => {
    await data.updateClash('p', 'c', { status: 'RESOLVED' });
    expect(calls()[0].init.method).toBe('PATCH');
  });
  it('runClashDetection + promote POST', async () => {
    await data.runClashDetection('p');
    await data.promoteClashToIssue('p', 'c');
    expect(calls()[0].init.method).toBe('POST');
    expect(calls()[0].url).toContain('/clashes/run');
    expect(calls()[1].url).toContain('/clashes/c/promote-to-issue');
  });
});

describe('models + federation url helpers', () => {
  it('modelFileUrl / chunkFileUrl / documentDownloadUrl / photoFileUrl carry the token', () => {
    expect(data.modelFileUrl('p', 'm')).toContain('/api/projects/p/models/m/file?access_token=T');
    expect(data.chunkFileUrl('/api/v1/scene-nodes/x/file')).toContain('/api/v1/scene-nodes/x/file?access_token=T');
    expect(data.documentDownloadUrl('p', 'd')).toContain('/documents/d/download?access_token=T');
    expect(data.photoFileUrl('p', 'ph')).toContain('/photos/ph/file?access_token=T');
  });

  it('getSceneManifest returns null on 404', async () => {
    f.mockResolvedValueOnce({ ok: false, status: 404, json: async () => ({}) } as Response);
    expect(await data.getSceneManifest('p')).toBeNull();
  });

  it('uploadModel sends multipart FormData without a JSON Content-Type', async () => {
    const file = new File(['x'], 'm.glb', { type: 'model/gltf-binary' });
    await data.uploadModel('p', file, { discipline: 'M' });
    const c = calls()[0];
    expect(c.init.method).toBe('POST');
    expect(c.url).toContain('/api/projects/p/models');
    expect(c.init.body).toBeInstanceOf(FormData);
    expect(new Headers(c.init.headers).get('Content-Type')).toBeNull(); // browser sets boundary
    expect(new Headers(c.init.headers).get('Authorization')).toBe('Bearer T');
  });
});

describe('meetings', () => {
  it('list/create/detail + live endpoints', async () => {
    await data.listMeetings('p', 'SCHEDULED');
    expect(calls()[0].url).toContain('/api/projects/p/meetings?status=SCHEDULED');

    await data.createMeeting('p', { title: 'M', scheduledAt: '2026-01-01T00:00:00Z' });
    expect(calls()[1].init.method).toBe('POST');

    await data.addAction('p', 'm', { description: 'do it' });
    expect(calls()[2].url).toContain('/meetings/m/actions');

    await data.startLiveSession('p', 'm', { displayName: 'me' });
    expect(calls()[3].url).toContain('/meetings/m/live-session');

    await data.getLiveKitToken('p', 's', 'me');
    expect(calls()[4].url).toContain('/meeting-sessions/s/livekit-token');
    expect(bodyOf(calls()[4].init)).toEqual({ displayName: 'me' });
  });
});

describe('documents', () => {
  it('listDocuments unwraps {items} and applies filters', async () => {
    lastBody = { items: [{ id: 'd' }], total: 1, page: 1, pageSize: 50 };
    const out = await data.listDocuments('p', { cdeStatus: 'SHARED', search: 'plan' });
    expect(out).toHaveLength(1);
    expect(calls()[0].url).toContain('cdeStatus=SHARED');
    expect(calls()[0].url).toContain('search=plan');
  });
  it('transitionDocument PUTs newState + suitability', async () => {
    await data.transitionDocument('p', 'd', { newState: 'PUBLISHED', suitabilityCode: 'S4' });
    const c = calls()[0];
    expect(c.init.method).toBe('PUT');
    expect(c.url).toContain('/documents/d/state');
    expect(bodyOf(c.init)).toEqual({ newState: 'PUBLISHED', suitabilityCode: 'S4' });
  });
  it('uploadDocument is multipart', async () => {
    lastBody = { id: 'd' };
    await data.uploadDocument('p', new File(['x'], 'a.pdf'), { discipline: 'A' });
    const c = calls()[0];
    expect(c.url).toContain('/documents/upload');
    expect(c.init.body).toBeInstanceOf(FormData);
  });
});

describe('members + search + transmittals + photos', () => {
  it('members', async () => {
    lastBody = [];
    await data.listMembers('p');
    expect(calls()[0].url).toContain('/api/projects/p/members');
    await data.inviteMember('p', { email: 'a@b.c', projectRole: 'Contributor' });
    expect(calls()[1].url).toContain('/members/invite');
    await data.updateMemberRole('p', 'mid', { projectRole: 'Manager' });
    expect(calls()[2].init.method).toBe('PUT');
    await data.removeMember('p', 'mid');
    expect(calls()[3].init.method).toBe('DELETE');
  });

  it('search builds q + type + limit', async () => {
    lastBody = { query: 'x', count: 0, results: [] };
    await data.search('road', ['issue', 'document'], 10);
    const u = calls()[0].url;
    expect(u).toContain('q=road');
    expect(u).toContain('type=issue%2Cdocument');
    expect(u).toContain('limit=10');
  });

  it('transmittals list unwraps + actions', async () => {
    lastBody = { transmittals: [{ id: 't' }] };
    expect(await data.listTransmittals('p')).toHaveLength(1);
    await data.transmittalAction('p', 't', 'send');
    expect(calls()[1].url).toContain('/transmittals/t/send');
    expect(calls()[1].init.method).toBe('PUT');
  });

  it('photos list unwraps {items} + approve/reject', async () => {
    lastBody = { items: [{ id: 'ph' }], total: 1 };
    expect(await data.listSitePhotos('p', { reason: 'Defect' })).toHaveLength(1);
    expect(calls()[0].url).toContain('reason=Defect');
    await data.approvePhoto('p', 'ph', 'caption here');
    expect(bodyOf(calls()[1].init)).toEqual({ caption: 'caption here' });
    await data.rejectPhoto('p', 'ph', 'blurry');
    expect(calls()[2].url).toContain('/photos/ph/reject');
  });
});
