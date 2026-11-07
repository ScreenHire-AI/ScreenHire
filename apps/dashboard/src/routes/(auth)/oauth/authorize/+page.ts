import { getOAuthAuthorizationRequest, type OAuthAuthorizationRequestInfo, type OAuthAuthorizeRequest } from '@screenhire/api-client';
import { problemText } from '#lib/oauth.js';
import type { PageLoad } from './$types';

/**
 * The OAuth parameters an assistant sent, copied as they came. The assistant writes them in snake_case in the address
 * (RFC 6749); the API's routes use camelCase, so this is the one place the two meet. `state` is the assistant's own
 * value and goes back to it untouched.
 */
export type ConsentParams = Pick<OAuthAuthorizeRequest, 'clientId' | 'redirectUri' | 'responseType' | 'codeChallenge' | 'codeChallengeMethod'> & {
  scope: string | null;
  resource: string | null;
  state: string | null;
};

export const load: PageLoad = async ({ url, parent }) => {
  // The root layout's guard sends a signed-out visitor to sign-in, and back here, before this asks the API anything.
  await parent();

  const query = url.searchParams;
  const params: ConsentParams = {
    clientId: query.get('client_id') ?? '',
    redirectUri: query.get('redirect_uri') ?? '',
    responseType: query.get('response_type') ?? '',
    scope: query.get('scope'),
    resource: query.get('resource'),
    codeChallenge: query.get('code_challenge') ?? '',
    codeChallengeMethod: query.get('code_challenge_method') ?? '',
    state: query.get('state'),
  };

  let info: OAuthAuthorizationRequestInfo | undefined;
  let problem: string | undefined;
  try {
    const response = await getOAuthAuthorizationRequest({
      clientId: params.clientId,
      redirectUri: params.redirectUri,
      responseType: params.responseType,
      scope: params.scope ?? undefined,
      resource: params.resource ?? undefined,
      codeChallenge: params.codeChallenge,
      codeChallengeMethod: params.codeChallengeMethod,
    });
    if (response.status === 200) {
      info = response.data;
    } else {
      problem = problemText(response.status, response.status === 401 ? null : response.data?.detail);
    }
  } catch {
    problem = 'We could not reach ScreenHire. Check your connection and try again.';
  }

  return { params, info, problem };
};
