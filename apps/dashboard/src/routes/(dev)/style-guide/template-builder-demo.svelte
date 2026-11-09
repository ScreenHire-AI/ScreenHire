<script lang="ts">
  import type { DefinitionError, HiringTemplateSummary, HiringWorkflow } from '@screenhire/api-client';
  import { getGetHiringTemplateVersionQueryKey, getListEmailTemplatesQueryKey } from '@screenhire/api-client/svelte-query';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import Panel from '#lib/components/panel.svelte';
  import StartingPointCard from '#lib/components/starting-point-card.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import JourneyOverview from '#lib/components/workflow/journey-overview.svelte';
  import StageCards from '#lib/components/workflow/stage-cards.svelte';
  import StageSheet from '#lib/components/workflow/stage-sheet.svelte';
  import TestDrive from '#lib/components/workflow/test-drive.svelte';
  import ValidationBadge from '#lib/components/workflow/validation-badge.svelte';

  // The template builder's parts (design system section 11: Journey, Stage cards, Stage sheet, Start from card, Test
  // drive, Fix button) on one sample workflow. Journey, Stage cards and Test drive draw their own panel, so they sit under
  // a heading rather than inside another panel. The style guide has no API, so this example brings its own query cache with
  // the answers the parts read; the Test drive shows its paused state, which runs nothing.
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } } });
  setQueryClientContext(client);
  const answer = <T,>(data: T) => ({ status: 200, headers: new Headers(), data });
  client.setQueryData(getListEmailTemplatesQueryKey(), answer({ templates: [], sendingEnabled: false }));

  let workflow = $state<HiringWorkflow>({
    stages: [
      { id: 'applied', name: 'Applied', kind: 'Applied' },
      { id: 'phone-call', name: 'Phone call', kind: 'Screening', slaDays: 3, scorecardTemplateId: 'phone-screen' },
      { id: 'trial-shift', name: 'Trial shift', kind: 'Assessment', slaDays: 5 },
      { id: 'offer', name: 'Offer', kind: 'Offer' },
      { id: 'hired', name: 'Hired', kind: 'Hired' },
      { id: 'rejected', name: 'Not this time', kind: 'Rejected' },
    ],
    questionnaire: {
      questions: [{ id: 'licence', type: 'YesNo', label: 'Do you hold a driving licence?', required: true, knockout: { disqualifyingAnswer: false } }],
    },
    automations: [
      { id: 'thanks', name: 'Say thank you', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] },
      { id: 'tag-trial', name: 'Tag the trial', trigger: { type: 'StageEntered', stageId: 'trial-shift' }, conditions: null, actions: [{ type: 'AddTag', tag: '' }] },
    ],
  });
  const problems: DefinitionError[] = [
    { path: 'automations[1].actions[0]', message: 'The tag in "Tag the trial" has no name. Type one, or remove that step.' },
  ];

  let stageId = $state<string | null>(null);
  const nothing = () => {};

  const office: HiringTemplateSummary = {
    id: 'demo-office',
    name: 'Office role',
    description: 'Bookkeeper, receptionist, office coordinator',
    isSystem: true,
    latestPublishedVersion: 1,
    latestPublishedVersionId: 'demo-office-1',
    draftVersion: null,
    createdAt: '2026-11-09T00:00:00Z',
    updatedAt: null,
    lastUpdated: { at: null, by: 'ScreenHire' },
    archived: false,
  };
  const trade: HiringTemplateSummary = { ...office, id: 'demo-trade', name: 'Trade role with a trial shift', description: 'Plumber, electrician, driver' };
  for (const [template, names] of [
    [office, ['Applied', 'Phone screen', 'Skills test', 'Interview', 'Offer']],
    [trade, ['Applied', 'Phone call', 'Trial shift', 'Offer']],
  ] as const) {
    client.setQueryData(
      getGetHiringTemplateVersionQueryKey(template.id, 1),
      answer({ definition: { workflow: { stages: names.map((name, index) => ({ id: `s${index}`, name, kind: index === 0 ? 'Applied' : 'Screening' })) } } }),
    );
  }
  let chosen = $state('demo-office');
</script>

<section aria-labelledby="style-guide-journey" class="flex flex-col gap-2">
  <h2 id="style-guide-journey" class="text-heading">Journey</h2>
  <JourneyOverview {workflow} onopen={(id) => (stageId = id)} questionsHref="#journey" />
</section>

<section aria-labelledby="style-guide-stage-cards" class="flex flex-col gap-2">
  <h2 id="style-guide-stage-cards" class="text-heading">Stage cards</h2>
  <StageCards bind:workflow errors={problems} onopen={(id) => (stageId = id)} onscorecard={nothing} onautomations={nothing} />
</section>

<Panel title="Stage sheet">
  <div class="flex flex-col gap-2">
    <p class="text-caption text-muted-foreground">Opens from a stage card or a Journey column, in place.</p>
    <div><Button variant="outline" onclick={() => (stageId = 'phone-call')}>Open the Phone call sheet</Button></div>
  </div>
  <StageSheet bind:workflow bind:stageId errors={[]} onscorecard={nothing} onautomations={nothing} onremove={nothing} automationsHref="#stage-sheet" />
</Panel>

<Panel title="Start from card">
  <div role="radiogroup" aria-label="Starting point" class="grid grid-cols-[repeat(auto-fit,minmax(16rem,1fr))] gap-4">
    {#each [office, trade] as template (template.id)}
      <StartingPointCard {template} chosen={chosen === template.id} onchoose={() => (chosen = template.id)} />
    {/each}
  </div>
</Panel>

<section aria-labelledby="style-guide-test-drive" class="flex flex-col gap-2">
  <h2 id="style-guide-test-drive" class="text-heading">Test drive</h2>
  <figure class="flex flex-col gap-2">
    <figcaption class="text-caption text-muted-foreground">Paused by a problem: only the notice, with the fix in place</figcaption>
    <TestDrive bind:workflow errors={problems} onfix={nothing} automationsHref="#test-drive" />
  </figure>
</section>

<Panel title="Fix button">
  <div class="flex flex-wrap items-center gap-3">
    <ValidationBadge errors={problems} />
  </div>
</Panel>
