/**
 * Where the header's Sign in and Start free buttons go. Both live in the recruiter dashboard, whose address is
 * `PUBLIC_APP_URL` (a host is never written into a page, ADR-0003). The dashboard has no separate sign-up page: a new
 * person signs in once and lands on onboarding, so sign-up is its sign-in page.
 *
 * With no dashboard address configured there is no sign-up to send anyone to, so Start free falls back to
 * `fallback` (the pricing page) and Sign in is left out.
 */
export function appLinks(appUrl: string | undefined, fallback: string): { signIn: string | undefined; signUp: string } {
  if (!appUrl) {
    return { signIn: undefined, signUp: fallback };
  }

  return { signIn: new URL('/', appUrl).href, signUp: new URL('/sign-in', appUrl).href };
}
