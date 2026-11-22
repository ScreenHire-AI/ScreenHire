<script lang="ts">
  import XIcon from '~icons/lucide/x';
  import IconButton from '#lib/components/icon-button.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // A field to start from and one icon button with its tooltip, under a provider as the root layout has.
</script>

<Tooltip.Provider>
  <input aria-label="Before" />
  <IconButton label="Close"><XIcon /></IconButton>
</Tooltip.Provider>
