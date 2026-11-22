import type { JobDetail, JobSalary, JobStatus, ManualPostingStatus, PostedField, SourceApplications } from '@screenhire/api-client';
import { locales } from '#lib/dates.js';
import type { PostingView } from '#lib/distribution.js';
import { employmentLabel } from '#lib/format.js';

// Posting a job yourself on a board ScreenHire does not integrate with (docs/architecture/job-distribution.md, "Posted by
// you"): the job's text ready to copy, the tracked apply link for the board, and how the saved posting's row reads. The
// listing's address is only a link for the person; nothing here or on the server fetches it.

/** Boards people post on by hand, offered while typing the board's name. Any other name works as well. */
export const commonBoards = [
  'LinkedIn',
  'Indeed',
  'Glassdoor',
  'ZipRecruiter',
  'Wellfound',
  'Handshake',
  'Dice',
  'Craigslist',
  'Facebook',
  'Nextdoor',
] as const;

const SOURCE_KEY_MAX = 40;

/**
 * The `src` a board's name makes, as the server's `ManualPosting.SourceKeyFor` does: lower case, accents dropped, every run
 * of other characters one hyphen, none at either end, at most 40 characters. `LinkedIn` is `linkedin`. Empty when the name
 * has no letter or digit. The key is sent with the save, so the link the person copied is the one that counts.
 */
export function sourceKeyFor(boardName: string): string {
  const words = boardName
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter(Boolean);
  const key = words.join('-');
  return key.length <= SOURCE_KEY_MAX ? key : key.slice(0, SOURCE_KEY_MAX).replace(/-+$/, '');
}

/**
 * Whether a key can be a posting's link: not empty, and not the id of a board ScreenHire posts to itself (`boardIds`, from
 * `GET /v1/boards`), whose applications it would take. The server refuses the ids of hidden boards too.
 */
export function isUsableKey(key: string, boardIds: readonly string[]): boolean {
  return key.length > 0 && !boardIds.includes(key);
}

/** The job's careers page with `src=key`, replacing an earlier `src`, as the server's `BoardUrls.WithSource` writes it. */
export function trackedUrl(jobUrl: string, key: string): string {
  const url = new URL(jobUrl);
  url.searchParams.set('src', key);
  return url.toString();
}

/** Today in the person's own time zone, as an ISO date (`2026-11-22`): the default posted date and the end-date check. */
export function localToday(now: Date = new Date()): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const periodWords: Record<string, string> = { Hour: 'an hour', Day: 'a day', Week: 'a week', Month: 'a month', Year: 'a year' };

/** A salary as a listing writes it, in full: `£70,000 to £85,000 a year`, `From £30,000 a year`, `Up to $14 an hour`. */
export function salaryText(salary: JobSalary): string {
  const money = new Intl.NumberFormat(locales, { style: 'currency', currency: salary.currency, maximumFractionDigits: 2, minimumFractionDigits: 0 });
  const period = periodWords[salary.period ?? 'Year'] ?? '';
  const { min, max } = salary;
  const range =
    min !== null && max !== null
      ? `${money.format(min)} to ${money.format(max)}`
      : min !== null
        ? `From ${money.format(min)}`
        : max !== null
          ? `Up to ${money.format(max)}`
          : '';
  return range ? `${range} ${period}`.trim() : '';
}

const blocks = new Set(['P', 'H1', 'H2', 'H3', 'H4', 'UL', 'OL', 'BLOCKQUOTE', 'DIV']);

/**
 * The description (sanitized HTML) as plain text for a board that takes no formatting: a blank line between paragraphs and
 * headings, `- ` before each bullet, `1. ` before each numbered item, and line breaks kept. A link keeps its words only.
 */
export function plainText(html: string): string {
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  const out: string[] = [];

  function inline(node: Node): string {
    if (node.nodeType === Node.TEXT_NODE) return (node.textContent ?? '').replace(/\s+/g, ' ');
    if (!(node instanceof Element)) return '';
    if (node.tagName === 'BR') return '\n';
    return [...node.childNodes].map(inline).join('');
  }
  const clean = (text: string) =>
    text
      .split('\n')
      .map((line) => line.trim())
      .join('\n')
      .trim();

  function block(element: Element) {
    if (element.tagName === 'UL' || element.tagName === 'OL') {
      const items = [...element.children].filter((child) => child.tagName === 'LI');
      out.push(items.map((item, index) => `${element.tagName === 'OL' ? `${index + 1}.` : '-'} ${clean(inline(item))}`).join('\n'));
    } else if (element.tagName === 'DIV' || element.tagName === 'BLOCKQUOTE') {
      walk(element);
    } else {
      const text = clean(inline(element));
      if (text) out.push(text);
    }
  }

  function walk(parent: Element) {
    let loose = '';
    const flush = () => {
      const text = clean(loose);
      if (text) out.push(text);
      loose = '';
    };
    for (const node of parent.childNodes) {
      if (node instanceof Element && blocks.has(node.tagName)) {
        flush();
        block(node);
      } else {
        loose += inline(node);
      }
    }
    flush();
  }

  walk(body);
  return out.join('\n\n');
}

