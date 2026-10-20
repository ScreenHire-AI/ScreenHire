<script lang="ts">
  // A file question's field. Server-rendered, it is a plain named file input that posts with the form, and the careers
  // server uploads the file (no JavaScript needed). Once hydrated, it sends the chosen file straight to storage through
  // an upload link the API issues, has the API check it, and leaves only the upload's reference in the form: the input
  // loses its name, so the bytes are never posted twice. It talks to the careers server's uploads endpoint
  // (pages/[organization]/uploads.ts), never to the API.
  import { describeSize, fileProblem, type UploadRef } from '../lib/files';

  interface Props {
    questionId: string;
    label: string;
    required: boolean;
    hint: string;
    accept: string[];
    maxBytes: number;
    /** The careers server's uploads endpoint for the organization. */
    endpoint: string;
    jobId: string;
    current: UploadRef | null;
    error: string | null;
  }

  const { questionId, label, required, hint, accept, maxBytes, endpoint, jobId, current: initial, error: initialError }: Props =
    $props();

  // Ids match fieldId() in ../lib/apply, so the error summary's links land on the input.
  const inputId = `field-file-${questionId}`;
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;
  const statusId = `${inputId}-status`;
  const failed = 'We could not upload this file. Try again in a moment.';

  let current = $state(initial);
  let error = $state(initialError);
  let uploading = $state(false);
  let hydrated = $state(false);
  let input: HTMLInputElement | undefined = $state();

  $effect(() => {
    hydrated = true;
  });

  // One call to the uploads endpoint; its answer, or the problem it reported.
  async function call<T>(body: Record<string, unknown>): Promise<T> {
    const response = await fetch(endpoint, {
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
    if (!file || !input) return;
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
  <label for={inputId} class="text-sm font-bold">
    {label}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </label>
  <p id={hintId} class="text-[13px] text-candidate-muted-foreground">{hint}</p>
  {#if error}
    <p id={errorId} class="text-sm font-semibold text-destructive">{error}</p>
  {/if}
  <div class="rounded-xl border-[1.5px] border-dashed border-candidate-input-border bg-candidate-surface p-3">
    <input
      bind:this={input}
      type="file"
      id={inputId}
      name={hydrated ? undefined : `file:${questionId}`}
      accept={accept.join(',')}
      required={required && !current}
      disabled={uploading}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={[hintId, error ? errorId : null, statusId].filter(Boolean).join(' ')}
      onchange={upload}
      class="file-input"
    />
  </div>
  <p id={statusId} aria-live="polite" class="text-sm font-semibold">
    {#if uploading}
      Uploading...
    {:else if current}
      Uploaded: {current.fileName} ({describeSize(current.sizeBytes)})
    {/if}
  </p>
  {#if current}
    <input type="hidden" name={`upload:${questionId}`} value={JSON.stringify(current)} />
  {/if}
</div>
