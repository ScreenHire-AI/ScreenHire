/**
 * Messages between the dashboard and the sandboxed file viewer (`frame.ts`). The frame's origin is opaque ("null"), so
 * both sides check the sender's window rather than its origin, and the dashboard trusts nothing from the frame beyond
 * these shapes: it renders no markup the frame sends, and every number is bounded before use.
 */

/** The most pages the viewer draws. A longer file says so and offers Download. */
export const maxPreviewPages = 20;

/** Dashboard to frame: the file's bytes, sent once the frame says it is ready. */
export type RenderRequest = { type: 'render'; bytes: ArrayBuffer };

/** Frame to dashboard. */
export type FrameMessage =
  | { type: 'ready' }
  | { type: 'size'; height: number }
  | { type: 'rendered'; pageCount: number }
  | { type: 'failed' };

const maxHeight = 1_000_000;
const maxPages = 100_000;

const isWhole = (value: unknown, max: number): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= max;

export function isRenderRequest(data: unknown): data is RenderRequest {
  return typeof data === 'object' && data !== null && 'type' in data && data.type === 'render' && 'bytes' in data && data.bytes instanceof ArrayBuffer;
}

/** The frame's message, or undefined for anything that is not exactly one of {@link FrameMessage}. */
export function readFrameMessage(data: unknown): FrameMessage | undefined {
  if (typeof data !== 'object' || data === null || !('type' in data)) return undefined;
  switch (data.type) {
    case 'ready':
      return { type: 'ready' };
    case 'failed':
      return { type: 'failed' };
    case 'size':
      return 'height' in data && isWhole(data.height, maxHeight) ? { type: 'size', height: data.height } : undefined;
    case 'rendered':
      return 'pageCount' in data && isWhole(data.pageCount, maxPages) && data.pageCount > 0
        ? { type: 'rendered', pageCount: data.pageCount }
        : undefined;
    default:
      return undefined;
  }
}
