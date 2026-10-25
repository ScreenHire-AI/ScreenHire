import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Email templates moved to the Templates page (design system section 22): the same template, at its new address.
export const load: PageLoad = ({ params }) => {
  redirect(307, `/templates/emails/${encodeURIComponent(params.templateId)}`);
};
