import { describe, expect, it } from 'vitest';
import { embedOf, embedOrigin, frameAncestors, newTabLinks, securityPolicy, useEmbedding, withEmbed } from './embed';

const listed = ['https://www.example.com', 'http://partner.localhost'];

describe('embedOrigin', () => {
  it('keeps a site on the organization list', () => {
    expect(embedOrigin('https://www.example.com', listed)).toBe('https://www.example.com');
  });

  it('drops a site that is not listed, or anything that is not a plain origin', () => {
    expect(embedOrigin('https://other.example.org', listed)).toBeNull();
    expect(embedOrigin('https://www.example.com/', listed)).toBeNull();
    expect(embedOrigin(null, listed)).toBeNull();
    expect(embedOrigin('', [''])).toBeNull();
  });
});

describe('frameAncestors', () => {
  it("is 'self' alone when the page knows no sites", () => {
    expect(frameAncestors(undefined)).toBe("'self'");
    expect(frameAncestors({ origins: [], embed: null })).toBe("'self'");
  });

  it('lets every listed site frame a page outside the widget', () => {
    expect(frameAncestors({ origins: listed, embed: null })).toBe("'self' https://www.example.com http://partner.localhost");
  });

  it('lets only the site the page is embedded in frame it', () => {
    expect(frameAncestors({ origins: listed, embed: 'http://partner.localhost' })).toBe("'self' http://partner.localhost");
  });

  it('never lets a value from the API add a directive', () => {
    const hostile = ["https://a.example.com; script-src 'unsafe-inline'", "'unsafe-inline'", 'https://*.example.com', 'https://ok.example.com'];
    expect(frameAncestors({ origins: hostile, embed: null })).toBe("'self' https://ok.example.com");
    expect(securityPolicy({ origins: hostile, embed: null })).toBe(
      "frame-ancestors 'self' https://ok.example.com; base-uri 'none'; object-src 'none'; form-action 'self'",
    );
  });
});

describe('withEmbed', () => {
  it('leaves a link alone outside the widget', () => {
    expect(withEmbed('/jobs/barista', null)).toBe('/jobs/barista');
  });

  it('keeps the site on a link, a query and a fragment', () => {
    expect(withEmbed('/jobs/barista', 'https://www.example.com')).toBe('/jobs/barista?embed=https%3A%2F%2Fwww.example.com');
    expect(withEmbed('?_action=apply', 'https://www.example.com')).toBe('?_action=apply&embed=https%3A%2F%2Fwww.example.com');
    expect(withEmbed('/privacy#future-roles', 'http://partner.localhost')).toBe('/privacy?embed=http%3A%2F%2Fpartner.localhost#future-roles');
  });
});

describe('newTabLinks', () => {
  it("opens a description's links in a new tab", () => {
    expect(newTabLinks('<p>See <a href="https://www.example.com/team" rel="nofollow noopener noreferrer">the team</a>.</p>')).toBe(
      '<p>See <a target="_blank" href="https://www.example.com/team" rel="nofollow noopener noreferrer">the team</a>.</p>',
    );
  });

  it('leaves a link that already says where it opens', () => {
    expect(newTabLinks('<a target="_self" href="#x">x</a>')).toBe('<a target="_self" href="#x">x</a>');
  });
});

describe('useEmbedding', () => {
  it('puts the list and the listed site in locals for the middleware and the layout', () => {
    const locals: App.Locals = {};
    const url = new URL('http://acme.localhost:4322/jobs/barista?embed=https%3A%2F%2Fwww.example.com');
    expect(useEmbedding(locals, url, listed)).toBe('https://www.example.com');
    expect(locals.embedding).toEqual({ origins: listed, embed: 'https://www.example.com' });
    expect(embedOf(locals)).toBe('https://www.example.com');
  });

  it('is not in embed mode for a site off the list, which still may not frame the page', () => {
    const locals: App.Locals = {};
    expect(useEmbedding(locals, new URL('http://acme.localhost:4322/?embed=https%3A%2F%2Fevil.example.net'), listed)).toBeNull();
    expect(frameAncestors(locals.embedding)).toBe("'self' https://www.example.com http://partner.localhost");
  });
});
