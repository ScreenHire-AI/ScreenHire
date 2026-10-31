import { defineMiddleware } from 'astro:middleware';
import { securityPolicy } from './lib/embed';
import { hostTenant } from './lib/host';
import { isFrameworkPath, tenantRoute } from './lib/tenancy';

/**
 * Host-based tenant routing and the response headers every careers page gets. On a tenant host
 * (`brightwater.{CareersDomain}`) a request for `/jobs/x` renders `/brightwater/jobs/x`; anywhere else the organization
 * is in the path.
 */
export const onRequest = defineMiddleware(async (context, next) => {
  const tenant = hostTenant(context.request);
  const path = context.url.pathname;
  const route = tenant && !isFrameworkPath(path) ? tenantRoute(path, tenant) : null;
  if (route && 'canonical' in route && ['GET', 'HEAD'].includes(context.request.method)) {
    // Relative, so it stays on the tenant host the browser used (Astro's own URL may name another host).
    return new Response(null, { status: 301, headers: { Location: `${route.canonical}${context.url.search}` } });
  }

  // A tenant-prefixed path that is not a GET (a form posted from a path-style page) is served as it is.
  const response = route && 'render' in route ? await next(`${route.render}${context.url.search}`) : await next();

  // Directives a <meta> policy cannot carry. Astro's own policy (astro.config.mjs) covers scripts and styles; appended,
  // not set, so neither replaces the other, and browsers enforce both. Which sites may frame the page is the
  // organization's list, which its page put in locals while it rendered (lib/embed.ts); this is the only place the
  // directive is written, because with two of them the stricter would win.
  response.headers.append('Content-Security-Policy', securityPolicy(context.locals.embedding));
  response.headers.set('X-Content-Type-Options', 'nosniff');
  response.headers.set('Referrer-Policy', 'same-origin');
  // Careers pages never use these; saying so in the browser too backs up the privacy notice.
  response.headers.set('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
  return response;
});
