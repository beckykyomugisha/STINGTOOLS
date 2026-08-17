import { afterEach, describe, expect, it, vi } from 'vitest';
import { getProjectCapabilities } from './capabilities';

/**
 * The three-state capability rule (#547 / #558 / #634), pinned.
 *
 * The whole correctness of a disabled Approve button hinges on one distinction
 * that is invisible in the type system and easy to "tidy" away:
 *
 *   'denied'   the server said no        → disable, name the capability
 *   'unknown'  we never got an answer    → LEAVE IT ENABLED
 *
 * Collapsing unknown into denied locks legitimate users out on a network blip
 * *while showing them a permissions message*. A 404 is the one exception, and
 * is asserted as such.
 */

function mockJson(status: number, body: unknown) {
  return vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response);
}

describe('getProjectCapabilities()', () => {
  afterEach(() => vi.restoreAllMocks());

  it('maps explicit booleans in both directions', async () => {
    vi.stubGlobal('fetch', mockJson(200, { canCurateProject: true, canApproveSitePhotos: false }));

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'allowed',
      approveSitePhotos: 'denied',
    });
  });

  it('treats 404 as authoritative-false — the caller cannot see the project', async () => {
    vi.stubGlobal('fetch', mockJson(404, {}));

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'denied',
      approveSitePhotos: 'denied',
    });
  });

  it('is unknown when the fetch never completes — NOT denied', async () => {
    // The regression this file exists to catch.
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'unknown',
      approveSitePhotos: 'unknown',
    });
  });

  it.each([500, 502, 403])('is unknown for HTTP %i', async (status) => {
    vi.stubGlobal('fetch', mockJson(status, { error: 'boom' }));

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'unknown',
      approveSitePhotos: 'unknown',
    });
  });

  it('is unknown when the body will not parse', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => {
          throw new Error('<html>proxy error</html>');
        },
      } as unknown as Response),
    );

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'unknown',
      approveSitePhotos: 'unknown',
    });
  });

  it('leaves a missing field unknown while honouring the one that is present', async () => {
    vi.stubGlobal('fetch', mockJson(200, { canCurateProject: true }));

    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'allowed',
      approveSitePhotos: 'unknown',
    });
  });

  it.each(['"true"', 'null', '1'])('treats the non-boolean %s as unknown', async (literal) => {
    const v = JSON.parse(literal);
    vi.stubGlobal('fetch', mockJson(200, { canCurateProject: v, canApproveSitePhotos: v }));

    // "true" the STRING is not true the BOOLEAN. Coercing it would mean a
    // contract drift silently granted a capability; reading it as false would
    // silently remove one. Neither is an answer we were given.
    expect(await getProjectCapabilities('p1')).toEqual({
      curateProject: 'unknown',
      approveSitePhotos: 'unknown',
    });
  });
});
