import type { ReceivedEmail } from '@screenhire/api-client';

const escapes: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };

/**
 * The HTML part to preview for a reply. The API keeps the sender's HTML cleaned to the editor's elements; a plain-text-only
 * reply has none, so its text is shown as it was written (line breaks kept, nothing read as markup).
 */
export function replyHtml(reply: Pick<ReceivedEmail, 'html' | 'text'>): string {
  if (reply.html.trim().length > 0) return reply.html;
  const text = reply.text.replace(/[&<>"]/g, (c) => escapes[c] ?? c);
  return `<p style="white-space: pre-wrap; font-family: sans-serif">${text}</p>`;
}
