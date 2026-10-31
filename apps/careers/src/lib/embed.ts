// The embeddable widget inside the careers pages (docs/architecture/careers-widget.md). Pure functions: no environment,
// so they unit-test cleanly.
//
// An organization lists the sites allowed to show its careers pages in a frame (`embedOrigins` on the public responses).
// The snippet's script frames the board at `?embed={the site's origin}`. A page whose `embed` value is on the list is in
// embed mode: its links keep the value, it sizes its frame, it narrows `frame-ancestors` to that one site, and the apply
// flow sends it with the application as the site the candidate applied from.

/** What a page knows about framing once it has its organization: the listed sites, and the one it is embedded in. */
export interface Embedding {
  origins: readonly string[];
  embed: string | null;
}

/** An origin a CSP host source can say, as the API keeps them: scheme, a name and an optional port, nothing else. */
const ORIGIN = /^https?:\/\/[a-z0-9.-]+(:\d{1,5})?$/;

export const isOrigin = (value: string | null | undefined): value is string => typeof value === 'string' && ORIGIN.test(value);

/** The site a page is embedded in: `value` when it is one of the organization's listed sites, otherwise null. */
export const embedOrigin = (value: string | null | undefined, allowed: readonly string[]): string | null =>
  isOrigin(value) && allowed.includes(value) ? value : null;

/**
 * The page's `frame-ancestors` sources. Embedded in a listed site, only that site may frame it, so the site an
 * application records is the page's real parent; otherwise any listed site may. Anything that is not a plain origin is
 * left out, so nothing from the API can add a directive to the header.
 */
export function frameAncestors(embedding: Embedding | undefined): string {
  const sites = embedding?.embed ? [embedding.embed] : (embedding?.origins ?? []);
  return ["'self'", ...sites.filter(isOrigin)].join(' ');
}

/** The middleware's policy for a page: who may frame it, and the directives every careers page shares. */
export const securityPolicy = (embedding: Embedding | undefined): string =>
  `frame-ancestors ${frameAncestors(embedding)}; base-uri 'none'; object-src 'none'; form-action 'self'`;

/** `href` with the page's embed mode kept (`embed=`), or as it is outside embed mode. */
export function withEmbed(href: string, embed: string | null | undefined): string {
  if (!embed) return href;
  const hash = href.indexOf('#');
  const [path, fragment] = hash === -1 ? [href, ''] : [href.slice(0, hash), href.slice(hash)];
  const query = new URLSearchParams({ embed }).toString();
  return `${path}${path.includes('?') ? '&' : '?'}${query}${fragment}`;
}

/**
 * A job description's links opened in a new tab, for the widget: another site would otherwise try to load inside the
 * frame, where most refuse to show. The description is the API's sanitized HTML, whose links are only
 * `<a href="..." rel="nofollow noopener noreferrer">` (RichText.cs), so a plain rewrite of the tag is enough.
 */
export const newTabLinks = (html: string): string => html.replace(/<a (?![^>]*\btarget=)/g, '<a target="_blank" ');

/**
 * Sets the page's embedding for the middleware's `frame-ancestors` and the components that build links, and returns the
 * site the page is embedded in. Pages call it in their own frontmatter, which runs before the middleware reads it.
 */
export function useEmbedding(locals: App.Locals, url: URL, origins: readonly string[]): string | null {
  const embed = embedOrigin(url.searchParams.get('embed'), origins);
  locals.embedding = { origins, embed };
  return embed;
}

/** The site the page is embedded in, as its page set it, or null. */
export const embedOf = (locals: App.Locals): string | null => locals.embedding?.embed ?? null;
