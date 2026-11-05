import { QueryClient } from '@tanstack/svelte-query';
import { describe, expect, it } from 'vitest';
import { invalidatePath } from '#lib/query.js';
import { invalidations } from '#lib/realtime.js';

describe('live updates', () => {
  it('marks every list, job, page and report an erased candidate appears in as stale, and nothing else', async () => {
    const client = new QueryClient();
    const keys = [
      ['v1', 'talent-pool'],
      ['v1', 'jobs'],
      ['v1', 'jobs', 'job-1', 'applications'],
      ['v1', 'applications'],
      ['v1', 'applications', 'app-1'],
      ['v1', 'candidates'],
      ['v1', 'candidates', 'candidate-1'],
      ['v1', 'candidates', 'candidate-1', 'history'],
      ['v1', 'tasks'],
      ['v1', 'reports', 'funnel'],
    ];
    const unrelated = [['v1', 'organization', 'members'], ['v1', 'templates']];
    for (const key of [...keys, ...unrelated]) client.setQueryData(key, {});

    await Promise.all((invalidations.CandidateErased ?? []).map((path) => invalidatePath(client, path)));

    for (const key of keys) expect(client.getQueryState(key)?.isInvalidated, key.join('/')).toBe(true);
    for (const key of unrelated) expect(client.getQueryState(key)?.isInvalidated, key.join('/')).toBe(false);
  });

  it.each(['DataRequestReceived', 'DataRequestChanged'] as const)('marks the Data requests list stale on %s and leaves the retention setting alone', async (type) => {
    const client = new QueryClient();
    const list = ['v1', 'data-requests'];
    const unrelated = [['v1', 'organization', 'data-retention'], ['v1', 'organization', 'data-retention', 'preview'], ['v1', 'candidates']];
    for (const key of [list, ...unrelated]) client.setQueryData(key, {});

    await Promise.all((invalidations[type] ?? []).map((path) => invalidatePath(client, path)));

    expect(client.getQueryState(list)?.isInvalidated).toBe(true);
    for (const key of unrelated) expect(client.getQueryState(key)?.isInvalidated, key.join('/')).toBe(false);
  });

  it('refreshes the retention setting, its dry-run and Last run, and the requests, when a candidate is erased', async () => {
    const client = new QueryClient();
    const keys = [['v1', 'data-requests'], ['v1', 'organization', 'data-retention'], ['v1', 'organization', 'data-retention', 'preview', { months: 6 }]];
    for (const key of keys) client.setQueryData(key, {});

    await Promise.all((invalidations.CandidateErased ?? []).map((path) => invalidatePath(client, path)));

    for (const key of keys) expect(client.getQueryState(key)?.isInvalidated, key.join('/')).toBe(true);
  });
});
