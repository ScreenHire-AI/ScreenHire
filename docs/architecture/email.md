---
type: Design
title: "Email to candidates"
description: "Organization email templates in Liquid with a plain-text body over fixed variables, the email composer (a template or a blank email changed for one send, previewed with the candidate's details), each email kept as it went for Messages, sending through the worker, the rules that the rejection email goes only with Reject and the follow-up questions email only with SendQuestionnaire, and what is not built (replies, an inbox)."
tags: [email, templates, liquid]
sources:
  - resource: src/ScreenHire.Domain/Hiring/EmailTemplate.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplates.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplateStore.cs
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-received.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-received.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-rejected.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-rejected.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/questionnaire-request.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/questionnaire-request.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/stage-change.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/stage-change.subject.liquid
  - resource: src/ScreenHire.Application/Applications/SendApplicationEmail.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationEmail.cs
  - resource: apps/api/Endpoints/EmailEndpoints.cs
  - resource: apps/api/Contracts/EmailContracts.cs
  - resource: apps/dashboard/src/lib/components/email-composer.svelte
  - resource: apps/dashboard/src/lib/components/email-composer-sheet.svelte
  - resource: apps/dashboard/src/lib/components/candidate/messages-tab.svelte
  - resource: src/ScreenHire.Application/Applications/ApplicationNotes.cs
---
# Email to candidates

A hiring team writes its own emails and sends them to candidates by hand, to a selection, or from an automation. A
person starts from one of the organization's templates or a blank email and may change it for that one send; an
automation sends a template as it stands. Every email goes out through the worker, is kept exactly as it went, and
appears in the application's Activity and the candidate's Messages. Candidates cannot reply into ScreenHire yet (see
"Not built").

## Templates

There are two kinds, both listed by `GET /v1/email-templates` and edited on the Templates page's Emails tab
(`/templates/emails/{id}`; the old `/settings/email-templates` addresses redirect there):

- **Built-in.** `application-received`, `stage-change`, `questionnaire-request` and `application-rejected`
  (`EmailTemplateIds`). ScreenHire's
  wording lives in `src/ScreenHire.Infrastructure/Email/Templates` (`{id}.subject.liquid`, `{id}.body.liquid`). An
  organization that edits one gets an `email_templates` row with `built_in_id` set; deleting the row restores
  ScreenHire's wording: the dashboard calls this Reset to original, and asks first. A built-in keeps its name and is
  never archived. Edits, restores and deletions go through the change tracker, so each template's History records them
  ([change-history.md](change-history.md)).
- **The organization's own.** An `email_templates` row with `built_in_id` null and a name. Automations and sends name it
  by its id, a Guid string. `POST /v1/email-templates/{id}/archive` sets `archived_at`, which takes it out of the
  composer's and the automation form's lists; automations that already name it keep sending it, and `unarchive` puts
  it back. An archived template reads only until it is restored (409 on an edit). The dashboard offers Archive in
  place of delete; `DELETE` still deletes one no automation names.

A template is a Liquid subject and a Liquid plain-text body (Fluid, ADR-0008). The body is text, not HTML: the email's
HTML part is built from the rendered text (a paragraph per blank line, links that show their own address, everything
else encoded), and the text part is the rendered text itself. So a template cannot carry a hidden link, a tracking
image or a script, and a candidate's name cannot inject markup. The rendered subject is one line, cut at 1,000
characters (`ApplicationEmail.MaxSubjectLength`): variables can make it far longer than its 200-character wording.

`EmailTemplates.Validate` checks wording on every save, preview and test send:

- Variables: `candidateName` (the full name), `firstName`, `lastName`, `jobTitle`, `organizationName`, `stageName`,
  `reason` in the rejection email only, and `questionnaireLink` in the follow-up questions email only. Nothing else, and
  no member access (`candidateName.size`). A candidate from before the apply form asked for two names has their whole
  name as `firstName` and an empty `lastName`. The built-in templates greet the candidate by `firstName`.
- Tags: `if`, `unless`, `case` and comments. No `assign`, `for`, `include`, `render` or other tags, and no ranges, so a
  template cannot loop or read anything beyond its values. Rendering also has a step limit.
- Filters: `capitalize`, `default`, `downcase`, `strip`, `truncate`, `upcase`.
- The rejection email must use `{{ reason }}`: a `Reject` action's reason goes into it, and the candidate must be told
  why (workflow-engine.md §2).
- The follow-up questions email (`questionnaire-request`) must use `{{ questionnaireLink }}`: the signed link to the
  questionnaire is the reason it exists (workflow-engine.md §8).

The template preview (`POST /v1/email-templates/preview`) renders unsaved wording with a made-up candidate, Jordan Lee. The
dashboard shows the HTML part in a sandboxed frame, never in the page. The test send (`POST /v1/email-templates/test`)
sends the same rendering to the signed-in person's own address, from the API, with `[Test]` before the subject.

## The composer

The email composer (`EmailComposer`, design system section 11) opens in a sheet from a list's selection and the
candidate's header, and sits at the top of the candidate's Messages tab. Start from offers a blank email and every
template but the rejection and follow-up questions emails. Picking a template fills the subject and the message with its
wording; changing them changes this email only, and the template stays as it was. A blank email has no template, and
nothing turns it into one. Insert variable puts a variable at the caret. Preview asks the API for the first candidate's
email (`POST /v1/emails/preview` with the application and the wording), rendered with that candidate's details; a
person's wording may use the general variables only, so `{{ reason }}` and `{{ questionnaireLink }}` are refused.

