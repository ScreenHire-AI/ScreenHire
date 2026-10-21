import type { ApplicationFileSummary, MalwareScanStatus } from '@screenhire/api-client';
import type { MarkShape } from '#lib/distribution.js';

/** The API offers a file only once its scan is clean or when no scanner is configured (docs/architecture/apply-flow.md). */
export const canDownload = (file: ApplicationFileSummary) => file.scanStatus === 'Clean' || file.scanStatus === 'NotScanned';

/** The resume preview draws PDFs, and only files a person may download. */
export const canPreview = (file: ApplicationFileSummary) => canDownload(file) && file.contentType === 'application/pdf';

type Withheld = { label: string; shape: MarkShape; tone: 'review' | 'verify' };

// Why a file is not offered, worded once for every place a file appears. A new scan status must be added here.
const withheld: Record<Exclude<MalwareScanStatus, 'Clean' | 'NotScanned'>, Withheld> = {
  Pending: { label: 'Being scanned for malware', shape: 'diamond', tone: 'review' },
  Infected: { label: 'Withheld: malware found', shape: 'square', tone: 'verify' },
};

/** The reason a file is withheld, or undefined when it may be downloaded. */
export const withheldReason = (file: ApplicationFileSummary): Withheld | undefined =>
  canDownload(file) ? undefined : withheld[file.scanStatus as keyof typeof withheld];
