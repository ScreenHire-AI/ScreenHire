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
  }
</script>

<script lang="ts">
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { cn } from '#lib/utils.js';

  // A record's tabs (design system section 11): its peer views in a fixed order, under the header band. Each tab's count
  // sits in the count badge, the same grey on every tab, and a tab shows its state in its count where one fits (7/8).
  // A state that needs a look is the record's status beside its title, never a coloured tab (E2, W7). Render it inside the
  // page's `Tabs.Root`, usually in `HeaderBand`'s `tabs` snippet. The list scrolls sideways rather than wrapping (F5).
  let { tabs, label, class: className }: { tabs: readonly RecordTab[]; label: string; class?: string } = $props();
</script>

<div class={cn('-mb-px max-w-full overflow-x-auto overflow-y-hidden [scrollbar-width:thin]', className)}>
  <!-- 44px tabs with the label centred, so the selected line sits 12px under the count badge, as on the canvas. The list's
       own 32px height and 3px padding would put the line right under the badge. -->
  <Tabs.List variant="line" aria-label={label} class="h-11 p-0 group-data-[orientation=horizontal]/tabs:h-11">
    {#each tabs as tab (tab.value)}
      <!-- The selected tab's line sits on the strip's bottom edge, inside the scroller, so it is never clipped. -->
      <Tabs.Trigger value={tab.value} class="h-full flex-none px-3 font-semibold group-data-[orientation=horizontal]/tabs:after:bottom-0">
        <!-- Screen readers hear "Distribution posted on 7 of 8 boards". The words start with a space, not a comma: a browser
             puts a space before the badge when it names the tab, which would land before a comma. -->
        {tab.label}{#if tab.count !== undefined && tab.of !== undefined}<Badge variant="count"><span aria-hidden="true">{tab.count}/{tab.of}</span><span class="sr-only">{' '}{tab.countLabel ?? `${tab.count} of ${tab.of}`}</span></Badge>{:else if tab.count !== undefined}{' '}<Badge variant="count">{tab.count}</Badge>{/if}
      </Tabs.Trigger>
    {/each}
  </Tabs.List>
</div>
