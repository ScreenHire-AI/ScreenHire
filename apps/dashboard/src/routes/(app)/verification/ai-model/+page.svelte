<script lang="ts">
  import AiModelForm from '#lib/components/verification-area/ai-model-form.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';

  // The AI model tab: ScreenHire's included model, or the organization's own key and model. For who can edit settings.
</script>

<svelte:head>
  <title>AI model · Verification · ScreenHire</title>
</svelte:head>

<SettingsTab title="AI model">
  {#snippet children(detail)}
    <AiModelForm {detail} />
  {/snippet}
</SettingsTab>