## Sending

`POST /v1/emails` takes up to 200 application ids and the email: the wording (`subject` and `body`), a template id to
send the template as it stands, or both, when the wording is a template changed for this send and the id records where
it started. The API renders each candidate's own copy in the request, with their details, and stores it in
`application_emails` (`ApplicationEmail`). In the same transaction it publishes one `SendApplicationEmail` per
application through the outbox, and answers 202 with how many it queued and skipped. The message carries ids only (the
organization, the application, the send and the person): wording a person typed can hold personal data, which never
travels in a message ([messaging.md](messaging.md)). The copy's id is the send's id.

The worker's `SendApplicationEmailHandler` sends the stored copy exactly as it is and adds an `EmailSent` timeline
entry with the template id, the subject and `emailId`, the copy. The entry's id is the send's id too, so a redelivered
message finds the entry and sends nothing. As with the `SendEmail` action, a crash between the send and the commit can
repeat one email. A send that cannot go when the worker reaches it (the application was removed, its copy with it, or
it ended and the email is the stage-change email, or the worker has no email sender) is dropped with a warning in the worker's log and no timeline entry,
and the worker deletes the copy of an email it did not send: Activity and Messages list emails that went out. A
message queued before copies were kept has none, and is dropped the same way.

`ApplicationEmailed` (an `ITenantEvent`) tells open dashboards to refetch the application's Activity and the
candidate's Messages.

`SendEmail`, `Reject` and `SendQuestionnaire` automation actions queue their email as a `SendAutomationEmail`
message in the action's own transaction, and the worker's `SendAutomationEmailHandler` renders the template's current
wording, sends it and keeps the copy through the same code (`ApplicationEmails`), so their emails are on the timeline
and in Messages too, linked to the automation run. The follow-up questions email's copy says "[the candidate's link,
not kept in this copy]" in place of its signed link: the link lets whoever holds it answer as the candidate, so it goes
to the candidate alone. Because the message leaves only when the action commits, an action retried after a conflict
(two automations changing one application at once) sends one email. An automation may name any organization template.
The API checks that the template exists when a person saves a workflow, and refuses to delete a template that an open
or draft job's workflow, or a hiring template, still names. A template deleted anyway (a race) fails the action with
the reason.

## Messages

`GET /v1/candidates/{candidateId}/emails` lists every email to the candidate, about any of their applications, newest
first: the subject, who or which automation sent it, when, the job, and `kept`. `GET /v1/emails/{emailId}` returns one
email as it went: the address, the subject, the HTML part and the plain-text part. Emails sent before copies were kept
have only their subject (`kept` is false), and Messages says so. A copy is never changed: the application role cannot
update `application_emails`, and a copy goes with its application. Every member reads them, viewers included.

## Rules

- **The rejection email goes only with `Reject`.** It cannot be sent by hand or by `SendEmail`: the API refuses it and
  the dashboard never offers it. The organization may reword it, but it must keep the reason, and ScreenHire ends it
  with a fixed sentence (`EmailTemplates.AutomatedDecisionNotice`): the decision was automatic, and a person reviews it
  on request (docs/research/legal.md, principle 1). The preview shows that sentence too.
- **The follow-up questions email goes only with `SendQuestionnaire`.** Its link is made for one questionnaire request,
  so it cannot be sent by hand or by `SendEmail`: the API and the validator refuse it, the worker drops it, and the
  dashboard never offers it. The organization may reword it, but it must keep the link.
- **`stage-change` never goes to an ended application.** "Moved to the next step: Rejected" would be a rejection no
  `Reject` action guarded. The API skips such applications in a send; the worker checks again when it sends.
- **No bulk email picked by integrity signals.** On the candidates list and a job's pipeline, Email for a selection is
  hidden while an integrity filter or an integrity saved view picked the rows, as bulk moves to an ending stage are
  (ADR-0004, `bulkMoveStages`). One candidate at a time, from their page, is a person's decision.
- **People only.** The email routes take no API key, the Messages list and the kept copies included. Owners and admins
  change templates and test them; owners, admins and recruiters send; viewers read templates and kept emails and send
  nothing.
- **Hidden without a sender.** With no `IEmailSender` (no Mailpit connection locally, no provider in production),
  `sendingEnabled` is false, the dashboard hides every send control, and `POST /v1/emails` is a 404. Templates can
  still be edited.

## Email to the team

Sign-in links and invitations go out inside the request, because their tokens must never sit in the outbox's message
table. A mention in a note has no secret, so its email goes through the outbox like a candidate's: the API publishes
`TeammateMentioned` with the note, and the worker's `TeammateMentionedHandler` sends one fixed email in ScreenHire's
own words (`MentionEmail`), not an organization template. It says who mentioned the person and on which candidate and
job, links to the note on the candidate's Activity tab (`ScreenHire:Domains:AppHost`, which the AppHost gives the
worker too), and never quotes the note, so candidate details stay in ScreenHire. The mention's row records the send,
so a redelivered message sends nothing; a mention removed, a note deleted or a person who left before the worker gets
to it gets no email.

## Not built

- Replies and an inbox. Candidates reply to the sender address, which nobody reads. Threading replies onto an
  application needs a real email provider with inbound routing; the provider is still to be chosen.
- Attachments, scheduled sends and per-person signatures.
