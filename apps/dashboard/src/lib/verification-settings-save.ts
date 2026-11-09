import type { OrganizationVerificationSettings, UpdateVerificationSettingsRequest } from '@screenhire/api-client';
import {
  createUpdateOrganizationVerificationSettings,
  getGetOrganizationVerificationSettingsQueryKey,
} from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';

// Each settings tab of the Verification area saves its own part. The API takes the whole settings in one PUT, so a tab sends
// what is saved with its own part laid over it. The API key is never sent from here (null keeps the saved one), and an
// unchanged provider, address and model are not tested again by the server.

/** The request that saves the settings exactly as they are. */
export function savedRequest(detail: OrganizationVerificationSettings): UpdateVerificationSettingsRequest {
  const own = detail.modelSource === 'OwnKey';
  const addressEditable = own && detail.presets.find((preset) => preset.provider === detail.provider)?.baseUrlEditable === true;
  return {
    llmConsistencyCheck: detail.llmConsistencyCheck,
    modelSource: detail.modelSource,
    provider: own ? detail.provider : null,
    baseUrl: addressEditable ? detail.baseUrl : null,
    model: own ? detail.model : null,
    apiKey: null,
    ownKeyInputPrice: own ? detail.ownKeyInputPrice : null,
    ownKeyOutputPrice: own ? detail.ownKeyOutputPrice : null,
    checks: {
      network: detail.checks.network ?? true,
      contact: detail.checks.contact ?? true,
      documents: detail.checks.documents ?? true,
      answers: detail.checks.answers ?? true,
      duplicates: detail.checks.duplicates ?? true,
      formUse: detail.checks.formUse ?? true,
    },
    defaultAiUse: detail.defaultAiUse,
    deviceCheck: detail.deviceCheck,
    trustedDomains: [...detail.trustedDomains],
    trustedNetworks: [...detail.trustedNetworks],
    alerts: {
      suggestedEmail: detail.alerts.suggestedEmail ?? false,
      suggestedSlack: detail.alerts.suggestedSlack ?? false,
      budgetEmail: detail.alerts.budgetEmail ?? false,
      budgetSlack: detail.alerts.budgetSlack ?? false,
    },
  };
}

/** What every settings tab says after a save. */
export const savedMessage = 'Saved. It applies from the next application.';

/**
 * A function that saves one tab's part of the settings over the current saved settings and refreshes them. Call it in a
 * component's setup. It throws the API's error, for the form to word.
 */
export function createVerificationSettingsSave() {
  const queryClient = useQueryClient();
  const update = createUpdateOrganizationVerificationSettings(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationVerificationSettingsQueryKey(), exact: true }) },
  }));
  return (saved: OrganizationVerificationSettings, part: Partial<UpdateVerificationSettingsRequest>) =>
    update.mutateAsync({ data: { ...savedRequest(saved), ...part } });
}
