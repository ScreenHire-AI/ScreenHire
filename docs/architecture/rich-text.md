---
type: Design
title: "Rich text"
description: "Job descriptions and email messages as one stored format, sanitized HTML limited to the editor toolbar's elements: the canonical form the server writes, Liquid inside an email's HTML, the plain-text part, the Tiptap editor with variable chips and its shortcuts, and the migration that converted plain text."
tags: [rich-text, email, jobs, editor]
sources:
  - resource: src/ScreenHire.Infrastructure/Content/RichText.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplates.cs
  - resource: src/ScreenHire.Domain/History/RichTextToPlain.cs
  - resource: src/ScreenHire.Infrastructure/Persistence/Migrations/20261031005107_ConvertPlainTextToRichText.cs
  - resource: apps/dashboard/src/lib/rich-text.ts
  - resource: apps/dashboard/src/lib/components/rich-text-editor.svelte
  - resource: apps/dashboard/src/lib/components/rich-text-toolbar.svelte
---

# Rich text

A job's description and an email template's message are rich text: bold, italic, two heading levels, bulleted and
numbered lists, and links. Both are stored the same way, as sanitized HTML that holds only what the dashboard's editor
toolbar makes. The careers page renders it with `set:html`, Google for Jobs reads it in the `JobPosting` JSON-LD, the
feeds put it in CDATA ([job distribution](job-distribution.md)), and email sends it as the HTML part with a plain-text
part made from it ([email](email.md)).

Roadmap item 8 first said Markdown. Descriptions were already sanitized HTML everywhere they are read, so HTML stayed
the one format and email bodies moved to it.

## The canonical form

`RichText.Sanitize` (`src/ScreenHire.Infrastructure/Content/RichText.cs`, HtmlSanitizer on AngleSharp, both MIT) runs
on every write: a job's create and update, a hiring template's posting default, and every email body the API stores,
previews or sends. It returns one canonical string:

- **Elements:** `p br strong em h2 h3 ul ol li a`, and nothing else. `b` becomes `strong`, `i` becomes `em`, `h1`
  becomes `h2` and `h4` to `h6` become `h3`. Any other tag goes and its text stays; script, style, iframe and similar
  go with their content.
- **Links:** only `href`, only an absolute `http`, `https` or `mailto` address, and the server sets
  `rel="nofollow noopener noreferrer"`. A link whose address is refused is just its text.
- **Structure:** the top level is blocks. Loose text becomes paragraphs: a blank line starts one, a single newline is a
  `br`, and lines that all start with "- " make a list. So an API client may send plain text and it reads as the old
  text box did. A lone `p` inside an `li` is unwrapped (the editor writes `<li><p>One</p></li>`), whitespace inside a
  block collapses, and empty blocks go.
- **Idempotent:** sanitizing the result again gives the same string.

A save whose content sanitizes to what is stored keeps the stored string. The editor's HTML, an API client's and an
older row's differ from the canonical form in markup only, so opening Edit details and saving records no change, writes
no History and leaves no unpublished changes. Email template updates follow the same rule.

## Liquid in an email's HTML

An email body is HTML with Liquid in its text (`EmailTemplates`, Fluid):

1. The body is sanitized, so wording that arrives for a preview, a test send or the composer is safe as it is.
2. Inside each `{{ ... }}` and `{% ... %}` the markup is stripped and entities decoded before Fluid parses, so
   `{% if x > 1 %}` stored as `&gt;` reads as written and bold over part of a tag does no harm.
3. Values render through an HTML encoder, so a candidate's name never becomes markup.
4. Newlines a value brought become `br`, so a reason with two lines keeps them; the stored body has none in its blocks.
5. Bare `http` and `https` addresses in the text become links that show themselves, which is how
   `{{ questionnaireLink }}` is clickable.
6. The result is sanitized again: a `{% if %}` that was false leaves no empty paragraph or orphaned tag.

The template checker refuses `raw`, and a link's address may not hold a variable, so a candidate's details never go to
another site inside a URL. A link may have its own text. ScreenHire adds no click tracking, and the allow-list keeps
images out, so no tracking pixel can be sent ([legal](../research/legal.md)).

The text part is `RichText.ToPlainText`: a blank line between blocks, "- " before a bulleted item and its number before
a numbered one, and a link's address in brackets after its text when the two differ. History's before and after use
the regex-only `RichTextToPlain` in Domain, with the same rules. When a change alters the HTML but not the text, History
says "changed the formatting of the description" or "of the message" with no before and after.

## The editor

`RichTextEditor` (`apps/dashboard/src/lib/components/rich-text-editor.svelte`) is Tiptap 3 (`@tiptap/core`,
`@tiptap/pm`, `@tiptap/starter-kit`, MIT, no Pro extensions) mounted with a Svelte attachment. It serves New job, Edit
details, the email template editor and the email composer. `richTextExtensions` (`apps/dashboard/src/lib/rich-text.ts`)
keeps StarterKit to the server's elements: no code, blockquote, strike, underline or rule, headings at levels 2 and 3,
links that open nothing while editing and accept only what the server accepts, and list items without Tab, so lists do
not nest and Tab always leaves the editor.

- **Values:** the editor writes `value` only when the person changes the text, so an untouched editor hands back the
  exact string it was given and no form is dirty on opening. An empty editor writes `""`, so New job sends no
  description and a template's default still applies. A value set from outside (Start from, Reset to original) loads
  without counting as an edit.
- **Chips:** with `variables`, each `{{ ... }}` is one inline atom shown as a muted word with the variable's label
  (First name), and the raw expression when it is more than a name. It is stored as the Liquid text itself.
- **Toolbar:** design system section 11, Rich text editor.

### Shortcuts

Every toolbar shortcut has a modifier: Ctrl or Cmd with B, I, Alt 2, Alt 3, Shift 8 and Shift 7. Links have none,
because Ctrl K opens the command palette everywhere. They are listed under Text editor in the `?` dialog. Typed
formatting ("- ", "1. ", "## ", "**bold**") works only while single-key shortcuts are on: for speech input those
sequences act like single-key shortcuts (WCAG 2.1.4). Tiptap builds those rules once, so changing the preference makes
the editor again with its text.

## The conversion

Migration `ConvertPlainTextToRichText` widened `email_templates.body` to 20,000 characters (the limit applies to the
sanitized HTML) and converted plain text with the same rule as the structure pass: every email body, every job
description with no tags, and every published snapshot description with no tags, so equal content stays equal. FORCE
row-level security is lifted around each update, as the owner running a migration is bound by it too. The updates
bypass change history on purpose: they change markup, not anyone's words ([change history](change-history.md)).
Rows that use the old tags (`b`, `h4`, `blockquote`) stay safe and take the canonical form on their next real edit.
