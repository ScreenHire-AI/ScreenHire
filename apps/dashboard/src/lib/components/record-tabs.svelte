<script lang="ts" module>
  /** One of a record's peer views. */
  export interface RecordTab {
    value: string;
    label: string;
    /** How many things the tab holds, in the count badge after its label (T4). */
    count?: number;
    /** With `count`, the count shows a state: 7 of 8 (Distribution 7/8, posted on 7 of 8 boards). */
    of?: number;
    /** What the count says, for screen readers ("posted on 7 of 8 boards"); "7 of 8" when left out. */
    countLabel?: string;
    /** A state no count can show, such as an integrity level that needs a look: a small dot and its words (E2). */
    marker?: { label: string; tone: 'review' | 'verify' };
  }
</script>

<script lang="ts">
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { cn } from '#lib/utils.js';

  // A record's tabs (design system section 11): its peer views in a fixed order, under the header band. Each tab's count
  // sits in the count badge; a tab shows its state in the count where one fits, and a small marker only where it does
  // not (E2). The marker is a dot, not a status icon, which only a status badge carries (I2). Render it inside the page's
  // `Tabs.Root`, usually in `HeaderBand`'s `tabs` snippet. The list scrolls sideways rather than wrapping (F5).
  let { tabs, label, class: className }: { tabs: readonly RecordTab[]; label: string; class?: string } = $props();

  const markerTone = { review: 'bg-status-review-mark', verify: 'bg-status-verify-mark' } as const;

  // A marked tab's name, said as its content would be, then the marker's words: "Integrity, Verification suggested".
  function markedName(tab: RecordTab & { marker: { label: string } }): string {
    const count =
      tab.count === undefined ? '' : ` ${tab.of === undefined ? tab.count : (tab.countLabel ?? `${tab.count} of ${tab.of}`)}`;
    return `${tab.label}${count}, ${tab.marker.label}`;
  }
</script>

<div class={cn('-mb-px max-w-full overflow-x-auto overflow-y-hidden [scrollbar-width:thin]', className)}>
  <Tabs.List variant="line" aria-label={label} class="h-11">
    {#each tabs as tab (tab.value)}
      <!-- The selected tab's line sits on the strip's bottom edge, inside the scroller, so it is never clipped. -->
      <Tabs.Trigger value={tab.value} aria-label={tab.marker ? markedName({ ...tab, marker: tab.marker }) : undefined} class="flex-none px-3 font-semibold group-data-[orientation=horizontal]/tabs:after:bottom-0">
        <!-- Screen readers hear "Distribution posted on 7 of 8 boards". The words start with a space, not a comma: a browser
             puts a space before the badge when it names the tab, which would land before a comma. A marked tab is named by
             aria-label, which starts with its visible label: hidden text after the dot would add a space before the comma. -->
        {tab.label}{#if tab.count !== undefined && tab.of !== undefined}<Badge variant="count"><span aria-hidden="true">{tab.count}/{tab.of}</span><span class="sr-only">{' '}{tab.countLabel ?? `${tab.count} of ${tab.of}`}</span></Badge>{:else if tab.count !== undefined}{' '}<Badge variant="count">{tab.count}</Badge>{/if}{#if tab.marker}<span aria-hidden="true" data-tab-marker class={cn('size-2 shrink-0 rounded-full', markerTone[tab.marker.tone])}></span>{/if}
      </Tabs.Trigger>
    {/each}
  </Tabs.List>
</div>
