import { defineMiddleware } from 'astro:middleware';
import { tenantFromHost, tenantPath } from './lib/tenant-host';

// CAREERS_HOST_PATTERN is the deployment's tenant host, such as {tenant}.careers.example.com: the same value as the API's
// ScreenHire:Domains:CareersHostPattern with the careers domain filled in. Unset (local development and the tests),
// requests are routed by path as they are.
const pattern = process.env.CAREERS_HOST_PATTERN;

export const onRequest = defineMiddleware((context, next) => {
  const tenant = tenantFromHost(context.request.headers.get('host'), pattern);
  if (tenant === null) {
    return next();
  }

  const url = new URL(context.url);
  url.pathname = tenantPath(tenant, url.pathname);
  return next(url);
});
