<script lang="ts" module>
  /** One alert: its name, the sentence its info button holds, and the two choices it saves. */
  export interface AlertRow<Key extends string> {
    label: string;
    /** What the alert says and to whom, one press away (W12). */
    about: string;
    /** Where "In the app" shows it, for screen readers and the tick's own info. */
    inApp: string;
    email: Key;
    slack: Key;
  }
</script>

<script lang="ts" generics="Key extends string">
  import CheckIcon from '~icons/lucide/check';
  import InfoButton from '#lib/components/info-button.svelte';
  import * as Table from '#lib/components/ui/table/index.js';
  import { checkboxClass } from '#lib/components/workflow/controls.js';

  // Notification choices as a grid (design system section 11, Alert grid): a row per alert, and a column per way it reaches
  // people. In the app is a fixed tick, never a disabled checkbox (A3): the app always shows it. Email shows only where this
  // deployment can send it, and Slack only once Slack is connected (A15: no nudge to connect it here).
  let {
    rows,
    values = $bindable(),
    emailAvailable,
    slackConnected,
    label = 'Alerts',
  }: {
    rows: readonly AlertRow<Key>[];
    values: Record<Key, boolean>;
    emailAvailable: boolean;
    slackConnected: boolean;
    label?: string;
  } = $props();

  // A new object each time, so a form store bound to `values` sees the change (a nested write would not reach it).
  const choose = (key: Key, checked: boolean) => (values = { ...values, [key]: checked });
</script>

<!-- The grid holds controls, so it needs no scrolling region of its own: the table itself carries the name. -->
<Table.Root aria-label={label} data-alert-grid>
  <Table.Header>
    <Table.Row>
      <Table.Head>Alert</Table.Head>
      <Table.Head class="text-center">In the app</Table.Head>
      {#if emailAvailable}<Table.Head class="text-center">Email</Table.Head>{/if}
      {#if slackConnected}<Table.Head class="text-center">Slack</Table.Head>{/if}
    </Table.Row>
  </Table.Header>
  <Table.Body>
    {#each rows as row (row.label)}
      <Table.Row data-alert={row.label}>
        <Table.Cell>
          <span class="flex items-center gap-1">
            <span class="font-medium">{row.label}</span>
            <InfoButton label={`About ${row.label}`} text={row.about} />
          </span>
        </Table.Cell>
        <Table.Cell class="text-center">
          <span class="inline-flex items-center justify-center">
            <CheckIcon aria-hidden="true" class="size-4 text-status-clear-mark" />
            <span class="sr-only">Always. {row.inApp}</span>
          </span>
        </Table.Cell>
        {#if emailAvailable}
          <Table.Cell class="text-center">
            <!-- The label is the 44px touch target on a touch screen (F4); the box inside stays its own size. -->
            <label class="inline-flex items-center justify-center pointer-coarse:size-11">
              <input type="checkbox" checked={values[row.email]} onchange={(event) => choose(row.email, event.currentTarget.checked)} aria-label={`${row.label} by email`} class={checkboxClass} />
            </label>
          </Table.Cell>
        {/if}
        {#if slackConnected}
          <Table.Cell class="text-center">
            <label class="inline-flex items-center justify-center pointer-coarse:size-11">
              <input
                type="checkbox"
                checked={values[row.slack]}
                onchange={(event) => choose(row.slack, event.currentTarget.checked)}
                aria-label={`${row.label} on Slack, as a direct message`}
                class={checkboxClass}
              />
            </label>
          </Table.Cell>
        {/if}
      </Table.Row>
    {/each}
  </Table.Body>
</Table.Root>
