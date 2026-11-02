// Which job board sent a candidate (docs/architecture/apply-flow.md). A board's feed link carries `src=<board id>`; the job
// page's Apply link and the apply form's address keep it, and the apply action sends it with the application as `source`.
// Pure functions: no environment, so they unit-test cleanly. The API keeps it only when it is a board that tags its links
// and drops anything else, so this only checks the shape, which keeps a stray value out of links and requests.

/** A board id as the API stores them: lower-case letters, digits and hyphens. */
const BOARD_ID = /^[a-z0-9-]{1,40}$/;

/** The board a link names, or null when `value` is not shaped like a board id. */
export const sourceBoard = (value: string | null | undefined): string | null =>
  typeof value === 'string' && BOARD_ID.test(value) ? value : null;

/** `href` with the board kept (`src=`), or as it is when no board sent the candidate. */
export function withSource(href: string, source: string | null | undefined): string {
  const board = sourceBoard(source);
  if (!board) return href;
  const hash = href.indexOf('#');
  const [path, fragment] = hash === -1 ? [href, ''] : [href.slice(0, hash), href.slice(hash)];
  const query = new URLSearchParams({ src: board }).toString();
  return `${path}${path.includes('?') ? '&' : '?'}${query}${fragment}`;
}
