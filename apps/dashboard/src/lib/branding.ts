/** A brand colour as a person types it: six hex digits, with or without the leading hash. */
export const brandColorPattern = /^#?[0-9a-f]{6}$/i;

/** The `#rrggbb` form the API takes, lower-cased, from what `brandColorPattern` accepted. */
export const normalizeBrandColor = (text: string): string => {
  const digits = text.trim().replace(/^#/, '').toLowerCase();
  return `#${digits}`;
};
