import type { ApplicationSummary } from '@screenhire/api-client';
import type { MarkShape, MarkTone } from '#lib/distribution.js';

/** A ready-made filter on the candidates table, listed in the sidebar with its count. */
export interface SavedView {
  id: string;
  label: string;
  shape: MarkShape;
  tone: MarkTone;
  matches(application: ApplicationSummary, now: number): boolean;
}

const day = 24 * 60 * 60 * 1000;

/**
 * The sidebar's saved views, in order. They open `/candidates?view=<id>`. Integrity views (review suggested,
 * verification recommended) join this list with the integrity data.
 */
export const savedViews: readonly SavedView[] = [
  {
    id: 'new-today',
    label: 'New today',
    shape: 'circle',
    tone: 'primary',
    matches: (application, now) => now - Date.parse(application.submittedAt) < day,
  },
  {
    id: 'not-screened',
    label: 'Not screened yet',
    shape: 'ring',
    tone: 'neutral',
    matches: (application) => application.stage.kind === 'Applied',
  },
  {
    id: 'interviewing',
    label: 'Interviewing',
    shape: 'ring',
    tone: 'neutral',
    matches: (application) => application.stage.kind === 'Interview',
  },
];

export const findSavedView = (id: string | null) => savedViews.find((view) => view.id === id);
