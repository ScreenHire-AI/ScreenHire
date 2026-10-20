import { getCollection } from 'astro:content';

/** Published posts, newest first. Drafts never build. */
export async function publishedPosts() {
  const posts = await getCollection('blog', (post) => !post.data.draft);
  return posts.sort((a, b) => b.data.published.localeCompare(a.data.published));
}
