import type { CurrentUser } from '@screenhire/api-client';

/** Pages a signed-out visitor may see. Everything else needs a session. */
const publicRoutes = ['/(auth)/sign-in'];
const onboardingRoute = '/(auth)/onboarding';

/** The query parameter that carries where to go after signing in. */
export const returnUrlParameter = 'returnUrl';

// Only the origin comparison matters, so any origin the dashboard can never have will do.
const probeOrigin = 'http://dashboard.invalid';

/**
 * A local path to return to after sign-in, or `/`. Never another site. The value is resolved the way the browser will
 * resolve it, because the URL parser drops tabs and newlines and reads `\` as `/`: `/\t/host` becomes `//host`.
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value?.startsWith('/')) {
    return '/';
  }
  const resolved = new URL(value, probeOrigin);
  return resolved.origin === probeOrigin ? `${resolved.pathname}${resolved.search}${resolved.hash}` : '/';
}

/**
 * Where the auth guard sends this visit, or null to let it through:
 * signed out → the sign-in page (remembering where they were going); signed in without an organization →
 * onboarding; signed in with one → away from sign-in and onboarding.
 */
export function guardRedirect(routeId: string | null, user: CurrentUser | null, url: URL): string | null {
  const isPublic = publicRoutes.some((route) => routeId === route || routeId?.startsWith(`${route}/`));
  const isOnboarding = routeId === onboardingRoute;

  if (!user) {
    if (isPublic) {
      return null;
    }
    const target = `${url.pathname}${url.search}`;
    return target === '/' ? '/sign-in' : `/sign-in?${returnUrlParameter}=${encodeURIComponent(target)}`;
  }

  if (!user.organization) {
    return isOnboarding ? null : '/onboarding';
  }

  if (isPublic || isOnboarding) {
    return safeReturnUrl(url.searchParams.get(returnUrlParameter));
  }

  return null;
}
