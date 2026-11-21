import type { TeamDetail, TeamMember } from '@screenhire/api-client';
import type { PickerOption } from '#lib/components/picker.svelte';

/**
 * Job access through each job's hiring team (ADR-0015), as the screens need it. The API applies the access itself;
 * these only decide what a screen offers.
 */

/** A team with `added` brought over, each person once, in the order they first appear. */
export const mergeTeam = (team: readonly string[], added: readonly string[]): string[] => [...new Set([...team, ...added])];

/** How many people, in words: "1 person", "3 people". */
export const peopleWords = (count: number): string => (count === 1 ? '1 person' : `${count} people`);

/**
 * The named teams a picker offers to add to a hiring team, with how many people each brings. Adding one copies its people
 * as they are now: the job keeps them whatever happens to the team later.
 */
export const teamOptions = (teams: readonly TeamDetail[]): PickerOption[] =>
  teams.map((team) => ({ id: team.id, label: team.name, hint: peopleWords(team.members.length) }));

/** The member ids a named team adds, or none for a team that is not in the list. */
export const teamMemberIds = (teams: readonly TeamDetail[], teamId: string): string[] =>
  teams.find((team) => team.id === teamId)?.members.map((person) => person.memberId) ?? [];

/** How a person's job access reads beside their name on a hiring team. */
export const accessWords = (member: Pick<TeamMember, 'seesAllJobs'>): string => (member.seesAllJobs ? 'All jobs' : "Only jobs they're on");

/**
 * The teammates who can see a candidate on the job with `team`: everyone who sees all jobs, and the people on its
 * hiring team. A mention names only them; the API refuses anyone else.
 */
export const membersWhoSee = (members: readonly TeamMember[], team: readonly string[] | undefined): TeamMember[] =>
  members.filter((member) => member.seesAllJobs || (team?.includes(member.id) ?? false));
