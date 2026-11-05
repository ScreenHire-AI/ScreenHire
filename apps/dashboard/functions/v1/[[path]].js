// Cloudflare Pages Function: the dashboard's /v1 path, forwarded to the API (docs/operations/deploy.md).
//
// The dashboard calls the API same-origin, because the session cookie belongs to the dashboard's host and has no Domain
// (ScreenHire:Domains:CookieDomain stays empty). Pages cannot rewrite to another origin from _redirects, so this
// function does it. It passes the method, the body, the cookies and the WebSocket upgrade of the real-time hub through,
// and hands redirects (the sign-in round trip) to the browser as they are. The visitor's address goes to the API in
// X-Forwarded-For, where ScreenHire:Auth:TrustedProxies decides whether to believe it.
//
// API_ORIGIN is a Pages environment variable, e.g. https://api.example.org (no path, no trailing slash).
// This file runs only on Cloudflare. No task can run it, so the first deployment checks it (deploy.md, "Check it works").

/** @param {{ request: Request, env: { API_ORIGIN?: string } }} context */
export async function onRequest({ request, env }) {
  if (!env.API_ORIGIN) {
    return new Response('API_ORIGIN is not set for this Pages project.', { status: 500 });
  }

  const incoming = new URL(request.url);
  const target = new URL(incoming.pathname + incoming.search, env.API_ORIGIN);

  const headers = new Headers(request.headers);
  const visitor = request.headers.get('CF-Connecting-IP');
  if (visitor) {
    headers.set('X-Forwarded-For', visitor);
  }

  return fetch(new Request(target, { method: request.method, headers, body: request.body, redirect: 'manual' }));
}
