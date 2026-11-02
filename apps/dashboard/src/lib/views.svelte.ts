import type { SavedViewSummary } from '@screenhire/api-client';
import {
  createDeleteView,
  createRestoreView,
  createUpdateHiddenViews,
  createUpdateView,
} from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';
import { describeError, invalidatePath } from '#lib/query.js';
import { viewsPath } from '#lib/realtime.js';
import type { UndoOffer } from '#lib/undo.svelte.js';

/** What the sidebar's view menus do. Each returns a sentence when it fails, or null when it worked. */
export interface ViewActions {
  rename(view: SavedViewSummary, name: string): Promise<string | null>;
  setShared(view: SavedViewSummary, shared: boolean): Promise<string | null>;
  /** Deletes the view at once and offers Undo for 10 seconds. */
  remove(view: SavedViewSummary): Promise<string | null>;
  /** Hides a built-in view from the sidebar (or shows it again), for this person only. */
  setHidden(builtInId: string, hidden: boolean): Promise<string | null>;
}

/**
 * The saved views' changes (docs/architecture/saved-views.md), as the sidebar's menus make them. Each one saves on the
 * server at once and refetches the list; other dashboards get it through ViewsChanged. Call during component
 * initialisation, inside the shell's query client.
 */
export function createViewActions(options: { undo: UndoOffer; hiddenBuiltIns: () => readonly string[] }): ViewActions {
  const queryClient = useQueryClient();
  const refetch = () => invalidatePath(queryClient, viewsPath);
  const update = createUpdateView(() => ({ mutation: { onSettled: refetch } }));
  const remove = createDeleteView(() => ({ mutation: { onSettled: refetch } }));
  const restore = createRestoreView(() => ({ mutation: { onSettled: refetch } }));
  const hide = createUpdateHiddenViews(() => ({ mutation: { onSettled: refetch } }));

  async function attempt(work: () => Promise<unknown>, fallback: string): Promise<string | null> {
    try {
      await work();
      return null;
    } catch (error) {
      return describeError(error, fallback);
    }
  }

  return {
    rename: (view, name) => attempt(() => update.mutateAsync({ viewId: view.id, data: { name, shared: null } }), 'Could not rename the view'),
    setShared: (view, shared) =>
      attempt(() => update.mutateAsync({ viewId: view.id, data: { name: null, shared } }), shared ? 'Could not share the view' : 'Could not make the view private'),
    async remove(view) {
      const failure = await attempt(() => remove.mutateAsync({ viewId: view.id }), 'Could not delete the view');
      if (failure) return failure;
      options.undo.offer(`Deleted "${view.name}".`, () => attempt(() => restore.mutateAsync({ viewId: view.id }), 'Could not bring the view back'));
      return null;
    },
    setHidden(builtInId, hidden) {
      const current = options.hiddenBuiltIns().filter((id) => id !== builtInId);
      const next = hidden ? [...current, builtInId] : current;
      return attempt(() => hide.mutateAsync({ data: { hiddenBuiltIns: next } }), hidden ? 'Could not hide the view' : 'Could not show the view');
    },
  };
}
