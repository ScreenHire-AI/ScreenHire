import type { ApplicationTaskKind, RoleSummary, TeamMember } from '@screenhire/api-client';
import { roleKey } from '#lib/settings.js';

/**
 * Team members an AssignReviewer action may name, in the team list's order: those whose role can complete the task it
 * makes (Complete tasks), built-in or custom. A Viewer could not complete it. Nobody until the roles have loaded.
 */
export const assignableMembers = (members: readonly TeamMember[], roles: readonly RoleSummary[]): TeamMember[] =>
  members.filter((member) => roles.find((role) => roleKey(role) === roleKey(member))?.permissions.includes('CompleteTasks'));

/**
 * A person as the screen names them: "you" for the signed-in person, a member's display name, or "a former teammate"
 * for someone no longer on the team (their notes and tasks stay).
 */
export function personName(userId: string, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): string {
  if (userId === currentUserId) return 'you';
  return members?.find((member) => member.userId === userId)?.displayName ?? 'a former teammate';
}

/** Words for each kind of task an automation makes. */
export const taskKindLabels: Record<ApplicationTaskKind, string> = {
  Review: 'Review',
  Task: 'To-do',
  RejectionReview: 'Rejection review',
};

/** Who a task is for: "For you", "For the team" or "For Avery Quinn". */
export function assigneeText(assigneeUserId: string | null, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): string {
  if (!assigneeUserId) return 'For the team';
  const name = personName(assigneeUserId, members, currentUserId);
  return `For ${name}`;
}
