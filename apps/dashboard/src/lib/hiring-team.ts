import type { TeamMember } from '@screenhire/api-client';

/**
 * Job access through each job's hiring team (ADR-0015), as the screens need it. The API applies the access itself;
 * these only decide what a screen offers.
 */

/** A team with `added` brought over, each person once, in the order they first appear. */
export const mergeTeam = (team: readonly string[], added: readonly string[]): string[] => [...new Set([...team, ...added])];

/** How a person's job access reads beside their name on a hiring team. */
export const accessWords = (member: Pick<TeamMember, 'seesAllJobs'>): string => (member.seesAllJobs ? 'All jobs' : "Only jobs they're on");

/**
 * The teammates who can see a candidate on the job with `team`: everyone who sees all jobs, and the people on its
 * hiring team. A mention names only them; the API refuses anyone else.
 */
export const membersWhoSee = (members: readonly TeamMember[], team: readonly string[] | undefined): TeamMember[] =>
  members.filter((member) => member.seesAllJobs || (team?.includes(member.id) ?? false));
