import type { ApplicationSummary } from '@screenhire/api-client';
import type { MarkShape, MarkTone } from '#lib/distribution.js';
import { needsReview, verificationRecommended } from '#lib/integrity.js';

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
 * The sidebar's saved views, in order. They open `/candidates?view=<id>`. The integrity views follow authenticity only:
 * AI assistance is information, not a queue (ADR-0004). A dismissed assessment leaves both.
 */
export const savedViews: readonly SavedView[] = [
  {
    id: 'needs-review',
    label: 'Needs review',
    shape: 'diamond',
    tone: 'review',
    matches: (application) => needsReview(application),
  },
  {
    id: 'verification-recommended',
    label: 'Verification recommended',
    shape: 'square',
    tone: 'verify',
    matches: (application) => verificationRecommended(application),
  },
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
