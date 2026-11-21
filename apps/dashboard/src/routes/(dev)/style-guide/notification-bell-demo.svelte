<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import {
    getGetActivityQueryKey,
    getGetOrganizationBrandingQueryKey,
    getGetOrganizationSendingDomainQueryKey,
    getListInvitationsQueryKey,
    getListJobsQueryKey,
    getListNotificationsQueryKey,
    getListTeamMembersQueryKey,
  } from '@screenhire/api-client/svelte-query';
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
  client.setQueryData(getGetOrganizationSendingDomainQueryKey(), answer({ available: true, sandbox: true, domain: null }));
  client.setQueryData(getListJobsQueryKey(), answer([]));
  // The Activity tab: one teammate's move since the person last looked, and the job's creation before it.
  const job = { jobId: 'demo-job-1', jobTitle: 'Senior .NET Engineer', fromStageName: null, stageName: null, followUpName: null, actorApiKeyName: null };
  client.setQueryData(
    getGetActivityQueryKey(),
    answer({
      entries: [
        {
          ...job, id: 'demo-activity-1', kind: 'Moves', occurredAt: '2026-11-08T09:30:00Z', by: 'Member', actorUserId: 'demo-user-2',
          applicationId: 'demo-application-1', candidateName: 'Taylor Brook', toStageName: 'Interview', jobChange: null,
          event: {
            id: 'demo-activity-1', type: 'StageChanged', occurredAt: '2026-11-08T09:30:00Z', actorUserId: 'demo-user-2', fromStageId: 'screening',
            toStageId: 'interview', note: null, tag: null, automationRunId: null, actorApiKeyId: null, scorecardId: null, stageId: null, recommendation: null,
          },
        },
        {
          ...job, id: 'demo-activity-2', kind: 'Jobs', occurredAt: '2026-11-07T15:00:00Z', by: 'Member', actorUserId: 'demo-user-1',
          applicationId: null, candidateName: null, toStageName: null, event: null, jobChange: { created: true, line: null, moreLines: 0 },
        },
      ],
      nextBefore: null,
      seenAt: '2026-11-08T09:00:00Z',
      unseenCount: 1,
      scope: 'MyJobs',
    }),
  );
  setQueryClientContext(client);
</script>

<NotificationBell {user} />
