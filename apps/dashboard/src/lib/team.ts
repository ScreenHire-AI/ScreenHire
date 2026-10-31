import type { ApplicationTaskKind, Permission, RoleSummary, TeamMember } from '@screenhire/api-client';
import { roleKey } from '#lib/settings.js';

/**
 * Team members whose role, built-in or custom, has `permission`, in the team list's order. Nobody until the roles have
 * loaded. A review and a requested scorecard are done by submitting a scorecard, so both name people who fill them in.
 */
export const membersWith = (members: readonly TeamMember[], roles: readonly RoleSummary[], permission: Permission): TeamMember[] =>
  members.filter((member) => roles.find((role) => roleKey(role) === roleKey(member))?.permissions.includes(permission));

/**
 * A person as the screen names them: "you" for the signed-in person, a member's display name, or "a former teammate"
 * for someone no longer on the team (their notes and tasks stay).
 */
export function personName(userId: string, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): string {
  if (userId === currentUserId) return 'you';
  return members?.find((member) => member.userId === userId)?.displayName ?? 'a former teammate';
}

/** Words for each kind of task. */
export const taskKindLabels: Record<ApplicationTaskKind, string> = {
  Review: 'Review',
  Task: 'To-do',
  RejectionReview: 'Rejection review',
  Scorecard: 'Scorecard',
};

/** Who a task is for: "For you", "For the team" or "For Avery Quinn". */
export function assigneeText(assigneeUserId: string | null, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): string {
  if (!assigneeUserId) return 'For the team';
  const name = personName(assigneeUserId, members, currentUserId);
  return `For ${name}`;
}
