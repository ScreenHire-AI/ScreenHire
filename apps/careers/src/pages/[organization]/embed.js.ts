// The careers widget's script (src/embed/embed.js, docs/architecture/careers-widget.md), at `{careers board}/embed.js`,
// which is the address the embed snippet names. The same bytes for every organization: the script reads the board from
// its own address, so this route makes no API call. On a tenant host the middleware's rewrite brings `/embed.js` here.
import type { APIRoute } from 'astro';
import script from '../../embed/embed.js?raw';

export const GET: APIRoute = () =>
  new Response(script, {
    headers: {
      'Content-Type': 'text/javascript; charset=utf-8',
      // A fixed address that changes with a release, so it is cached for an hour rather than for good.
      'Cache-Control': 'public, max-age=3600',
    },
  });
