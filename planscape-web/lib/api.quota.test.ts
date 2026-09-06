import { describe, it, expect } from 'vitest';
import { ApiError, describeFailure } from './api';

/**
 * #670 — the owner met the literal token `quota_exceeded` on
 * /projects/new in production and read it as an expired account.
 *
 * RED WITNESS: every assertion below fails if `describeFailure` drops the 402
 * branch, because the fallback path returns tone 'error' and the message
 * `ApiError.message` — which is the machine token. That is the regression this
 * file exists to catch, not a restatement of the fix.
 */
const opts = { forbidden: 'Only an Owner or Admin can create projects.', fallback: 'Failed' };

describe('describeFailure — quota (#670)', () => {
  it('shows the server reason, not the machine code', () => {
    const e = new ApiError(402, 'quota_exceeded', {
      error: 'quota_exceeded', axis: 'Projects', current: 5, max: 5,
      reason: 'Projects cap reached (5 of 5).', upgrade_url: '/settings/billing',
    }, 'quota_exceeded');
    const r = describeFailure(e, opts);
    expect(r.message).toBe('Projects cap reached (5 of 5).');
    expect(r.message).not.toContain('quota_exceeded');
    expect(r.tone).toBe('quota');
    expect(r.actionHref).toBe('/settings/billing');
  });

  it('never surfaces the raw token even when the server sends no reason', () => {
    const e = new ApiError(402, 'quota_exceeded', { error: 'quota_exceeded', axis: 'Authors' });
    const r = describeFailure(e, opts);
    expect(r.message).toBe('Authors limit reached for your plan.');
    expect(r.message).not.toContain('quota_exceeded');
    expect(r.actionHref).toBeUndefined();
  });

  it('falls back to a sentence when there is no reason and no axis', () => {
    const e = new ApiError(402, 'quota_exceeded', { error: 'quota_exceeded' });
    expect(describeFailure(e, opts).message).toBe('Your plan limit has been reached.');
  });

  it('is keyed off status + discriminator, never off the message text', () => {
    // A 402 whose body is not a quota refusal must NOT be dressed as one, and a
    // message that merely CONTAINS the token must not trigger it either - that
    // string-sniffing is #624/#646.
    expect(describeFailure(new ApiError(402, 'boom', { error: 'card_declined' }), opts).tone).toBe('error');
    expect(describeFailure(new ApiError(500, 'quota_exceeded happened', undefined), opts).tone).toBe('error');
  });

  it('leaves 403 as forbidden, unchanged', () => {
    expect(describeFailure(new ApiError(403, 'x', undefined, undefined), opts).tone).toBe('forbidden');
  });
});
