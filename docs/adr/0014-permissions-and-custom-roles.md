---
type: Decision
title: "ADR-0014: Permissions, not role names, and custom roles"
description: "The server checks the permission catalogue on every route instead of role names; organizations make custom roles by copying one; a member holds one role, built-in or custom; nobody gives a role with more access than their own; Owner-only permissions stay with Owners; API keys keep their scopes and are capped by their creator."
tags: [adr, roles, permissions, auth]
---
# ADR-0014: Permissions, not role names, and custom roles

- **Status:** Accepted (2026-10-25)
- **Deciders:** Owner
- **Builds on:** ADR-0005 (tenant isolation), ADR-0011 (API key scopes)
- **Detail:** `docs/architecture/sign-in-and-tenancy.md`, "Organizations and roles"

## Context

The API checked role names. `RequireHiringRole()` let Owners, Admins and Recruiters change hiring data,
`RequireOrganizationAdmin()` let Owners and Admins change the organization, and `TeamPolicy` compared role names to
decide who may give which role. The permission catalogue (`RolePermissions.cs`) described those checks but enforced
nothing.

Organizations use roles differently. An interview panel member should see candidates, add notes and fill in
scorecards without being able to move candidates or email them. Four fixed roles cannot express that, and a fifth
fixed role would not fit the next organization either.

## Decision

1. **The server checks permissions.** Every route that changes data calls `RequirePermission(Permission.X)`, an
   endpoint filter that reads the member's role on each request and refuses with 403 "Not in your role", naming the
   permission. `RolePermissionsTests` reads every route from `apps/api/Endpoints` and holds each to its permission. The
   four built-in roles hold exactly the sets the role-name checks allowed, written out in that test, so no existing
   member's access changed. Reads stay open to every member: See jobs and candidates is in every role. Job access
   (roadmap item 8, second half) narrows which jobs.
2. **A member has one role.** `organization_members.role` is a built-in role or `Custom`, and `custom_role_id` names the
   custom role. A check constraint keeps the two consistent, and a composite foreign key on `(organization_id,
   custom_role_id)` keeps a member from pointing at another organization's role, because foreign key checks bypass
   row-level security. Existing rows got a NULL `custom_role_id` and kept their role.
3. **Custom roles are copies.** An Owner or Admin makes one by copying a role, built-in or custom, then changes what it
   may do on its own page. Built-in roles never change. Names are unique in an organization ignoring case, and never a
   built-in role's name. A role somebody has, or an open invitation offers, cannot be deleted; the refusal says how
   many. Every change lands in the membership log, and the role's change history says what changed.
4. **Owner-only permissions never go into a custom role.** Today that is Manage Owners. The catalogue flags it; a
   request that includes it is refused, and reading a custom role drops it again, so a stored row can never grant it.
   A custom role can hold everything else, Admin's permissions included.
5. **Nobody gives more access than they have.** `TeamPolicy` is a subset rule: an actor with Manage the team may give,
   take away, make, change or delete a role only when that role holds nothing their own role does not. For the
   built-in roles this is the old rule (Owners manage everyone, Admins everyone but Owners, Recruiters and Viewers
   nobody). Making and changing roles uses Manage the team; no new permission was added.
6. **API keys keep their scopes (ADR-0011).** A Write key passes exactly the hiring writes, `RolePermissions.HiringWrites`,
   and nothing else, as before. Creating a Write key needs every hiring write in the creator's role, so a custom role
   with Manage API keys cannot mint a key that does more than it may.
7. **Invitations can offer a custom role** with no foreign key on `custom_role_id`. The id is only resolved under
   row-level security, when the invitation is written and again when it is accepted, so no foreign key check bypasses
   it, and closed invitations cannot keep a role from being deleted forever. Open invitations block the deletion.

## Consequences

- The dashboard reads the member's permissions from `GET /v1/auth/me` and shows each control by the permission its
  route needs. The API stays the judge.
- Run automations is an accepted gap. A manual automation runs the actions the job's workflow defines, which may move
  or email the candidate, as ScreenHire. The people who edit jobs and hiring templates decide those actions, so running
  one does not need Move candidates or Email candidates.
- The organization switcher names a custom role only in the organization the user works in. Row-level security keeps
  another organization's roles from them until they switch.
- A role change, or a change to what a custom role allows, applies on the member's next request. No live event pushes
  it to an open dashboard; its controls follow on the next load of `me`.
- Job access through each job's hiring team (roadmap item 8, second half) adds a scope to the same membership and the
  same query filters.
