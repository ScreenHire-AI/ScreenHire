---
type: Design
title: "Email to candidates"
description: "Organization email templates in Liquid with a plain-text body over fixed variables, sending to one candidate or a selection through the worker, EmailSent in Activity, the rule that the rejection email goes only with Reject, and what is not built (replies, an inbox)."
tags: [email, templates, liquid]
sources:
  - resource: src/ScreenHire.Domain/Hiring/EmailTemplate.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplates.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplateStore.cs
  - resource: src/ScreenHire.Infrastructure/Email/Templates/
  - resource: src/ScreenHire.Application/Applications/SendApplicationEmail.cs
  - resource: apps/api/Endpoints/EmailEndpoints.cs
  - resource: apps/dashboard/src/lib/components/email-sheet.svelte
---
# Email to candidates

A hiring team writes its own emails and sends them to candidates by hand, to a selection, or from an automation. Every
email a candidate gets is one of the organization's templates, goes out through the worker, and appears in the
application's Activity. Candidates cannot reply into ScreenHire yet (see "Not built").

## Templates

There are two kinds, both listed by `GET /v1/email-templates` and edited in Settings, Email templates:

- **Built-in.** `application-received`, `stage-change` and `application-rejected` (`EmailTemplateIds`). ScreenHire's
  wording lives in `src/ScreenHire.Infrastructure/Email/Templates` (`{id}.subject.liquid`, `{id}.body.liquid`). An
  organization that edits one gets an `email_templates` row with `built_in_id` set; deleting the row restores
  ScreenHire's wording. A built-in keeps its name.
- **The organization's own.** An `email_templates` row with `built_in_id` null and a name. Automations and sends name it
  by its id, a Guid string.

A template is a Liquid subject and a Liquid plain-text body (Fluid, ADR-0008). The body is text, not HTML: the email's
HTML part is built from the rendered text (a paragraph per blank line, links that show their own address, everything
else encoded), and the text part is the rendered text itself. So a template cannot carry a hidden link, a tracking
image or a script, and a candidate's name cannot inject markup.

`EmailTemplates.Validate` checks wording on every save, preview and test send:

- Variables: `candidateName`, `jobTitle`, `organizationName`, `stageName`, and `reason` in the rejection email only.
  Nothing else, and no member access (`candidateName.size`).
- Tags: `if`, `unless`, `case` and comments. No `assign`, `for`, `include`, `render` or other tags, and no ranges, so a
  template cannot loop or read anything beyond its values. Rendering also has a step limit.
- Filters: `capitalize`, `default`, `downcase`, `strip`, `truncate`, `upcase`.
- The rejection email must use `{{ reason }}`: a `Reject` action's reason goes into it, and the candidate must be told
  why (workflow-engine.md §2).

The preview (`POST /v1/email-templates/preview`) renders unsaved wording with a made-up candidate, Jordan Lee. The
dashboard shows the HTML part in a sandboxed frame, never in the page. The test send (`POST /v1/email-templates/test`)
sends the same rendering to the signed-in person's own address, from the API, with `[Test]` before the subject.

## Sending

`POST /v1/emails` takes a template id and up to 200 application ids. The API publishes one `SendApplicationEmail`
message per application through the outbox, in one transaction, and answers 202 with how many it queued and skipped.
The worker's `SendApplicationEmailHandler` renders the organization's current wording for that candidate, sends it and
adds an `EmailSent` timeline entry with the template id and the subject. The message's send id, created once in the
API, is that entry's id, so a redelivered message finds the entry and sends nothing. As with the `SendEmail` action, a
crash between the send and the commit can repeat one email. A send that cannot go when the worker reaches it (the
template was deleted, or the application ended or was removed, since the request) is dropped with a warning in the
worker's log and no timeline entry: Activity lists emails that went out.

`ApplicationEmailed` (an `ITenantEvent`) tells open dashboards to refetch the application's Activity.

`SendEmail` and `Reject` automation actions go through the same code (`ApplicationEmails.SendAsync`), so their emails
are on the timeline too, linked to the automation run. An automation may name any organization template. The API checks
that the template exists when a person saves a workflow, and refuses to delete a template that an open or draft job's
workflow, or a hiring template, still names. A template deleted anyway (a race) fails the action with the reason.

## Rules

- **The rejection email goes only with `Reject`.** It cannot be sent by hand or by `SendEmail`: the API refuses it and
  the dashboard never offers it. The organization may reword it, but it must keep the reason, and ScreenHire ends it
  with a fixed sentence (`EmailTemplates.AutomatedDecisionNotice`): the decision was automatic, and a person reviews it
  on request (docs/research/legal.md, principle 1). The preview shows that sentence too.
- **`stage-change` never goes to an ended application.** "Moved to the next step: Rejected" would be a rejection no
  `Reject` action guarded. The API skips such applications in a send; the worker checks again when it sends.
- **No bulk email picked by integrity signals.** On the candidates list and a job's pipeline, Email for a selection is
  hidden while an integrity filter or an integrity saved view picked the rows, as bulk moves to an ending stage are
  (ADR-0004, `bulkMoveStages`). One candidate at a time, from their page, is a person's decision.
- **People only.** The email routes take no API key. Owners and admins change templates and test them; owners, admins
  and recruiters send; viewers read templates and send nothing.
- **Hidden without a sender.** With no `IEmailSender` (no Mailpit connection locally, no provider in production),
  `sendingEnabled` is false, the dashboard hides every send control, and `POST /v1/emails` is a 404. Templates can
  still be edited.

## Not built

- Replies and an inbox. Candidates reply to the sender address, which nobody reads. Threading replies onto an
  application needs a real email provider with inbound routing; the provider is still to be chosen.
- Attachments, scheduled sends and per-person signatures.
