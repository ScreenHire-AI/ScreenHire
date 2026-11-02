// A CSV file from a report's tables (docs/architecture/reports.md). Each panel is a section: its title, its header row, its
// rows, then a blank line. Numbers are written as numbers. Text a recruiter typed (a job title) is written so a spreadsheet
// never runs it as a formula: a text cell that starts with = + - or @ gets a quote in front.

/** A cell: text, a number, or nothing. */
export type CsvCell = string | number | null;

export interface CsvSection {
  title: string;
  header: readonly string[];
  rows: readonly (readonly CsvCell[])[];
}

const formulaStart = /^[=+\-@\t\r]/;

function cell(value: CsvCell): string {
  if (value === null) return '';
  if (typeof value === 'number') return Number.isFinite(value) ? String(value) : '';
  const text = formulaStart.test(value) ? `'${value}` : value;
  return /[",\r\n]|^\s|\s$/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

const line = (cells: readonly CsvCell[]) => cells.map(cell).join(',');

/** The sections as CSV text (RFC 4180, CRLF line ends). */
export function toCsv(sections: readonly CsvSection[]): string {
  return sections.map((section) => [line([section.title]), line(section.header), ...section.rows.map(line)].join('\r\n')).join('\r\n\r\n') + '\r\n';
}

/** Saves `csv` as `filename` through the browser. The byte order mark lets Excel read it as UTF-8. */
export function downloadCsv(filename: string, csv: string): void {
  const url = URL.createObjectURL(new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
