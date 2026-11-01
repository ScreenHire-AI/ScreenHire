<script lang="ts">
  import LogOutIcon from '~icons/lucide/log-out';
  import { beforeNavigate, goto } from '$app/navigation';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { cn } from '#lib/utils.js';

  // Asks before leaving a page with unsaved work, in a dialog (design system section 9). Closing the tab or reloading
  // gets the browser's own prompt instead: it is the only one a page may show then.
  let { unsaved, title }: { unsaved: boolean; title: string } = $props();

  let open = $state(false);
  let target: URL | null = null;
  let leaving = false;

  beforeNavigate((navigation) => {
    if (!unsaved || leaving) return;
    navigation.cancel();
    if (navigation.type === 'leave' || !navigation.to) return;
    target = navigation.to.url;
    open = true;
  });

  async function leave() {
    if (!target) return;
    leaving = true;
    open = false;
    try {
      if (target.origin === location.origin) await goto(target);
      else location.assign(target);
    } finally {
      leaving = false;
    }
  }
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>{title}</Dialog.Title>
      <Dialog.Description>Your unsaved changes are lost.</Dialog.Description>
    </Dialog.Header>
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Keep editing</Dialog.Close>
      <Button variant="destructive" size="lg" onclick={leave}><LogOutIcon aria-hidden="true" />Leave without saving</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
