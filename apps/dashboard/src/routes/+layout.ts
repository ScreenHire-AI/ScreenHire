import { getCurrentUser } from '@screenhire/api-client';
import { error, redirect } from '@sveltejs/kit';
import { guardRedirect } from '#lib/auth.js';
import type { LayoutLoad } from './$types';

// Single-page app: rendered in the browser and served as static files with a 200.html fallback (ADR-0006).
export const ssr = false;

/**
 * The auth guard: every navigation asks the API who is signed in (which also refreshes the antiforgery token) and
 * sends the visitor to sign-in, onboarding or the app as needed.
 */
export const load: LayoutLoad = async ({ route, url }) => {
  const response = await getCurrentUser();
  // The contract has only 200 and 401, but a proxy or a crash can answer anything.
  const status: number = response.status;
  if (status !== 200 && status !== 401) {
    error(status, 'Could not check your session.');
  }

  const user = response.status === 200 ? response.data : null;
  const destination = guardRedirect(route.id, user, url);
  if (destination) {
    redirect(307, destination);
  }

  return { user };
};
