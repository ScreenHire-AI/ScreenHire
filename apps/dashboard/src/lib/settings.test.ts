import type { OrganizationRole } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { assignableRoles, isAdministrator, mayAssign, mayManage, roles } from './settings.js';

// The expectations follow TeamPolicy.cs (src/ScreenHire.Domain/Organizations): Owners manage everyone; Admins manage
// Admins, Recruiters and Viewers but cannot make or touch an Owner; Recruiters and Viewers manage nobody.

describe('mayAssign', () => {
  const table: Record<OrganizationRole, Record<OrganizationRole, boolean>> = {
    Owner: { Owner: true, Admin: true, Recruiter: true, Viewer: true },
    Admin: { Owner: false, Admin: true, Recruiter: true, Viewer: true },
    Recruiter: { Owner: false, Admin: false, Recruiter: false, Viewer: false },
    Viewer: { Owner: false, Admin: false, Recruiter: false, Viewer: false },
  };

  for (const actor of roles) {
    for (const role of roles) {
      it(`${table[actor][role] ? 'lets' : 'does not let'} ${actor === 'Admin' ? 'an' : 'a'} ${actor} give the ${role} role`, () => {
        expect(mayAssign(actor, role)).toBe(table[actor][role]);
      });
    }
  }
});

describe('mayManage', () => {
  it('lets an Owner manage a member of any role', () => {
    expect(roles.map((role) => mayManage('Owner', role))).toEqual([true, true, true, true]);
  });

  it('keeps an Admin away from Owners but lets them manage everyone else', () => {
    expect(mayManage('Admin', 'Owner')).toBe(false);
    expect(mayManage('Admin', 'Admin')).toBe(true);
    expect(mayManage('Admin', 'Recruiter')).toBe(true);
    expect(mayManage('Admin', 'Viewer')).toBe(true);
  });

  it('lets Recruiters and Viewers manage nobody', () => {
    for (const actor of ['Recruiter', 'Viewer'] as const) {
      expect(roles.map((role) => mayManage(actor, role))).toEqual([false, false, false, false]);
    }
  });
});

describe('assignableRoles', () => {
  it('offers an Owner every role, most powerful first', () => {
    expect(assignableRoles('Owner')).toEqual(['Owner', 'Admin', 'Recruiter', 'Viewer']);
  });

  it('never offers an Admin the Owner role', () => {
    expect(assignableRoles('Admin')).toEqual(['Admin', 'Recruiter', 'Viewer']);
  });

  it('offers a Recruiter or a Viewer nothing', () => {
    expect(assignableRoles('Recruiter')).toEqual([]);
    expect(assignableRoles('Viewer')).toEqual([]);
  });
});

describe('isAdministrator', () => {
  it('is true for Owners and Admins', () => {
    expect(isAdministrator('Owner')).toBe(true);
    expect(isAdministrator('Admin')).toBe(true);
  });

  it('is false for Recruiters, Viewers and a missing role', () => {
    expect(isAdministrator('Recruiter')).toBe(false);
    expect(isAdministrator('Viewer')).toBe(false);
    expect(isAdministrator(null)).toBe(false);
    expect(isAdministrator(undefined)).toBe(false);
  });
});
