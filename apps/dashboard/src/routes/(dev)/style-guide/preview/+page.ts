import { error } from '@sveltejs/kit';
import { dev } from '$app/env';
import type { PageLoad } from './$types';

// The style guide is a development tool: a production build shows the 404 page in its place.
export const load: PageLoad = () => {
  if (!dev) {
    error(404, 'Not found');
  }
};
