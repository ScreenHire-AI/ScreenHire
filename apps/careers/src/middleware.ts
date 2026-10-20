import { defineMiddleware } from 'astro:middleware';
import { hostTenant } from './lib/host';
import { isFrameworkPath } from './lib/tenancy';

/**
 * Host-based tenant routing and the response headers every careers page gets. On a tenant host
 * (`brightwater.{CareersDomain}`) a request for `/jobs/x` renders `/brightwater/jobs/x`; anywhere else the organization
 * is in the path.
 */
export const onRequest = defineMiddleware(async (context, next) => {
  const tenant = hostTenant(context.request);

  const path = context.url.pathname;
  const response = tenant && !isFrameworkPath(path) ? await next(`/${tenant}${path === '/' ? '' : path}${context.url.search}`) : await next();

  // Directives a <meta> policy cannot carry. Astro's own policy (astro.config.mjs) covers scripts and styles; appended,
  // not set, so neither replaces the other, and browsers enforce both.
  response.headers.append('Content-Security-Policy', "frame-ancestors 'self'; base-uri 'none'; object-src 'none'; form-action 'self'");
  response.headers.set('X-Content-Type-Options', 'nosniff');
  response.headers.set('Referrer-Policy', 'same-origin');
  // Careers pages never use these; saying so in the browser too backs up the privacy notice.
  response.headers.set('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
  return response;
});
