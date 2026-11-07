// File rules shared by the careers server and the upload island. Browser-safe and dependency-free, because the island
// bundles it: keep it small. The API checks every file again, including its content; these checks only give the
// candidate an answer before the bytes are sent.

/** "PDF or DOCX" for `['.pdf', '.docx']`, "PNG, JPG or JPEG" for three. */
export const acceptedLabel = (types: readonly string[]): string => {
  const names = [...new Set(types.map((type) => type.replace(/^\./, '').toUpperCase()))];
  return names.length < 3 ? names.join(' or ') : `${names.slice(0, -1).join(', ')} or ${names.at(-1)}`;
};

/** "320 KB", "1.5 MB". */
export function describeSize(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  const megabytes = bytes / (1024 * 1024);
  return `${Number.isInteger(megabytes) ? megabytes : megabytes.toFixed(1)} MB`;
}

/**
 * Why a file cannot be uploaded, worded for the person, or null when it can. An empty list of types takes any type, and
 * no size limit takes any size. Every file input checks with this, picked or dropped (the drop area, `DropArea.svelte`).
 */
export function fileProblem(file: { name: string; size: number }, acceptedTypes: readonly string[], maxBytes = Infinity): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (acceptedTypes.length > 0 && !acceptedTypes.includes(extension)) return `Upload a ${acceptedLabel(acceptedTypes)} file.`;
  if (file.size === 0) return 'This file is empty.';
  if (file.size > maxBytes) return `This file is larger than ${describeSize(maxBytes)}.`;
  return null;
}

/** An upload the API has checked, as the form carries it between steps. */
export interface UploadRef {
  uploadId: string;
  fileName: string;
  sizeBytes: number;
}

/** A UUID, as upload ids and job ids are. */
export const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** An upload reference from untrusted JSON (a hidden field), or null when it is not one. */
export function parseUploadRef(value: unknown): UploadRef | null {
  if (typeof value === 'string') {
    try {
      value = JSON.parse(value);
    } catch {
      return null;
    }
  }

  if (typeof value !== 'object' || value === null) return null;
  const { uploadId, fileName, sizeBytes } = value as Record<string, unknown>;
  return typeof uploadId === 'string' && uuid.test(uploadId) && typeof fileName === 'string' && typeof sizeBytes === 'number'
    ? { uploadId, fileName: fileName.slice(0, 200), sizeBytes }
    : null;
}
