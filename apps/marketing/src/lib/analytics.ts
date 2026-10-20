// Cookieless analytics for the marketing site only (ADR-0003; the carve-out is in .claude/rules/astro-public.md).
// Off unless PUBLIC_ANALYTICS names a provider and PUBLIC_ANALYTICS_SCRIPT_PATH is a site-relative path, so the
// script is always served first-party (a self-hosted instance or a same-origin proxy) and never from a third party.

export type AnalyticsProvider = 'off' | 'plausible' | 'umami';

export interface AnalyticsConfig {
  provider: AnalyticsProvider;
  scriptPath?: string | undefined;
  siteId?: string | undefined;
  /** The site host, Plausible's default data-domain. */
  siteHost: string;
}

export interface AnalyticsScript {
  src: string;
  attributes: Record<string, string>;
}

/** The script tag to render, or null when analytics is off or not configured to be first-party. */
export function analyticsScript(config: AnalyticsConfig): AnalyticsScript | null {
  const { provider, scriptPath, siteId, siteHost } = config;
  // A site-relative path ("/stats/script.js"), never a URL or a protocol-relative "//host/..." path.
  if (provider === 'off' || !scriptPath || !/^\/(?!\/)/.test(scriptPath)) {
    return null;
  }

  if (provider === 'plausible') {
    return { src: scriptPath, attributes: { 'data-domain': siteId ?? siteHost } };
  }

  return siteId ? { src: scriptPath, attributes: { 'data-website-id': siteId } } : null;
}
