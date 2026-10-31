/**
 * The heading fonts an employer can pick on Settings, Branding (ADR-0018), keyed by the API's `HeadingFont` values.
 * Each family comes from heading-fonts.css; ScreenHire's own, Bricolage Grotesque, is the default and comes from
 * fonts.css. The careers pages set `--sh-font-display` to the chosen family, so every title that uses `font-display`
 * takes it. A family is written from this table only, never from what the API sent.
 */
export interface HeadingFontChoice {
  /** The font's name, as the font picker and History show it. */
  label: string;
  /** The CSS `font-family` stack. */
  family: string;
}

const sans = '"Hanken Grotesk Variable", system-ui, sans-serif';
const serif = 'Georgia, "Times New Roman", serif';

/** ScreenHire's heading font, used while an organization has picked none. */
export const defaultHeadingFont: HeadingFontChoice = {
  label: 'Bricolage Grotesque',
  family: `"Bricolage Grotesque Variable", ${sans}`,
};

export const headingFonts = {
  Inter: { label: 'Inter', family: `"Inter Variable", ${sans}` },
  Montserrat: { label: 'Montserrat', family: `"Montserrat Variable", ${sans}` },
  Nunito: { label: 'Nunito', family: `"Nunito Variable", ${sans}` },
  Fraunces: { label: 'Fraunces', family: `"Fraunces Variable", ${serif}` },
  PlayfairDisplay: { label: 'Playfair Display', family: `"Playfair Display Variable", ${serif}` },
} as const satisfies Record<string, HeadingFontChoice>;

export type HeadingFontName = keyof typeof headingFonts;

/** The choice for `font`, the default for null; throws on a name this table does not have. */
export function headingFont(font: string | null | undefined): HeadingFontChoice {
  if (font == null) return defaultHeadingFont;
  if (!Object.hasOwn(headingFonts, font)) throw new Error(`Unknown heading font: ${font}`);
  return headingFonts[font as HeadingFontName];
}
