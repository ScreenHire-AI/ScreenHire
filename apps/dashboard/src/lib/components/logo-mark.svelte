<script lang="ts">
  let {
    size = 'default',
  }: {
    /** `large` is the 40px mark at the top of the sign-in card; `default` sits beside the name in the sidebar and the brand bar. */
    size?: 'default' | 'large';
  } = $props();
</script>

<!-- The ScreenHire logo mark, beside the name in the sidebar and the brand bar, and above the title in the sign-in card.
     Decorative: the name or the title is the text. -->
<span
  aria-hidden="true"
  class={[
    'grid shrink-0 place-items-center bg-foreground font-display font-extrabold text-background',
    size === 'large' ? 'size-10 rounded-panel text-panel-title' : 'size-7 rounded-lg',
  ]}>S</span
>
