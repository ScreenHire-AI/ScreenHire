import type { TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { accessWords, membersWhoSee, mergeTeam } from './hiring-team.js';

const person = (id: string, seesAllJobs: boolean): TeamMember => ({
  id,
  userId: `user-${id}`,
  email: `${id}@example.com`,
  displayName: id,
  role: seesAllJobs ? 'Recruiter' : 'Viewer',
  customRoleId: null,
  roleName: seesAllJobs ? 'Recruiter' : 'Viewer',
  joinedAt: '2026-10-01T09:00:00Z',
  jobAccess: seesAllJobs ? 'All' : 'HiringTeamOnly',
  seesAllJobs,
  jobCount: 0,
});

describe('hiring teams', () => {
  it('brings over a copied team without doubling anyone, keeping the order', () => {
    expect(mergeTeam(['a', 'b'], ['b', 'c', 'a', 'd'])).toEqual(['a', 'b', 'c', 'd']);
  });

  it('names a member by what they see', () => {
    expect(accessWords({ seesAllJobs: true })).toBe('All jobs');
    expect(accessWords({ seesAllJobs: false })).toBe("Only jobs they're on");
  });

  it('lets a note mention everyone who sees all jobs, and the others only from the hiring team', () => {
    const all = person('all', true);
    const onTeam = person('on-team', false);
    const offTeam = person('off-team', false);

    expect(membersWhoSee([all, onTeam, offTeam], ['on-team'])).toEqual([all, onTeam]);
    expect(membersWhoSee([all, onTeam, offTeam], undefined)).toEqual([all]);
  });
});
