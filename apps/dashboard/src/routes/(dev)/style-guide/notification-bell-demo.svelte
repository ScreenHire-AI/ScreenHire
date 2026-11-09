<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import {
    getGetOrganizationBrandingQueryKey,
    getGetOrganizationQueryKey,
    getGetOrganizationSendingDomainQueryKey,
    getListInvitationsQueryKey,
    getListJobsQueryKey,
    getListNotificationsQueryKey,
    getListTeamMembersQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import NotificationBell from '#lib/components/notification-bell.svelte';

  // The notification bell with sample data (design system section 11, Notification bell and Setup checklist): the style guide
  // has no API, so this example brings its own query client, filled in advance and never refetched. The organization has its
  // team invited and nothing else set up, and one alert is unread, so the panel shows both sections. Mark read and Dismiss
  // have nowhere to go here.
  let { user }: { user: CurrentUser | null | undefined } = $props();

  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } } });
  const answer = (data: unknown) => ({ status: 200, headers: new Headers(), data });
  client.setQueryData(
    getListNotificationsQueryKey(),
    answer({
      items: [
        {
          id: 'demo-notification-1',
          kind: 'sending-domain-failing',
          payload: { domain: 'brightwater.example.com' },
          link: '#notification-bell',
          createdAt: '2026-11-08T09:00:00Z',
          readAt: null,
        },
      ],
      unreadCount: 1,
    }),
  );
  client.setQueryData(getListTeamMembersQueryKey(), answer([{ id: 'demo-member-1' }, { id: 'demo-member-2' }]));
  client.setQueryData(getListInvitationsQueryKey(), answer([]));
  client.setQueryData(
    getGetOrganizationBrandingQueryKey(),
    answer({ brandColor: null, secondColor: null, headingFont: null, logo: null, favicon: null, sharingImage: null }),
  );
  client.setQueryData(getGetOrganizationQueryKey(), answer({ embedOrigins: [], questionsPerPage: DEFAULT_QUESTIONS_PER_PAGE }));
  client.setQueryData(getGetOrganizationSendingDomainQueryKey(), answer({ available: true, sandbox: true, domain: null }));
  client.setQueryData(getListJobsQueryKey(), answer([]));
  setQueryClientContext(client);
</script>

<NotificationBell {user} />
