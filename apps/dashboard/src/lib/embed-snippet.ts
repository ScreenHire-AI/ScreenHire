/**
 * The code a company pastes into its own site to show its careers pages in a frame (docs/architecture/careers-widget.md).
 * The script lives on the careers board, so the snippet is the board's address and the script's name. With a job's
 * address (its slug) the frame shows that job and its application instead of the whole board.
 */
export function embedSnippet(careersUrl: string, jobSlug?: string): string {
  const job = jobSlug ? ` data-job="${jobSlug}"` : '';
  return `<script src="${careersUrl.replace(/\/$/, '')}/embed.js"${job} async></script>`;
}
