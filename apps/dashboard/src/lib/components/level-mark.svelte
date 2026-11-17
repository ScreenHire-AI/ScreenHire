<script lang="ts" module>
  import EyeIcon from '~icons/lucide/eye';
  import IdCardIcon from '~icons/lucide/id-card';

  // The icon's colour: the status's `-mark` token, which dark mode redefines (design system sections 3.2 and 3.3).
  const colours = { review: 'text-status-review-mark', verify: 'text-status-verify-mark' } as const;
  // Each level's own status icon from the catalogue (section 3.2): `eye` for Review, take a look; `id-card` for Verify,
  // confirm who they are. The icon alone tells the two apart, so colour is never the only signal (T1).
  const icons = { review: EyeIcon, verify: IdCardIcon } as const;
</script>

<script lang="ts">
  import { cn } from '#lib/utils.js';

  // A verification level's icon beside its word (design system sections 3.2 and 14): Review and Verify, the two levels
  // that need a look. Decorative: the state's word and its screen-reader text carry the meaning, so it is hidden from
  // assistive technology. `steps` stays for the callers that pass the level's place; the icon follows `tone`.
  let {
    steps,
    tone,
    size = 'sm',
    class: className,
  }: {
    /** The level's place, two for Review and three for Verify. */
    steps: 0 | 1 | 2 | 3;
    tone: 'review' | 'verify';
    size?: 'sm' | 'lg';
    class?: string;
  } = $props();
  const Icon = $derived(icons[tone]);
</script>

<Icon aria-hidden="true" data-level-mark={steps} data-icon={tone === 'review' ? 'eye' : 'id-card'} class={cn('shrink-0', size === 'lg' ? 'size-4' : 'size-3.5', colours[tone], className)} />
