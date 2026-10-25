<script lang="ts">
  import type { PermissionGroup, PermissionSummary, RoleSummary } from '@screenhire/api-client';
  import { createListRoles } from '@screenhire/api-client/svelte-query';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { roleKey } from '#lib/settings.js';

  // Compare roles (design system section 22): what each role can do, built-in and the team's own, from the catalogue the
  // API serves (RolePermissions.cs), never copy typed into the page. A fixed grid in the catalogue's order, which does
  // not sort (L2); each role's name opens its page. A narrow panel gets a list item per permission instead, naming the
  // roles that have it.
  const catalogue = createListRoles();
  const listHref = '/settings/roles';

  const roles = $derived<RoleSummary[]>(catalogue.data?.data.roles ?? []);
  const groups = $derived.by(() => {
    const byGroup = new Map<PermissionGroup, PermissionSummary[]>();
    for (const permission of catalogue.data?.data.permissions ?? []) {
      byGroup.set(permission.group, [...(byGroup.get(permission.group) ?? []), permission]);
    }
    return [...byGroup];
  });
  const has = (role: RoleSummary, permission: PermissionSummary) => role.permissions.includes(permission.permission);
</script>

<svelte:head>
  <title>Compare roles · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Roles', href: listHref }, { label: 'Compare roles' }]} back />

<main>
  <HeaderBand title="Compare roles" count={catalogue.isSuccess ? roles.length : undefined} countLabel="roles">
    {#snippet details()}<span>Give each person the role with the least access they need.</span>{/snippet}
  </HeaderBand>

  <div class="flex flex-col gap-5 px-4 py-6 md:px-6">
    {#if catalogue.isError}
      <Panel title="What each role can do">
        <QueryError message={describeError(catalogue.error, 'Could not load the roles')} onretry={() => catalogue.refetch()} inset />
      </Panel>
    {:else}
      <Panel title="What each role can do" flush footer={catalogue.isSuccess ? keys : undefined}>
        {#if catalogue.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1, 2, 3] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-48" />
                <Skeleton class="h-4 min-w-0 flex-1" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the roles…</p>
        {:else}
          <!-- The grid needs about 42rem; a narrower panel, on a phone, lists each permission instead (F2). -->
          <div class="@container">
            <div class="@2xl:hidden">
              {#each groups as [group, permissions] (group)}
                <h3 class="border-b border-border-subtle bg-muted px-4 py-2.5 text-caption font-semibold">{group}</h3>
                <ul>
                  {#each permissions as permission (permission.permission)}
                    <li class="flex min-w-0 flex-col gap-1.5 border-b border-border-subtle px-4 py-3">
                      <span class="font-semibold">{permission.name}</span>
                      <span class="text-muted-foreground">{permission.description}</span>
                      <span class="flex flex-wrap gap-1.5">
                        {#each roles.filter((role) => has(role, permission)) as role (roleKey(role))}<Badge variant="muted">{role.name}</Badge>{/each}
                      </span>
                    </li>
                  {/each}
                </ul>
              {/each}
            </div>
            <div class="hidden overflow-x-auto @2xl:block">
              <table class="w-full border-collapse text-left">
                <thead class="bg-muted">
                  <tr>
                    <th scope="col" class="h-11 border-b border-border px-4.5 text-caption font-semibold text-muted-foreground">Permission</th>
                    <th scope="col" class="h-11 border-b border-border px-3 text-caption font-semibold text-muted-foreground">Covers</th>
                    {#each roles as role (roleKey(role))}
                      <th scope="col" class="h-11 max-w-32 min-w-24 border-b border-border px-3 text-center text-caption font-semibold text-muted-foreground">
                        <span class="flex min-w-0 justify-center">
                          <TruncatedText text={role.name} href={`/settings/roles/${roleKey(role)}`} class="text-muted-foreground underline-offset-2 hover:underline" />
                        </span>
                      </th>
                    {/each}
                  </tr>
                </thead>
                {#each groups as [group, permissions] (group)}
                  <tbody>
                    <tr>
                      <th scope="rowgroup" colspan={roles.length + 2} class="h-10 border-b border-border-subtle bg-muted/50 px-4.5 text-caption font-semibold">{group}</th>
                    </tr>
                    {#each permissions as permission (permission.permission)}
                      <tr class="h-12 border-b border-border-subtle">
                        <th scope="row" class="px-4.5 font-semibold whitespace-nowrap">{permission.name}</th>
                        <td class="w-full max-w-0 px-3 text-muted-foreground"><TruncatedText text={permission.description} /></td>
                        {#each roles as role (roleKey(role))}
                          <td class="px-3 text-center">
                            {#if has(role, permission)}Yes{:else}<span class="text-muted-foreground">No</span>{/if}
                          </td>
                        {/each}
                      </tr>
                    {/each}
                  </tbody>
                {/each}
              </table>
            </div>
          </div>
        {/if}
      </Panel>
    {/if}
  </div>
</main>

{#snippet keys()}
  <!-- What a key can do has one home, the API keys page (W4); this only says where it is. -->
  <p class="text-muted-foreground">
    An API key has a scope instead of a role. <a href="/settings/api-keys" class="underline underline-offset-2">API keys</a> says what
    each scope can do.
  </p>
{/snippet}
