import type { ApplicationFileSummary } from '@screenhire/api-client';

/** The API offers a file only once its scan is clean or when no scanner is configured (docs/architecture/apply-flow.md). */
export const canDownload = (file: ApplicationFileSummary) => file.scanStatus === 'Clean' || file.scanStatus === 'NotScanned';

/** The resume preview draws PDFs, and only files a person may download. */
export const canPreview = (file: ApplicationFileSummary) => canDownload(file) && file.contentType === 'application/pdf';
