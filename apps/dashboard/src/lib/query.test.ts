import { ApiError } from '@screenhire/api-client/svelte-query';
import { describe, expect, it } from 'vitest';
import { describeError } from './query.js';

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
