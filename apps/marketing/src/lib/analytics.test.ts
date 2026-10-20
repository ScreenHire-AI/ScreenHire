import { describe, expect, it } from 'vitest';
import { analyticsScript } from './analytics';

const siteHost = 'marketing.example.com';

describe('analyticsScript', () => {
  it('renders nothing by default', () => {
    expect(analyticsScript({ provider: 'off', siteHost })).toBeNull();
    expect(analyticsScript({ provider: 'off', scriptPath: '/stats/script.js', siteId: 'x', siteHost })).toBeNull();
  });

  it('renders nothing without a script path', () => {
    expect(analyticsScript({ provider: 'plausible', siteHost })).toBeNull();
  });

  it.each(['https://analytics.example.net/script.js', '//analytics.example.net/script.js', 'stats/script.js'])(
    'refuses %s, which is not a first-party site-relative path',
    (scriptPath) => {
      expect(analyticsScript({ provider: 'plausible', scriptPath, siteHost })).toBeNull();
    },
  );

  it('tags Plausible with the site host unless a domain is configured', () => {
    expect(analyticsScript({ provider: 'plausible', scriptPath: '/js/script.js', siteHost })).toEqual({
      src: '/js/script.js',
      attributes: { 'data-domain': siteHost },
    });
  });

  it('needs a website id for Umami', () => {
    expect(analyticsScript({ provider: 'umami', scriptPath: '/u.js', siteHost })).toBeNull();
    expect(analyticsScript({ provider: 'umami', scriptPath: '/u.js', siteId: 'abc', siteHost })).toEqual({
      src: '/u.js',
      attributes: { 'data-website-id': 'abc' },
    });
  });
});
