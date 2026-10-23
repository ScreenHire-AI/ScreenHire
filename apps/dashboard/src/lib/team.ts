import type { ApplicationTaskKind, OrganizationRole, TeamMember } from '@screenhire/api-client';

/** Roles that can change hiring data, so the ones a task can go to: a Viewer could not complete it. */
const assignableRoles: readonly OrganizationRole[] = ['Owner', 'Admin', 'Recruiter'];

/** Team members an AssignReviewer action may name, in the team list's order. */
export const assignableMembers = (members: readonly TeamMember[]): TeamMember[] =>
  members.filter((member) => assignableRoles.includes(member.role));

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
