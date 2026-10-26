import type { HistoryEntry, HistoryPage } from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { flushSync } from 'svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createHistoryPages } from './history-pages.svelte.js';

// createHistoryPages calls createQueries without a client, so it reads the nearest component context. This test has no
// component, so the mock hands createQueries its client through the argument it already accepts.
const holder = vi.hoisted(() => ({ client: undefined as unknown }));
vi.mock('@tanstack/svelte-query', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/svelte-query')>();
  return {
    ...actual,
    createQueries: (options: Parameters<typeof actual.createQueries>[0]) =>
      actual.createQueries(options, () => holder.client as QueryClient),
  };
});

const pageSize = 2;
const entry = (id: string): HistoryEntry => ({ id, at: '2026-10-25T12:00:00Z', by: 'Member', userId: 'u1', apiKeyId: null, lines: [] });

/** The server's history, newest first. `before` is an entry id; the page holds the entries strictly older than it. */
let stored: string[];

function readPage(before: string | undefined): { data: HistoryPage } {
  const start = before === undefined ? 0 : stored.indexOf(before) + 1;
  const ids = stored.slice(start, start + pageSize);
  const more = start + pageSize < stored.length;
  return { data: { entries: ids.map(entry), nextBefore: more ? ids[ids.length - 1]! : null } };
}

const page = (before: string | undefined) => ({
  queryKey: ['history', before ?? 'first'],
  queryFn: async () => readPage(before),
});

let client: QueryClient;
let stop: () => void;

function mount() {
  let pages!: ReturnType<typeof createHistoryPages<{ data: HistoryPage }, Error>>;
  stop = $effect.root(() => {
    pages = createHistoryPages(page, () => true);
  });
  flushSync();
  return pages;
}

const ids = (pages: { entries: HistoryEntry[] | undefined }) => pages.entries?.map((e) => e.id);

/** Waits until nothing is loading, running the effects each time it looks. */
const settled = () =>
  vi.waitFor(() => {
    flushSync();
    expect(client.isFetching()).toBe(0);
  });

beforeEach(() => {
  stored = ['e6', 'e5', 'e4', 'e3', 'e2', 'e1'];
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  holder.client = client;
});

afterEach(() => {
  stop();
  client.clear();
});

describe('createHistoryPages', () => {
  it('loads the first page, then the next page after it, in order', async () => {
    const pages = mount();
    await vi.waitFor(() => expect(ids(pages)).toEqual(['e6', 'e5']));
    expect(pages.hasMore).toBe(true);

    pages.more();
    flushSync();
    await vi.waitFor(() => expect(ids(pages)).toEqual(['e6', 'e5', 'e4', 'e3']));
    expect(pages.hasMore).toBe(true);

    pages.more();
    flushSync();
    await vi.waitFor(() => expect(ids(pages)).toEqual(['e6', 'e5', 'e4', 'e3', 'e2', 'e1']));
    expect(pages.hasMore).toBe(false);
  });

  it('leaves no gap when a save adds an entry while older pages are loaded', async () => {
    const pages = mount();
    await vi.waitFor(() => expect(ids(pages)).toEqual(['e6', 'e5']));
    pages.more();
    flushSync();
    await vi.waitFor(() => expect(ids(pages)).toEqual(['e6', 'e5', 'e4', 'e3']));

    // A save adds e7 at the head, which pushes e5 off the first page, and invalidates the object's history.
    stored = ['e7', ...stored];
    await client.invalidateQueries({ queryKey: ['history'] });
    await vi.waitFor(() => expect(ids(pages)?.[0]).toBe('e7'));
    await settled();

    // Whatever is shown is the newest entries with nothing missing between them, so e5 is still there.
    const shown = ids(pages)!;
    expect(shown.length).toBeGreaterThanOrEqual(pageSize);
    expect(shown).toEqual(stored.slice(0, shown.length));
    expect(pages.hasMore).toBe(true);

    // And loading more carries on from where the list ends.
    pages.more();
    flushSync();
    await vi.waitFor(() => expect(ids(pages)).toEqual(stored.slice(0, shown.length + pageSize)));
  });
});
