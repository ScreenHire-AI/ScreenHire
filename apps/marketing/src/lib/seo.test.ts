import { describe, expect, it } from 'vitest';
import { article, breadcrumbList, faqPage, formatDate, ogImageKey, ogImagePath, siteGraph, softwareApplication } from './seo';

const site = new URL('https://marketing.example.com/');

describe('ogImageKey', () => {
  it.each([
    ['/', 'index'],
    ['/pricing/', 'pricing'],
    ['/hiring-ai-laws/eu-ai-act/', 'hiring-ai-laws/eu-ai-act'],
    ['/blog/why-no-fraud-score', 'blog/why-no-fraud-score'],
  ])('maps %s to %s', (pathname, key) => {
    expect(ogImageKey(pathname)).toBe(key);
  });

  it('serves the image under /og/', () => {
    expect(ogImagePath('/pricing/')).toBe('/og/pricing.png');
  });
});

describe('siteGraph', () => {
  it('builds every URL from the configured site, never a fixed host', () => {
    const json = JSON.stringify(siteGraph(new URL('https://preview.example.org/')));
    expect(json).toContain('https://preview.example.org/#organization');
    expect(json).not.toContain('screenhire.');
  });
});

describe('breadcrumbList', () => {
  it('starts at Home and numbers each crumb with an absolute URL', () => {
    const list = breadcrumbList([{ name: 'Pricing', path: '/pricing/' }], site);
    expect(list).toEqual({
      '@type': 'BreadcrumbList',
      itemListElement: [
        { '@type': 'ListItem', position: 1, name: 'Home', item: 'https://marketing.example.com/' },
        { '@type': 'ListItem', position: 2, name: 'Pricing', item: 'https://marketing.example.com/pricing/' },
      ],
    });
  });
});

describe('faqPage', () => {
  it('turns each question into a Question with an accepted Answer', () => {
    expect(faqPage([{ question: 'Q?', answer: 'A.' }])).toEqual({
      '@type': 'FAQPage',
      mainEntity: [{ '@type': 'Question', name: 'Q?', acceptedAnswer: { '@type': 'Answer', text: 'A.' } }],
    });
  });

  it('refuses an empty FAQ, which would be invalid structured data', () => {
    expect(() => faqPage([])).toThrow();
  });
});

describe('article', () => {
  it('links the page, its Open Graph image and the organization', () => {
    const node = article(
      { type: 'BlogPosting', headline: 'H', description: 'D', path: '/blog/post/', datePublished: '2026-10-20' },
      site,
    );
    expect(node).toMatchObject({
      '@type': 'BlogPosting',
      url: 'https://marketing.example.com/blog/post/',
      image: 'https://marketing.example.com/og/blog/post.png',
      datePublished: '2026-10-20',
      dateModified: '2026-10-20',
      author: { '@id': 'https://marketing.example.com/#organization' },
    });
  });
});

describe('softwareApplication', () => {
  it('leaves offers out while no plan has a price', () => {
    expect(softwareApplication('D', site)).not.toHaveProperty('offers');
  });

  it('lists priced plans as offers', () => {
    const app = softwareApplication('D', site, [{ name: 'Starter', price: '49', priceCurrency: 'USD' }]);
    expect(app.offers).toEqual([{ '@type': 'Offer', name: 'Starter', price: '49', priceCurrency: 'USD' }]);
  });
});

describe('formatDate', () => {
  it('formats a calendar date without shifting it by the time zone', () => {
    expect(formatDate('2027-01-01')).toBe('1 January 2027');
  });
});
