/** The API's own words for a problem when it gave any, or a sentence for the statuses that come without. */
export function problemText(status: number, detail: string | null | undefined): string {
  if (detail) {
    return detail;
  }
  if (status === 403) {
    return 'Connecting an assistant needs a role that manages API keys and sees every job. Ask an owner or admin to connect it.';
  }
  if (status === 401) {
    return 'Your session has ended. Sign in again, then start the connection from the assistant.';
  }
  return `We could not check this request (HTTP ${status}). Start the connection again from the assistant.`;
}