/** One field of the job, ready to copy to a board by hand. */
export interface CopyField {
  field: Exclude<PostedField, 'Link'>;
  label: string;
  /** What Copy puts on the clipboard as plain text. */
  text: string;
}

/** The job's fields a listing needs, in the order boards ask for them. A field the job does not have is left out. */
export function copyFields(job: Pick<JobDetail, 'title' | 'location' | 'employmentType' | 'salary' | 'description'>): CopyField[] {
  const fields: CopyField[] = [
    { field: 'Title', label: 'Title', text: job.title },
    { field: 'Location', label: 'Location', text: job.location },
    { field: 'Type', label: 'Type', text: employmentLabel(job.employmentType) },
  ];
  const salary = job.salary ? salaryText(job.salary) : '';
  if (salary) fields.push({ field: 'Salary', label: 'Salary', text: salary });
  fields.push({ field: 'Description', label: 'Description', text: plainText(job.description) });
  return fields.filter((field) => field.text !== '');
}

/** What each changed field is called in the row's tooltip and the sheet: "the title", "the apply link". */
export const changedFieldWords: Record<PostedField, string> = {
  Title: 'the title',
  Location: 'the location',
  Type: 'the type',
  Salary: 'the salary',
  Description: 'the description',
  Link: 'the apply link',
};

const list = new Intl.ListFormat('en', { type: 'conjunction' });

/** The changed fields in words: "the title and the salary". */
export const changedList = (fields: readonly PostedField[]) => list.format(fields.map((field) => changedFieldWords[field]));

/** The tooltip of a row whose job changed since it was posted: what changed, in words. */
export function changedSentence(fields: readonly PostedField[]): string {
  return `Since you posted it, the job changed ${changedList(fields)}. Copy the updated text, update the listing, then mark it updated.`;
}

/** What the Post it yourself sheet is for: a new posting, editing one, or updating one after the job changed. */
export type PostItYourselfMode = { kind: 'new' } | { kind: 'edit'; posting: ManualPostingStatus } | { kind: 'update'; posting: ManualPostingStatus };

/**
 * Whether the team should take the listing down and say so: it is not marked taken down, and the job is closed or the end
 * date the person gave has passed (today, in their own time zone, is after it).
 */
export function needsTakingDown(posting: Pick<ManualPostingStatus, 'takenDownAt' | 'endsOn'>, jobStatus: JobStatus, today: string): boolean {
  if (posting.takenDownAt) return false;
  return jobStatus === 'Closed' || (posting.endsOn !== null && posting.endsOn < today);
}

/** How a posting by the team reads in the Job postings table: it claims only what the person told us. */
export function describeManualPosting(posting: ManualPostingStatus, jobStatus: JobStatus, today: string): PostingView {
  if (posting.takenDownAt) {
    return { status: 'notInEffect', label: 'Taken down', detail: 'Taken down: you marked this listing taken down.' };
  }

  if (needsTakingDown(posting, jobStatus, today)) {
    return {
      status: 'stillPosted',
      label: 'Still posted by you',
      detail:
        jobStatus === 'Closed'
          ? `Still posted by you: the job is closed. Take the listing down on ${posting.boardName}, then mark it taken down.`
          : `Still posted by you: the end date you gave has passed. Check the listing on ${posting.boardName} is down, then mark it taken down.`,
    };
  }

  return { status: 'open', label: 'Posted by you', detail: `Posted by you: you posted this job on ${posting.boardName} yourself. ScreenHire cannot see the listing.` };
}

/**
 * The applications through each link of the job, by source: `careers`, an automatic board's id or a posting's source key.
 * Google for Jobs and the public job-board API send candidates to the careers page's own address, so they have no count
 * of their own (null): theirs are in the careers page's.
 */
export function applicationsBySource(counts: readonly SourceApplications[] | undefined): (source: string) => number | null {
  const bySource = new Map((counts ?? []).map((count) => [count.source, count.applications]));
  return (source) => (source === 'google' || source === 'job-board-api' ? null : (bySource.get(source) ?? 0));
}
