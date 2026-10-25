import type { CurrentUser, Permission } from '@screenhire/api-client';

/**
 * Whether the signed-in person's role has `permission` in the organization they work in (ADR-0014). The API checks the
 * same permissions on every request and stays the judge; screens use this to leave out controls the person cannot use
 * (design system A3), whether their role is built in or one their organization made.
 */
export function can(user: CurrentUser | null | undefined, permission: Permission): boolean {
  return user?.organization?.permissions.includes(permission) ?? false;
}
