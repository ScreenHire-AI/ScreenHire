<script lang="ts">
  // A file question's field. Server-rendered, it is a plain named file input that posts with the form, and the careers
  // server uploads the file (no JavaScript needed). Hydrated as the careers apply page's island, with an `endpoint`, it
  // sends the chosen file straight to storage through an upload link the API issues, has the API check it, and leaves
  // only the upload's reference in the form: the input loses its name, so the bytes are never posted twice. It talks to
  // the careers server's uploads endpoint (apps/careers/src/pages/[organization]/uploads.ts), never to the API. Without
  // an endpoint (the dashboard's preview) it stays a plain file input.
  import { untrack } from 'svelte';
  import { describeSize, fileProblem, type UploadRef } from './files';
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
  const statusId = `${inputId}-status`;
  const failed = 'We could not upload this file. Try again in a moment.';

  // The server-rendered starting values; the island keeps its own from then on.
  let current = $state(untrack(() => initial));
  let error = $state(untrack(() => initialError));
  let uploading = $state(false);
  let hydrated = $state(false);
  let input: HTMLInputElement | undefined = $state();

  $effect(() => {
    if (!endpoint) return;
    hydrated = true;
    // A file chosen before the island hydrated fired its change event too early: upload it now.
    if (input?.files?.length) void upload();
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

  async function upload() {
    const file = input?.files?.[0];
    if (!file || !input || !endpoint) return;
    const problem = fileProblem(file, accept, maxBytes);
    if (problem) {
      error = problem;
      input.value = '';
      return;
    }

    error = null;
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
      input.value = '';
    }
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
  <div class="rounded-xl border-[1.5px] border-dashed border-candidate-input-border bg-candidate-surface p-3">
    <input
      bind:this={input}
      type="file"
      id={inputId}
      name={hydrated ? undefined : fields.file(questionId)}
      accept={accept.join(',')}
      required={required && !current}
      disabled={uploading}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={[hintId, error ? errorId : null, statusId].filter(Boolean).join(' ')}
      onchange={upload}
      class="file-input"
    />
  </div>
  <p id={statusId} aria-live="polite" class="text-candidate-body font-semibold">
    {#if uploading}
      Uploading...
    {:else if current}
      Uploaded: {current.fileName} ({describeSize(current.sizeBytes)})
    {/if}
  </p>
  {#if current}
    <input type="hidden" name={fields.upload(questionId)} value={JSON.stringify(current)} />
  {/if}
</div>
