<script lang="ts">
  import { accountCrumbs } from '#lib/account.js';
  import AccountMenu from '#lib/components/account-menu.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { preferences, type ThemeChoice } from '#lib/preferences.svelte.js';

  // Your account > Preferences (design system section 21). Each switch applies at once and is announced, as the menu's
  // switches were. Single-key shortcuts and confirming moves are the person's, kept on the server for this organization; the
  // theme and reduced motion are this device's, kept in this browser (docs/architecture/sign-in-and-tenancy.md).
  const themes: readonly { value: ThemeChoice; label: string }[] = [
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
    { value: 'system', label: 'System' },
  ];
  let announcement = $state('');
  const say = (sentence: string) => {
    announcement = '';
    queueMicrotask(() => (announcement = sentence));
  };
</script>

<svelte:head>
  <title>Preferences · ScreenHire</title>
</svelte:head>

<TopBar crumbs={accountCrumbs('/account/preferences')} />

<main class="flex flex-col">
  <HeaderBand title="Preferences" frame oneRow />

  <AccountMenu>
    <div class="flex w-full flex-col gap-5">
      <p role="status" class="sr-only">{announcement}</p>
      <Panel title="On this device">
        <div class="flex flex-col gap-5">
          <div class="flex flex-col gap-2">
            <span id="preference-theme" class="font-semibold">Theme</span>
            <div class="-m-1 max-w-[calc(100%+--spacing(2))] overflow-x-auto p-1 [scrollbar-width:thin]">
              <ToggleGroup.Root
                type="single"
                variant="outline"
                size="lg"
                spacing={0}
                aria-labelledby="preference-theme"
                bind:value={() => preferences.themeChoice, (next) => next && (preferences.setThemeChoice(next as ThemeChoice), say('Theme changed.'))}
              >
                {#each themes as theme (theme.value)}
                  <ToggleGroup.Item value={theme.value} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground"
                    >{theme.label}</ToggleGroup.Item
                  >
                {/each}
              </ToggleGroup.Root>
            </div>
          </div>
          <div class="flex items-start justify-between gap-4">
            <div class="flex flex-col gap-0.5">
              <Label for="preference-motion">Reduce motion</Label>
              <span id="preference-motion-hint" class="text-caption text-muted-foreground">Nothing slides or fades.</span>
            </div>
            <Switch
              id="preference-motion"
              aria-describedby="preference-motion-hint"
              checked={preferences.reduceMotion}
              onCheckedChange={(on) => (preferences.setReduceMotion(on), say(on ? 'Motion reduced.' : 'Motion back on.'))}
            />
          </div>
        </div>
      </Panel>
      <Panel title="How you work">
        <div class="flex flex-col gap-5">
          <div class="flex items-start justify-between gap-4">
            <div class="flex flex-col gap-0.5">
              <Label for="preference-shortcuts">Single-key shortcuts</Label>
              <span id="preference-shortcuts-hint" class="text-caption text-muted-foreground">G then a letter, J and K, X and V. Ctrl K always works.</span>
            </div>
            <Switch
              id="preference-shortcuts"
              aria-describedby="preference-shortcuts-hint"
              checked={preferences.shortcutsEnabled}
              onCheckedChange={(on) => (preferences.setShortcutsEnabled(on), say(on ? 'Single-key shortcuts on.' : 'Single-key shortcuts off.'))}
            />
          </div>
          <div class="flex items-start justify-between gap-4">
            <div class="flex flex-col gap-0.5">
              <Label for="preference-confirm">Confirm moves that send emails or create tasks</Label>
              <span id="preference-confirm-hint" class="text-caption text-muted-foreground">Ask before a move runs a stage's automations.</span>
            </div>
            <Switch
              id="preference-confirm"
              aria-describedby="preference-confirm-hint"
              checked={preferences.confirmMoves}
              onCheckedChange={(on) => (preferences.setConfirmMoves(on), say(on ? 'Moves will ask first.' : 'Moves will not ask.'))}
            />
          </div>
        </div>
      </Panel>
    </div>
  </AccountMenu>
</main>
