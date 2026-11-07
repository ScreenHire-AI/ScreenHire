<script lang="ts">
  // A file question's field, drawn by the drop area (DropArea.svelte). Server-rendered, it is a plain named file input
  // that posts with the form, and the careers server uploads the file (no JavaScript needed). Hydrated as the careers
  // apply page's island, with an `endpoint`, it sends the chosen or dropped file straight to storage through an upload
  // link the API issues, has the API check it, and leaves only the upload's reference in the form: the input loses its
  // name, so the bytes are never posted twice. It talks to the careers server's uploads endpoint
  // (apps/careers/src/pages/[organization]/uploads.ts), never to the API. Without an endpoint (the dashboard's preview)
  // the input keeps its name and a dropped file is put into it, so it posts with the form like a picked one.
  import { untrack } from 'svelte';
  import DropArea from './DropArea.svelte';
  import type { UploadRef } from './files';
  import { fieldId, fields } from './model';

  interface Props {
    questionId: string;
    label: string;
    required: boolean;
    hint: string;
    accept: string[];
    maxBytes: number;
    /** The careers server's uploads endpoint for the organization; without one, nothing uploads until the form posts. */
    endpoint?: string | null;
    jobId?: string | null;
    current?: UploadRef | null;
    error?: string | null;
  }

  const {
    questionId,
    label,
    required,
    hint,
    accept,
    maxBytes,
    endpoint = null,
    jobId = null,
    current: initial = null,
    error: initialError = null,
  }: Props = $props();

  // The same ids the careers error summary links to. A mounted field keeps its question.
  const inputId = untrack(() => fieldId(fields.file(questionId)));
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;
  const failed = 'We could not upload this file. Try again in a moment.';

  // The server-rendered starting values; the island keeps its own from then on.
  let current = $state(untrack(() => initial));
  let error = $state(untrack(() => initialError));
  let uploading = $state(false);
  // With an endpoint the island sends the bytes itself; the input is named only until it hydrates.
  let sending = $state(false);
  // Without one, the file the form will post, for the area to name.
  let picked = $state<{ name: string; size: number } | null>(null);

  $effect(() => {
    if (endpoint) sending = true;
  });

  // One call to the uploads endpoint; its answer, or the problem it reported.
  async function call<T>(body: Record<string, unknown>): Promise<T> {
    const response = await fetch(endpoint!, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jobId, questionId, ...body }),
    });
    const result = (await response.json().catch(() => ({}))) as T & { error?: string };
    if (!response.ok || result.error) throw new Error(result.error ?? failed);
    return result;
  }

  async function upload(file: File) {
    uploading = true;
    try {
      const ticket = await call<{ uploadId: string; uploadUrl: string; method: string; headers: Record<string, string> }>({
        step: 'start',
        fileName: file.name,
        sizeBytes: file.size,
      });
      const put = await fetch(ticket.uploadUrl, {
        method: ticket.method,
        headers: { ...ticket.headers, 'Content-Type': file.type || 'application/octet-stream' },
        body: file,
      });
      if (!put.ok) throw new Error(failed);
      current = await call<UploadRef>({ step: 'verify', uploadId: ticket.uploadId });
    } catch (exception) {
      error = exception instanceof Error && exception.message ? exception.message : failed;
    } finally {
      uploading = false;
    }
  }

  // A file that passed the type and size checks: sent now with an endpoint, or left in the input to post with the form.
  function choose(file: File) {
    error = null;
    if (endpoint) void upload(file);
    else picked = { name: file.name, size: file.size };
  }

  function remove() {
    current = null;
    picked = null;
    error = null;
  }
</script>

<div class="flex flex-col gap-1.5">
  <label for={inputId} class="text-candidate-body font-bold">
    {label}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </label>
  <p id={hintId} class="text-candidate-hint text-candidate-muted-foreground">{hint}</p>
  {#if error}
    <p id={errorId} class="text-candidate-body font-semibold text-destructive">{error}</p>
  {/if}
  <DropArea
    id={inputId}
    keep={!endpoint}
    name={sending ? undefined : fields.file(questionId)}
    {accept}
    {maxBytes}
    required={required && !current && !picked}
    disabled={uploading}
    invalid={!!error}
    describedby={error ? `${hintId} ${errorId}` : hintId}
    file={current ? { name: current.fileName, size: current.sizeBytes } : picked}
    status={uploading ? 'Uploading...' : null}
    onfile={choose}
    onproblem={(message) => (error = message)}
    onremove={remove}
  />
  {#if current}
    <input type="hidden" name={fields.upload(questionId)} value={JSON.stringify(current)} />
  {/if}
</div>
