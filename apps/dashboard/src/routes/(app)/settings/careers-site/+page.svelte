<script lang="ts">
  import ExternalLinkIcon from '~icons/lucide/external-link';
  import SaveIcon from '~icons/lucide/save';
  import type { HttpValidationProblemDetails, OrganizationProfile } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganization,
    createUpdateCareersEmbedding,
    createUpdateOrganization,
    getGetOrganizationQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE, MAX_QUESTIONS_PER_PAGE, MIN_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import CodeBlock from '#lib/components/code-block.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TagList from '#lib/components/tag-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // Everything about the organization's careers site in one place, which later settings for it join: its address (the
  // slug boards and candidates link to, which nobody changes here), how the apply form splits a questionnaire with no
  // sections into pages (apply-flow.md §7), and the sites that may show it in a frame
  // (docs/architecture/careers-widget.md). The form template (section 7). The organization's name stays on Organization;
  // the API takes both in one update, so this page sends the name as it is.
  const queryClient = useQueryClient();
  const organization = createGetOrganization();
  const detail = $derived(organization.data?.data);
  const refresh = () => queryClient.invalidateQueries({ queryKey: getGetOrganizationQueryKey(), exact: true });
  const update = createUpdateOrganization(() => ({ mutation: { onSuccess: refresh } }));

  const questionsPerPageRange = `Choose from ${MIN_QUESTIONS_PER_PAGE} to ${MAX_QUESTIONS_PER_PAGE} questions a page.`;
  const schema = z.object({
    questionsPerPage: z
      .number({ error: 'Enter a number of questions.' })
      .int('Enter a whole number.')
      .min(MIN_QUESTIONS_PER_PAGE, questionsPerPageRange)
      .max(MAX_QUESTIONS_PER_PAGE, questionsPerPageRange),
  });

  let saved = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ questionsPerPage: DEFAULT_QUESTIONS_PER_PAGE }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid || !detail) return;
      try {
        await update.mutateAsync({ data: { name: detail.name, questionsPerPage: submitted.data.questionsPerPage } });
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the apply form')];
        }
      }
    },
  });

  // The form starts from the organization once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({ data: { questionsPerPage: detail.questionsPerPage } });
    }
  });

  // Each add and remove saves the whole list at once, as tags do; removing is undone by adding the site again (section 10).
  const embedding = createUpdateCareersEmbedding(() => ({ mutation: { onSuccess: refresh } }));
  let embeddingFailure = $state('');

  async function saveSites(origins: string[]): Promise<boolean> {
    embeddingFailure = '';
    try {
      await embedding.mutateAsync({ data: { origins } });
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        const problems = (error.body as HttpValidationProblemDetails | undefined)?.errors ?? {};
        embeddingFailure = Object.values(problems).flat()[0] ?? 'Could not save the sites';
      } else {
        embeddingFailure = describeError(error, 'Could not save the sites');
      }
      return false;
    }
  }

  // The widget's script lives on the careers board, so the snippet is the board's address and the script's name. The
  // closing tag is escaped, or it would end this component's own script.
  const snippet = (profile: OrganizationProfile) => `<script src="${profile.careersUrl.replace(/\/$/, '')}/embed.js" async><\/script>`;
</script>

<svelte:head>
  <title>Careers site · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Careers site' }]} />

<main class="flex flex-col">
  <HeaderBand title="Careers site" />

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if organization.isError && !detail}
        <Panel title="Careers page">
          <QueryError message={describeError(organization.error, 'Could not load the careers site')} onretry={() => organization.refetch()} inset />
        </Panel>
      {:else if !detail}
        {#each ['Careers page', 'Apply form', 'Embed on your website'] as title (title)}
          <Panel {title}><div aria-hidden="true"><Skeleton class="h-10 w-full" /></div></Panel>
        {/each}
        <p class="sr-only" role="status">Loading the careers site…</p>
      {:else}
        <Panel title="Careers page">
          <SummaryList>
            <SummaryItem label="Address">
              <span class="flex min-w-0"><TruncatedText text={detail.careersUrl} href={detail.careersUrl} icon={ExternalLinkIcon} underlined class="text-primary" /></span>
            </SummaryItem>
          </SummaryList>
        </Panel>

        <form method="POST" use:enhance novalidate aria-label="Apply form">
          <Panel title="Apply form">
            <Field.Field data-invalid={$errors.questionsPerPage ? true : undefined}>
              <Field.Label for="questions-per-page">Questions per page</Field.Label>
              <Input
                id="questions-per-page"
                name="questionsPerPage"
                type="number"
                inputmode="numeric"
                min={MIN_QUESTIONS_PER_PAGE}
                max={MAX_QUESTIONS_PER_PAGE}
                step={1}
                required
                bind:value={$form.questionsPerPage}
                aria-invalid={$errors.questionsPerPage ? 'true' : undefined}
                aria-describedby={`questions-per-page-hint${$errors.questionsPerPage ? ' questions-per-page-error' : ''}`}
                class="max-w-32"
              />
              <Field.Description id="questions-per-page-hint">When a questionnaire has no sections, candidates see this many questions a page.</Field.Description>
              <Field.Error id="questions-per-page-error" errors={$errors.questionsPerPage?.map((message) => ({ message }))} />
            </Field.Field>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-2">
                <FailureText message={$errors._errors?.[0]} class="mr-auto" />
                <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
                <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
              </div>
            {/snippet}
          </Panel>
        </form>

        <Panel title="Embed on your website">
          <section aria-labelledby="embed-sites-heading" class="flex flex-col gap-3">
            <h3 id="embed-sites-heading" class="flex items-center gap-2 font-semibold">
              Sites that can embed it <Badge variant="count">{detail.embedOrigins.length}</Badge>
            </h3>
            <TagList
              tags={detail.embedOrigins}
              noun="site"
              label="Sites that can embed the careers page"
              placeholder="https://example.com"
              maxlength={200}
              inputmode="url"
              missing="Enter the site's address first."
              empty="No sites yet."
              busy={embedding.isPending}
              failure={embeddingFailure}
              onadd={(site) => saveSites([...detail.embedOrigins, site])}
              onremove={(site) => saveSites(detail.embedOrigins.filter((origin) => origin !== site))}
            />
          </section>
          {#if detail.embedOrigins.length > 0}
            <div class="mt-5 flex flex-col gap-2 border-t border-border-subtle pt-5">
              <h3 class="font-semibold">Embed code</h3>
              <CodeBlock code={snippet(detail)} label="Embed code" />
              <p class="text-muted-foreground">Paste it into your site where the jobs should show.</p>
            </div>
          {/if}
        </Panel>
      {/if}
    </div>
  </SettingsMenu>
</main>
