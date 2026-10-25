<script lang="ts">
  import PlusIcon from '@lucide/svelte/icons/plus';
  import { createRole, type Permission, type RoleSummary } from '@screenhire/api-client';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { invalidatePath } from '#lib/query.js';
  import { mayAssign, roleKey } from '#lib/settings.js';
  import { cn } from '#lib/utils.js';

  // New role (design system section 22, ADR-0014): a name and the role to copy, then the role's own page, where the
  // person changes what it may do. Only roles the person could give are offered to copy, and never Owner: its one
  // extra permission is Owner-only and a copy would not keep it. Starts at Viewer, the least access, or at the role
  // whose Copy role opened it.
  let {
    open = $bindable(false),
    roles,
    actor,
    from = 'viewer',
  }: {
    open?: boolean;
    /** The catalogue's roles, built-in ones first. */
    roles: readonly RoleSummary[];
    /** The signed-in person's permissions. */
    actor: readonly Permission[];
    /** The key of the role to start from (roleKey). */
    from?: string;
  } = $props();

  /** As the API allows it (CustomRole.MaxNameLength). */
  const maxNameLength = 50;
  const queryClient = useQueryClient();
  const sources = $derived(roles.filter((role) => role.role !== 'Owner' && mayAssign(actor, role.permissions)));

  const schema = z.object({
    name: z.string().trim().min(1, 'Enter a name.').max(maxNameLength, `Use at most ${maxNameLength} characters.`),
    from: z.string().min(1, 'Choose a role to copy.'),
  });

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', from: 'viewer' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const source = sources.find((role) => roleKey(role) === submitted.data.from);
      if (!source) {
        submitted.valid = false;
        submitted.errors.from = ['Choose a role to copy.'];
        return;
      }
      const response = await createRole({
        name: submitted.data.name.trim(),
        copyFrom: source.role === 'Custom' ? null : source.role,
        copyFromRoleId: source.customRoleId,
      });
      if (response.status === 201) {
        await invalidatePath(queryClient, '/v1/organization/roles');
        open = false;
        await goto(`/settings/roles/${response.data.id}`);
      } else if (response.status === 400) {
        applyValidationProblem(submitted, response.data);
      } else {
        submitted.valid = false;
        submitted.errors._errors = [
          response.status === 403 ? 'Your role cannot make a role with that much access.' : "We couldn't make the role just now. Try again in a minute.",
        ];
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: { name: '', from } });
  });
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>New role</Sheet.Title>
      <Sheet.Description>Start from a role you have, then change it.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="new-role-name">Name</Field.Label>
        <Input
          id="new-role-name"
          name="name"
          autocomplete="off"
          maxlength={maxNameLength}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'new-role-name-error' : undefined}
        />
        <Field.Error id="new-role-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <div class="flex flex-col gap-2">
        <span id="new-role-from-label" class="text-body font-medium">Copy from</span>
        <div role="radiogroup" aria-labelledby="new-role-from-label" class="flex flex-col gap-2">
          {#each sources as role (roleKey(role))}
            <label
              class="flex cursor-pointer items-start gap-3 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
            >
              <input type="radio" name="from" value={roleKey(role)} bind:group={$form.from} class="mt-0.5 size-4 shrink-0 accent-primary" />
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="font-semibold break-words">{role.name}</span>
                <span class="text-muted-foreground">{role.description}</span>
              </span>
            </label>
          {/each}
        </div>
        <Field.Error errors={$errors.from?.map((message) => ({ message }))} />
      </div>
      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create and edit</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
