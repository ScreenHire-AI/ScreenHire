import type { TeamMember } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { interviewerRoleId, roleCatalogue } from '../test-support/fixtures.js';
import { assigneeText, membersWith, personName } from './team.js';

const member = (userId: string, displayName: string, role: TeamMember['role']): TeamMember => ({
  id: `m-${userId}`,
  userId,
  email: `${userId}@example.com`,
  displayName,
  role,
  customRoleId: role === 'Custom' ? interviewerRoleId : null,
  roleName: role === 'Custom' ? 'Interviewer' : role,
  joinedAt: '2026-10-20T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0,
});

const members = [
  member('u1', 'Avery Quinn', 'Owner'),
  member('u2', 'Robin Ashby', 'Recruiter'),
  member('u3', 'Sam Lee', 'Viewer'),
  member('u4', 'Robin Hale', 'Custom'),
];

// A catalogue where Owners and Recruiters complete tasks and nobody else does.
const roles = roleCatalogue.roles.map((role) => ({
  ...role,
  permissions: role.role === 'Owner' || role.role === 'Recruiter' ? [...role.permissions, 'CompleteTasks' as const] : role.permissions,
}));

describe('team', () => {
  it('names the signed-in person "you", members by name, and anyone else as a former teammate', () => {
    expect(personName('u1', members, 'u1')).toBe('you');
    expect(personName('u2', members, 'u1')).toBe('Robin Ashby');
    expect(personName('gone', members, 'u1')).toBe('a former teammate');
  });

  it('says who a task is for', () => {
    expect(assigneeText(null, members, 'u1')).toBe('For the team');
    expect(assigneeText('u1', members, 'u1')).toBe('For you');
    expect(assigneeText('u2', members, 'u1')).toBe('For Robin Ashby');
  });

  it('offers only members whose role has the permission, built-in or custom', () => {
    expect(membersWith(members, roles, 'CompleteTasks').map((m) => m.userId)).toEqual(['u1', 'u2']);
    const withInterviewers = roles.map((role) => (role.role === 'Custom' ? { ...role, permissions: [...role.permissions, 'CompleteTasks' as const] } : role));
    expect(membersWith(members, withInterviewers, 'CompleteTasks').map((m) => m.userId)).toEqual(['u1', 'u2', 'u4']);
    expect(membersWith(members, [], 'CompleteTasks')).toEqual([]);
  });
});
