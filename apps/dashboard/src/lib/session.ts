import { signOut } from '@screenhire/api-client';
import { goto } from '$app/navigation';

/** Ends the session and goes to the sign-in page, re-running the auth guard so no signed-in data stays on screen. */
export async function signOutAndRedirect(): Promise<void> {
  await signOut();
  await goto('/sign-in', { invalidateAll: true });
}
