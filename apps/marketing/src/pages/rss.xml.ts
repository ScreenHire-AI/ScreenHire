import rss from '@astrojs/rss';
import type { APIRoute } from 'astro';
import { publishedPosts } from '../lib/blog';
import { pages } from '../lib/pages';

// RSS for the blog (ADR-0003). Item links are relative; @astrojs/rss resolves them against SITE_URL.
export const GET: APIRoute = async ({ site }) => {
  const posts = await publishedPosts();
  return rss({
    title: 'ScreenHire blog',
    description: pages.blog.description,
    site: site ?? 'http://localhost:4321',
    trailingSlash: true,
    items: posts.map((post) => ({
      title: post.data.title,
      description: post.data.description,
      link: `${pages.blog.path}${post.id}/`,
      pubDate: new Date(`${post.data.published}T00:00:00Z`),
    })),
    customData: '<language>en</language>',
  });
};
