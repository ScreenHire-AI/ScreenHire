import Building2Icon from '@lucide/svelte/icons/building-2';
import KeyRoundIcon from '@lucide/svelte/icons/key-round';
import MailIcon from '@lucide/svelte/icons/mail';
import PaletteIcon from '@lucide/svelte/icons/palette';
import ShieldUserIcon from '@lucide/svelte/icons/shield-user';
import ShieldCheckIcon from '@lucide/svelte/icons/shield-check';
import UsersRoundIcon from '@lucide/svelte/icons/users-round';
import WebhookIcon from '@lucide/svelte/icons/webhook';
import type { OrganizationRole } from '@screenhire/api-client';
import type { LucideIcon } from '#lib/shell.js';

/**
 * The Settings menu's pages (design system section 11), for Owners and Admins only: the sidebar's one Settings item opens
 * the first, and the command palette lists them all. Kept apart from `sections`, which every member sees and which the G
 * chords read. Email templates stay last until the Templates page takes them (section 22).
 */
export const settingsSections: readonly { label: string; href: string; icon: LucideIcon }[] = [
  { label: 'Organization', href: '/settings/organization', icon: Building2Icon },
  { label: 'Team', href: '/settings/team', icon: UsersRoundIcon },
  { label: 'Roles', href: '/settings/roles', icon: ShieldUserIcon },
  { label: 'Branding', href: '/settings/branding', icon: PaletteIcon },
  { label: 'Integrity', href: '/settings/integrity', icon: ShieldCheckIcon },
  { label: 'API keys', href: '/settings/api-keys', icon: KeyRoundIcon },
  { label: 'Webhooks', href: '/settings/webhooks', icon: WebhookIcon },
  { label: 'Email templates', href: '/settings/email-templates', icon: MailIcon },
];


/** Whether the role manages the organization's settings: the API's `RequireOrganizationAdmin`. */
export const isAdministrator = (role: OrganizationRole | null | undefined): boolean => role === 'Owner' || role === 'Admin';

/** Every role, most powerful first. */
export const roles: readonly OrganizationRole[] = ['Owner', 'Admin', 'Recruiter', 'Viewer'];

// The API's TeamPolicy (src/ScreenHire.Domain/Organizations/TeamPolicy.cs), so the page offers only what it allows. The
// API stays the judge: it refuses anything else with 403 "Owners only", and the last Owner with 409.

/** Whether `actor` may invite someone as, or move someone into, `role`. */
export const mayAssign = (actor: OrganizationRole, role: OrganizationRole): boolean =>
  actor === 'Owner' || (actor === 'Admin' && role !== 'Owner');

/** The roles `actor` may give. */
export const assignableRoles = (actor: OrganizationRole): OrganizationRole[] => roles.filter((role) => mayAssign(actor, role));

/** Whether `actor` may change the role of, or remove, a member who has `role`. */
export const mayManage = (actor: OrganizationRole, role: OrganizationRole): boolean => mayAssign(actor, role);
