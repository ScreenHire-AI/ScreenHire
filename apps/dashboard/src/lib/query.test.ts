import { ApiError } from '@screenhire/api-client/svelte-query';
import { describe, expect, it } from 'vitest';
import { describeError, planProblem } from './query.js';

describe('describeError', () => {
  it("uses the API's problem detail when there is one", () => {
    expect(describeError(new ApiError(409, { title: 'Conflict', detail: 'A closed job cannot be changed.' }), 'Could not save')).toBe(
      'A closed job cannot be changed.',
    );
  });

  it("lists a validation problem's messages, each once, after what failed", () => {
    const problem = {
      title: 'One or more validation errors occurred.',
      errors: {
        description: ['Add a description before publishing.'],
        applicantLocationRequirements: ['A remote job needs the countries candidates may work from before publishing.'],
        other: ['Add a description before publishing.'],
      },
    };

    expect(describeError(new ApiError(400, problem), 'Could not publish the job')).toBe(
      'Could not publish the job: Add a description before publishing. A remote job needs the countries candidates may work from before publishing.',
    );
  });

  it('falls back to the status when the body says nothing', () => {
    expect(describeError(new ApiError(503, undefined), 'Could not load jobs')).toBe('Could not load jobs (HTTP 503).');
    expect(describeError(new ApiError(400, { errors: {} }), 'Could not publish the job')).toBe('Could not publish the job (HTTP 400).');
  });

  it('says to check the connection when the request never reached the API', () => {
    expect(describeError(new TypeError('Failed to fetch'), 'Could not close the job')).toBe(
      'Could not close the job. Check your connection and try again.',
    );
  });
});

describe('planProblem', () => {
  const detail = 'Your plan allows 5 open jobs and 5 are open. Close a job, or ask an owner to upgrade the plan.';

  it('recognises a plan limit and a subscription that ended, by status and problem type', () => {
    expect(planProblem(new ApiError(402, { type: 'urn:screenhire:problem:plan-limit', title: 'Plan limit reached', detail }))).toEqual({
      kind: 'plan-limit',
      detail,
    });
    expect(planProblem(new ApiError(402, { type: 'urn:screenhire:problem:subscription-ended', detail: 'Your subscription has ended.' }))).toEqual({
      kind: 'subscription-ended',
      detail: 'Your subscription has ended.',
    });
  });

  it('words a problem that has no detail', () => {
    expect(planProblem(new ApiError(402, { type: 'urn:screenhire:problem:plan-limit' }))?.detail).toBe('Your plan has no room for another open job.');
  });

  it('leaves every other failure to describeError', () => {
    expect(planProblem(new ApiError(402, { type: 'urn:screenhire:problem:something-else', detail }))).toBeNull();
    expect(planProblem(new ApiError(402, { detail }))).toBeNull();
    expect(planProblem(new ApiError(402, undefined))).toBeNull();
    expect(planProblem(new ApiError(409, { type: 'urn:screenhire:problem:plan-limit', detail }))).toBeNull();
    expect(planProblem(new TypeError('Failed to fetch'))).toBeNull();
  });
});
