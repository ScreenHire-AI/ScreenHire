import { getCollection } from 'astro:content';
import { publishedPosts } from './blog';
import { pages } from './pages';

export interface IndexedPage {
  path: string;
  title: string;
  description: string;
  section: 'Pages' | 'Hiring-AI law tracker' | 'Comparisons' | 'Blog';
}

/**
 * Every indexable page on the site, in reading order. The Open Graph image endpoint and llms.txt both build from this,
 * so a new page gets an image and an llms.txt line without a second list to update.
 */
export async function siteIndex(): Promise<IndexedPage[]> {
  const laws = (await getCollection('laws')).sort((a, b) => a.data.order - b.data.order);
  const comparisons = await getCollection('comparisons');
  const posts = await publishedPosts();

  return [
    ...Object.values(pages).map((page): IndexedPage => ({ ...page, section: 'Pages' })),
    ...laws.map((law): IndexedPage => ({
      path: `${pages.laws.path}${law.id}/`,
      title: law.data.title,
      description: law.data.description,
      section: 'Hiring-AI law tracker',
    })),
    ...comparisons.map((comparison): IndexedPage => ({
      path: `${pages.compare.path}${comparison.id}/`,
      title: comparison.data.title,
      description: comparison.data.description,
      section: 'Comparisons',
    })),
    ...posts.map((post): IndexedPage => ({
      path: `${pages.blog.path}${post.id}/`,
      title: post.data.title,
      description: post.data.description,
      section: 'Blog',
    })),
  ];
}
