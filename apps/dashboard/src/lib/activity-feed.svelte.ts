import type { ActivityEntry, ActivityKind, ActivityPage, ActivityScope } from '@screenhire/api-client';
import { getGetActivityQueryOptions } from '@screenhire/api-client/svelte-query';
import { createQueries } from '@tanstack/svelte-query';
import { preferences } from '#lib/preferences.svelte.js';
import { describeError } from '#lib/query.js';

/**
 * The workspace's Activity feed, the bell's second tab (docs/architecture/notifications.md, "Activity"): every change to an
 * application or a job the person may see, newest first, from `GET /v1/activity`, a page at a time. It reads while the
 * bell's panel is open (`enabled`), and live events refetch it then (`activityPath` in `#lib/realtime.ts`). The filters,
 * whose jobs and which kinds, are the person's in this browser.
 */

/** The kinds the feed filters by, in the order of the filter. */
export const feedKinds: readonly { value: ActivityKind; label: string }[] = [
  { value: 'Moves', label: 'Moves' },
  { value: 'Applications', label: 'Applications' },
  { value: 'Messages', label: 'Messages' },
  { value: 'Interviews', label: 'Interviews' },
  { value: 'Scorecards', label: 'Scorecards' },
  { value: 'Notes', label: 'Notes and tags' },
  { value: 'Tasks', label: 'Tasks' },
  { value: 'Jobs', label: 'Jobs' },
];

const scopes: readonly ActivityScope[] = ['MyJobs', 'Everything'];

/** How the count of changes since the person last looked reads: "99+" past 99. */
export const unseenText = (count: number): string => (count > 99 ? '99+' : String(count));

export interface ActivityFeed {
  /** The loaded entries, newest first, or undefined until the first page arrives. */
  readonly entries: ActivityEntry[] | undefined;
  /** The first page: when the person last looked and how many changes came since. */
  readonly first: ActivityPage | undefined;
  /**
   * The first page as the server has it now, or undefined while it is read again: a panel opened a second time shows the
   * last visit's cached page until the refetch lands, and that page's count is the last visit's.
   */
  readonly fresh: ActivityPage | undefined;
  readonly error: string | undefined;
  readonly hasMore: boolean;
  readonly loadingMore: boolean;
  /** Whose jobs the feed covers: the one chosen here, else the person's own choice from Your account. */
  readonly scope: ActivityScope | undefined;
  readonly kinds: readonly ActivityKind[];
  setScope(scope: ActivityScope): void;
  /** Shows only these kinds; none shows every kind. */
  setKinds(kinds: readonly string[]): void;
  more(): void;
  refetch(): void;
}

export function createActivityFeed(enabled: () => boolean): ActivityFeed {
  const saved = preferences.activityFilters();
  let scope = $state<ActivityScope | undefined>(scopes.find((value) => value === saved.scope));
  let kinds = $state<ActivityKind[]>(feedKinds.map((kind) => kind.value).filter((kind) => saved.kinds.includes(kind)));
  let cursors = $state<(string | undefined)[]>([undefined]);

  const queries = createQueries(() => ({
    queries: cursors.map((before) => ({
      ...getGetActivityQueryOptions({
        ...(scope ? { scope } : {}),
        ...(kinds.length > 0 ? { kinds } : {}),
        ...(before ? { before } : {}),
      }),
      enabled: enabled(),
    })),
  }));

  // Closing the panel goes back to the first page.
  $effect(() => {
    if (!enabled()) cursors = [undefined];
  });
  // A change that arrives while older pages are loaded refetches them all, and the first page then starts at the new entry:
  // the second page's cursor still points at the old first page's end, so go back to the first page rather than leave a gap.
  $effect(() => {
    const next = queries[0]?.data?.data.nextBefore;
    if (next !== undefined && cursors.length > 1 && next !== cursors[1]) cursors = [undefined];
  });

  const last = () => queries[queries.length - 1];
  const save = () => preferences.setActivityFilters({ scope, kinds: [...kinds] });

  return {
    get entries() {
      if (!queries[0]?.data) return undefined;
      return queries.flatMap((query) => query.data?.data.entries ?? []);
    },
    get first() {
      return queries[0]?.data?.data;
    },
    get fresh() {
      return queries[0]?.isFetching ? undefined : queries[0]?.data?.data;
    },
    get error() {
      const failed = queries.find((query) => query.isError);
      return failed ? describeError(failed.error, 'Could not load the activity') : undefined;
    },
    get hasMore() {
      return Boolean(last()?.data?.data.nextBefore);
    },
    get loadingMore() {
      return cursors.length > 1 && Boolean(last()?.isPending);
    },
    get scope() {
      return scope ?? queries[0]?.data?.data.scope;
    },
    get kinds() {
      return kinds;
    },
    setScope(value) {
      scope = value;
      cursors = [undefined];
      save();
    },
    setKinds(values) {
      kinds = feedKinds.map((kind) => kind.value).filter((kind) => values.includes(kind));
      cursors = [undefined];
      save();
    },
    more() {
      const next = last()?.data?.data.nextBefore;
      if (next && !cursors.includes(next)) cursors = [...cursors, next];
    },
    refetch() {
      for (const query of queries) void query.refetch();
    },
  };
}

/**
 * A request to open the bell at its Activity tab: the A shortcut counts one up, and the bell on the page opens its panel
 * on Activity when the count passes the one it saw when it mounted, as `setupPanel` does for the setup steps.
 */
class ActivityPanel {
  requests = $state(0);

  open(): void {
    this.requests += 1;
  }
}

export const activityPanel = new ActivityPanel();
