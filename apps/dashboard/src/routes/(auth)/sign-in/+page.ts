import { getSignInMethods } from '@screenhire/api-client';
import { error } from '@sveltejs/kit';
import { returnUrlParameter, safeReturnUrl } from '#lib/auth.js';
import type { PageLoad } from './$types';

/** Why a provider sign-in came back here, from the API's `?error=` redirect. */
const errorMessages: Record<string, string> = {
  provider: "We couldn't sign you in with that provider. Try again, or use an email link.",
  'email-unverified':
    "Your provider didn't confirm your email address, so we can't match it to an account. Use an email link instead.",
};

export const load: PageLoad = async ({ url }) => {
  const response = await getSignInMethods();
  if (response.status !== 200) {
    error(response.status, 'Could not load the ways to sign in.');
  }

  const reason = url.searchParams.get('error');
  return {
    methods: response.data,
    returnUrl: safeReturnUrl(url.searchParams.get(returnUrlParameter)),
    errorMessage: reason ? (errorMessages[reason] ?? errorMessages.provider) : undefined,
  };
};
