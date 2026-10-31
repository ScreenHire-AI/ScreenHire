import type { SlackChannelEvent, SlackProblem } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** The events a channel can get, in the order the page lists them, with their labels. */
export const slackEvents: readonly { event: SlackChannelEvent; label: string }[] = [
  { event: 'ApplicationSubmitted', label: 'New application' },
  { event: 'ApplicationMoved', label: 'Candidate moved' },
  { event: 'AutomationFailed', label: 'Automation failed' },
  { event: 'JobPublished', label: 'Job published' },
];

/** What is wrong, and what fixes it, for each reason Slack stopped taking ScreenHire's messages (E2). */
export const slackProblems: Record<NonNullable<SlackProblem>, string> = {
  Disconnected: 'Slack no longer accepts ScreenHire. Reconnect Slack to keep posting.',
  ChannelNotFound: 'Slack cannot find the channel. Pick another one.',
  NotInChannel: 'ScreenHire is not in the channel. Invite the app to it in Slack, or pick another one.',
  ChannelArchived: 'The channel is archived. Pick another one.',
  CannotPost: "The channel's settings stop ScreenHire posting there. Change them in Slack, or pick another channel.",
  CannotMessage: 'Slack would not deliver your direct messages. Link your account again.',
};

/** The workspace's status beside the page title: connected is calm, a problem needs fixing (T1, T2). */
export function workspaceStatus(problem: SlackProblem | null | undefined): { status: StatusName; label: string } {
  if (problem === 'Disconnected') return { status: 'needsFixes', label: 'Disconnected' };
  return problem ? { status: 'needsFixes', label: 'Not posting' } : { status: 'done', label: 'Connected' };
}

/** How an OAuth round trip to Slack ended, from the callback's `?slack=` value. */
export type SlackOutcome = 'connected' | 'linked' | 'denied' | 'expired' | 'wrong-workspace' | 'failed';

const failures: Record<Exclude<SlackOutcome, 'connected' | 'linked'>, string> = {
  denied: 'Slack was not connected: it was cancelled on Slack.',
  expired: 'That took too long. Try again.',
  'wrong-workspace': "That Slack account is in another workspace. Sign in to your team's workspace and try again.",
  failed: 'Slack could not be reached. Try again.',
};

/** The words for a failed round trip, or undefined for a success or no outcome at all. */
export function slackFailure(outcome: string | null): string | undefined {
  return outcome !== null && Object.hasOwn(failures, outcome) ? failures[outcome as keyof typeof failures] : undefined;
}

/** Whether `?slack=` holds a round trip's outcome, success or not. */
export const isSlackOutcome = (value: string | null): value is SlackOutcome =>
  value === 'connected' || value === 'linked' || slackFailure(value) !== undefined;
