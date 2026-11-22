---
type: Design
title: "Email to candidates"
description: "Organization email templates in Liquid with a rich-text body over fixed variables, the email composer (a template or a blank email changed for one send, previewed with the candidate's details), each email kept as it went for Messages, sending through the worker, the rules that the rejection email goes only with Reject, the follow-up questions email only with a form's own send and the invitation to apply only with Invite to apply, the link to finish a saved application, the link to the candidate's own page that every email to a candidate ends with, sending through SendGrid, candidates' replies with their attachments, bounces and spam complaints, the company's own sending domain, and what is not built (an inbox, attachments on outgoing mail, scheduled sends)."
tags: [email, templates, liquid]
sources:
  - resource: src/ScreenHire.Domain/Hiring/EmailTemplate.cs
  - resource: src/ScreenHire.Application/Verification/Models/VerificationAlerts.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplates.cs
  - resource: src/ScreenHire.Domain/Hiring/EmailTemplateIds.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailTemplateStore.cs
  - resource: apps/dashboard/src/routes/(app)/templates/emails/[templateId]/+page.svelte
  - resource: apps/dashboard/src/lib/components/email-template-form.svelte
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-received.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-received.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-rejected.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/application-rejected.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/questionnaire-request.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/questionnaire-request.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/stage-change.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/stage-change.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-confirmation.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-confirmation.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-cancelled.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-cancelled.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-new-times.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-new-times.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-reminder.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-reminder.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation-suggest.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation-suggest.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation-proposed.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-invitation-proposed.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-options-updated.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-options-updated.subject.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-more-times.body.liquid
  - resource: src/ScreenHire.Infrastructure/Email/Templates/interview-more-times.subject.liquid
  - resource: src/ScreenHire.Application/Applications/SendApplicationEmail.cs
  - resource: src/ScreenHire.Domain/Applications/ApplicationEmail.cs
  - resource: apps/api/Endpoints/EmailEndpoints.cs
  - resource: apps/api/Contracts/EmailContracts.cs
  - resource: apps/dashboard/src/lib/components/email-composer.svelte
  - resource: apps/dashboard/src/lib/components/email-composer-sheet.svelte
  - resource: apps/dashboard/src/lib/components/candidate/messages-tab.svelte
  - resource: apps/dashboard/src/lib/email-history.ts
  - resource: apps/api/Email/EmailSnippet.cs
  - resource: src/ScreenHire.Application/Applications/ApplicationNotes.cs
  - resource: apps/api/Applications/ApplicationDrafts.cs
  - resource: src/ScreenHire.Domain/Applications/CandidateLanguage.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewText.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailHeader.cs
  - resource: src/ScreenHire.Infrastructure/Email/IEmailSender.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailOptions.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailRegistration.cs
  - resource: src/ScreenHire.Infrastructure/Email/SmtpEmailSender.cs
  - resource: src/ScreenHire.Infrastructure/Email/SendGridEmailSender.cs
  - resource: src/ScreenHire.Infrastructure/Email/SendGridEventSignature.cs
  - resource: src/ScreenHire.Infrastructure/Email/ReplyAddresses.cs
  - resource: src/ScreenHire.Infrastructure/Email/MailRoute.cs
  - resource: src/ScreenHire.Application/Applications/CandidateMailRouting.cs
  - resource: src/ScreenHire.Application/Applications/CandidateReplies.cs
  - resource: src/ScreenHire.Application/SendingDomains/SendingDomainProviders.cs
  - resource: src/ScreenHire.Application/SendingDomains/SendingDomainService.cs
  - resource: src/ScreenHire.Application/SendingDomains/RecheckSendingDomain.cs
  - resource: src/ScreenHire.Domain/Applications/InboundEmail.cs
  - resource: src/ScreenHire.Domain/Organizations/SendingDomain.cs
  - resource: src/ScreenHire.Domain/Candidates/Candidate.cs
  - resource: apps/api/Endpoints/EmailWebhookEndpoints.cs
  - resource: apps/api/Endpoints/SendingDomainEndpoints.cs
  - resource: apps/api/Email/InboundEmailIngest.cs
  - resource: apps/api/Email/EmailAttachmentStore.cs
  - resource: apps/api/Contracts/SendingDomainContracts.cs
  - resource: apps/dashboard/src/routes/(app)/settings/sending-domain/+page.svelte
  - resource: apps/dashboard/src/lib/components/candidate/received-email.svelte
  - resource: apps/dashboard/src/lib/components/candidate/sent-email.svelte
  - resource: apps/dashboard/src/lib/email-issue.ts
  - resource: apps/dashboard/src/lib/received-email.ts
  - resource: apps/dashboard/src/lib/sending-domain.ts
  - resource: apps/api/Email/AttachmentFiles.cs
  - resource: src/ScreenHire.Infrastructure/Email/TransactionalEmail.cs
  - resource: src/ScreenHire.Infrastructure/Email/CandidatePortalFooter.cs
  - resource: src/ScreenHire.Application/Notifications/NoticeDelivery.cs
  - resource: src/ScreenHire.Application/Notifications/MemberNotices.cs
  - resource: src/ScreenHire.Application/Notifications/Digest.cs
  - resource: src/ScreenHire.Application/Scheduling/InterviewHandlers.cs
---
# Email to candidates

A hiring team writes its own emails and sends them to candidates by hand, to a selection, or from an automation. A
person starts from one of the organization's templates or a blank email and may change it for that one send; an
automation sends a template as it stands. Every email goes out through the worker, is kept exactly as it went, and
appears in the application's Activity and the candidate's Messages. A candidate's answer comes back to the same
application (see "Replies"). SendGrid sends it in production, Mailpit locally (see "Sending provider").

## Templates

There are two kinds, both listed by `GET /v1/email-templates` and edited on the Templates page's Emails tab
(`/templates/emails/{id}`; the old `/settings/email-templates` addresses redirect there):

- **Built-in.** `application-received`, `stage-change`, `questionnaire-request`, `application-rejected`,
  `talent-pool-invitation` and the nine interview emails `interview-invitation`, `interview-confirmation`,
  `interview-cancelled`, `interview-new-times`, `interview-reminder`, `interview-invitation-suggest` (tell us when you're free),
  `interview-invitation-proposed` (we'll suggest a time), `interview-options-updated` and `interview-more-times`
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

A template is a Liquid plain-text subject and a Liquid rich-text body (Fluid, ADR-0008). The body is sanitized HTML
limited to the editor's elements (bold, italic, two heading levels, lists and links), at most 20,000 characters with
its formatting; [rich text](rich-text.md) has the canonical form and the render. Values are HTML-encoded as they
render, the template's own addresses and the questionnaire link become links that show themselves, and the result is sanitized again, so a template
cannot carry a script, an image or a tracking pixel, and a candidate's name cannot inject markup. A link may have its
own text ("Apply here"), and the text part then prints its address after it; a link's address may not hold a variable,
so a candidate's details never go to another site in a URL. ScreenHire adds no click tracking. The text part is the
HTML as plain text. In the dashboard, each variable in the message is a chip, and in a template's subject too, where it
is one line of plain text (`SubjectEditor`) stored as the Liquid text. ScreenHire puts the organization's header first
in the HTML part, after the body is sanitized: its logo, a plain image at one address for every recipient, or its
name, on its second colour (`EmailHeader`, [branding.md](branding.md) section 6). A white header has no side padding, so
the name lines up with the body text; a band of the second colour keeps 20px each side. That logo is the only image an email
carries, and a template cannot add one. The rendered subject is one line, cut at
1,000 characters (`ApplicationEmail.MaxSubjectLength`): variables can make it far longer than its 200-character wording.

`EmailTemplates.Validate` checks wording on every save, preview and test send:

- Variables: `candidateName` (the full name), `firstName`, `lastName`, `jobTitle`, `organizationName`, `stageName`,
  `reason` in the rejection email only, and `questionnaireLink` in the follow-up questions email only. Nothing else, and
  no member access (`candidateName.size`), except `meeting.name` in the interview emails: what the candidate's stage calls
  the meeting, in lower case (`trial shift`, `interview` when the stage names none). A candidate from before the apply form asked for two names has their whole
  name as `firstName` and an empty `lastName`. The built-in templates greet the candidate by `firstName`.
- Tags: `if`, `unless`, `case` and comments. No `assign`, `for`, `include`, `render`, `raw` or other tags, and no ranges, so a
  template cannot loop or read anything beyond its values. Rendering also has a step limit.
- Filters: `capitalize`, `default`, `downcase`, `strip`, `truncate`, `upcase`.
- The rejection email must use `{{ reason }}`: a `Reject` action's reason goes into it, and the candidate must be told
  why (workflow-engine.md §2).
- The follow-up questions email (`questionnaire-request`) must use `{{ questionnaireLink }}`: the signed link to the
  questionnaire is the reason it exists (workflow-engine.md §8).
- The interview emails ([interview scheduling](interview-scheduling.md)) are sent only by scheduling, which makes their
  links, so like the talent pool invitation they are `invitationOnly` in the API: the composer never offers them, the
  generic send route refuses them and no `SendEmail` action may name them. `interview-invitation` must use
  `{{ bookingLink }}` and may use `{{ interviewLength }}`. Every interview email may use `{{ meeting.name }}`, and the built-in
  wording does, so a trial shift is not called an interview; start a sentence with it as `{{ meeting.name | capitalize }}`.
  `interview-confirmation` must use `{{ interviewTime }}` and
  `{{ manageLink }}` and may use `{{ meetingLink }}` (empty until the organizer's calendar event has one, so wrap it in
  `{% if meetingLink != blank %}`) and `{{ interviewLength }}`. `interview-cancelled` may use `{{ interviewTime }}`, which
  is empty when the offer was withdrawn before a time was booked. `interview-new-times` must use `{{ bookingLink }}` and may use
  `{{ interviewTime }}` (the time that stays until the candidate picks) and `{{ interviewLength }}`. `interview-reminder` is sent
  a day and an hour before a booked interview and must use `{{ interviewTime }}` and `{{ manageLink }}`; it may use
  `{{ meetingLink }}`. The worker writes `interviewTime` from the instant, the interview's time zone and the candidate's
  language (`Tuesday, November 3, 2026, 10:00 AM to 10:30 AM (Europe/London)` for en-US, `Tuesday 3 November 2026, 10:00 to
  10:30 (Europe/London)` for en-GB); the API never sends formatted dates. See "Dates in the candidate's language" below.
  The invitation follows how the candidate picks ([interview scheduling](interview-scheduling.md#three-ways-to-pick-a-time)),
  as a separate template each, so an organization's own wording of one never sends another way's words.
  `interview-invitation-suggest`, `interview-invitation-proposed` (whose `{{ interviewTime }}` is the team's times, joined by
  "or"), `interview-options-updated` ("We've updated your options", with `{{ interviewTime }}` the booked time a move keeps) and
  `interview-more-times` (with `{{ teamNote }}`, the team's message, empty when it wrote none) each must use `{{ bookingLink }}`.
  The confirmation and the cancellation of a booked interview carry a calendar invitation, `invite.ics`
  (`EmailMessage.Attachments`, sent as an attachment by SMTP and SendGrid).
  The copy the team keeps hides the candidate's own link, as the follow-up questions email does.

The template preview (`POST /v1/email-templates/preview`) renders unsaved wording with a made-up candidate, Jordan Lee.
The dashboard shows the HTML part in a sandboxed frame (no scripts, forms or popups; `allow-same-origin` only so the frame
can take the email's height), never in the page, in the preview beside the wording where both
fit, or behind the Edit / Preview switch; the window icon opens it in a second window (`/preview/email`) that the editor
feeds each rendering. The test send (`POST /v1/email-templates/test`) sends the same rendering to the signed-in person's
own address, from the API, with `[Test]` before the subject.

## The composer

The email composer (`EmailComposer`, design system section 11) opens in a sheet from a list's selection and the
candidate's header, and sits under the emails on the candidate's Messages tab. A link to one email (`?tab=messages#email-<id>`,
from Activity or a toast) opens its row and brings it into view. Start from offers a blank email and every
template but the rejection and follow-up questions emails. Picking a template fills the subject and the message with its
wording; changing them changes this email only, and the template stays as it was. A blank email has no template, and
nothing turns it into one. Insert variable puts a variable at the caret: a chip in the message, `{{ name }}` in the subject. Preview asks the API for the first candidate's
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
first: the subject, who or which automation sent it, when, the job, `kept`, and `snippet`. The snippet is the first line
of the text part that says something: not a lone greeting such as "Hi Casey,", and never the quoted email under a reply.
`EmailSnippet` cuts it at a word to 140 characters and reads only the first 400 characters of the text from the
database. A reply also has `inReplyTo`, the id of the sent email it answers
(the send its reply address named), so the tab shows it under that email. `GET /v1/emails/{emailId}` returns one
email as it went: the address, the subject, the HTML part and the plain-text part. Emails sent before copies were kept
have only their subject (`kept` is false), and Messages says so. A copy is never changed: the application role cannot
update `application_emails`, and a copy goes with its application. Every member reads them, viewers included.

## Rules

- **The rejection email goes only with `Reject`.** It cannot be sent by hand or by `SendEmail`: the API refuses it and
  the dashboard never offers it. The organization may reword it, but it must keep the reason, and ScreenHire ends it
  with a fixed sentence (`EmailTemplates.AutomatedDecisionNotice`): the decision was automatic, and a person reviews it
  on request (docs/research/legal.md, principle 1). The preview shows that sentence too.
- **The follow-up questions email goes only with a form's own send.** Its link is made for one questionnaire request,
  so it goes only with `SendQuestionnaire` or with Send a form on the candidate's page (`SendQuestionnaireLink`,
  workflow-engine.md section 8), never from the composer or by `SendEmail`: the API and the validator refuse it there,
  the worker drops it, and the composer never offers it. The organization may reword it, but it must keep the link.
- **The invitation to apply (`talent-pool-invitation`) goes only with Invite to apply** ([saved candidates](talent-pool.md)). It must use
  `{{ jobLink }}` (the job's careers page; `{{ jobTitle }}` is that job here) and `{{ removeLink }}` (the candidate's
  signed link to stop being kept), which only it may use. The worker renders it, so its kept copy says "[the
  candidate's link, not kept in this copy]" in place of the remove link. It cannot be sent by hand or by `SendEmail`,
  and the composer never offers it (`invitationOnly`).
- **`stage-change` never goes to an ended application,** nor to a saved one. "Moved to the next step:
  Rejected" would be a rejection no `Reject` action guarded. The API skips such applications in a send; the worker checks again when it sends.
- **No bulk email picked by verification signals.** On the candidates list and a job's pipeline, Email for a selection is
  hidden while a verification filter or a verification saved view picked the rows, as bulk moves to an ending stage are
  (ADR-0004, `bulkMoveStages`). One candidate at a time, from their page, is a person's decision.
- **People only, over HTTP.** The email routes take no API key, the Messages list and the kept copies included. The MCP
  server's `send_email`, `draft_email` and `read_email_thread` call the same handlers for a key with the person's
  confirmation before a send ([MCP server](mcp-server.md)); a key's send carries `ActorApiKeyId` on
  `SendApplicationEmail`, and its EmailSent entry names the key. Owners and admins
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
so a redelivered message sends nothing; a mention removed, a note deleted, a person who left, or one who no longer sees
the job (ADR-0015) before the worker gets to it gets no email. A note can mention only someone who sees the candidate.
Someone who linked their Slack account also gets the mention as a Slack direct message, its own message beside this
one, so a failing email never holds it up ([slack.md](slack.md)).

Each person chooses, per kind, whether ScreenHire emails them at once, in a daily digest at 08:00 their time, or not at all
(Your account > Notifications, [notifications.md](notifications.md#each-persons-choices)). The mention email above, a
candidate's reply, an interview booked, moved or cancelled, a task given to them, a new application on their jobs and a
teammate's move or note are all sent through that choice (`NoticeDelivery`), in ScreenHire's own words (`MentionEmail`,
`ReplyEmail`, `MemberNoticeEmails`): names and titles only, never a note's, a task's or a reply's text. The digest
(`DigestEmail`) is one email with a line per item, grouped by kind, each linking to its record.

Every email to the team about their work ends with a short rule and "Change what we email you", linking to
`/account/notifications` (`TeamEmail.WithFooter`): every kind above, the verification and AI usage alerts, the trial's
reminders, the interviewer's day-before reminder, the email domain alert, the data request alert and the digest. With no
dashboard address the footer is left out, as the links are. Sign-in links and invitations are not notifications and have
none. Team email is not candidate email: it gets no candidate-portal footer and no reply address.

## Verification alerts

Two emails go to teammates who want them. Verification sets where each member starts (Verification suggested on the Checks
tab, AI usage on the AI use tab), and each member's own choice on Your account wins, including the daily digest
(`VerificationAlertWording`, ScreenHire's own wording like the mention email, not a template). "An application needs a closer
look" goes once, when an application first reaches Verification suggested, to members who hold Review verification and see the
job (ADR-0015); it names the job, says the application needs a closer look and links to it, and carries no level, candidate name,
reason or evidence. The AI usage alerts ("AI usage is at 80% of this month's budget", the cap, a spike; `AiUsageAlertWording`) go once each to members who see AI usage ([ai-usage.md](ai-usage.md)).
Nothing is sent when the host has no email sender or no dashboard address, and a Slack direct message can go beside either
([slack.md](slack.md), [verification engine](verification-engine.md) section 10.11).

## Dates in the candidate's language

Every date in an email to a candidate is written in the language of the browser the candidate applied from. The careers
server sends the first tag of its `Accept-Language` with the submission (`language`), and the API records it as
`JobApplication.Language` when it has the shape of a language tag, and drops it otherwise. `CandidateLanguage.Culture`
turns the recorded tag into a .NET culture and reads as en-GB for an application with none (added by a recruiter, or made
before this was recorded), for a language the host has no data for and for a host without globalization data. The interview
emails (`InterviewText.Time`, called with that culture) and the link to finish a saved application (`CandidateLanguage.LongDate`)
write through that one helper, so no template has a format of its own. Words, zone names and the word "to" stay English, and
a time keeps the zone it states. The language is a display preference: no check reads it, and no screen shows it to the team.
Emails to the hiring team (the trial's reminders, the interviewers' reminder) are not candidate emails and keep their
fixed formats.

## The link to finish a saved application

A candidate who saves the apply form to finish later gets one email with the link back to it ([apply-flow.md](apply-flow.md)
§7). It is ScreenHire's own wording (`ApplicationDrafts.Compose`), not an organization template: there is no
application to name yet, and nothing in it is the hiring team's to word. It names the job and the organization, links to
the job's apply page with the draft's token, says the date the answers are deleted (in the language the save request
named, since no application records one yet) and that files are attached again.
Like a sign-in link it goes out inside the request, because its token lets whoever holds it read the draft and must not
sit in the outbox's message table, and it is never kept as a copy or shown in Messages. An address gets at most 5 a day
in one organization. With no `IEmailSender`, saving is hidden on the careers page.

## The link to the candidate's page

Every email to a candidate about an application ends with a link to their own page, where they see where each application
stands and read and answer their messages ([candidate portal](candidate-portal.md)). `CandidatePortalFooter` adds ScreenHire's
fixed sentence and the link after an organization's template is rendered and sanitized, as the header is, so a template cannot
change or remove it. The link is a capability, so it is added to the email that goes out (`MailRoute.PortalLink`, which
`CandidateMailRouting.ForAsync` fills with the candidate's signed link) and never to the copy kept for Messages, as with the
follow-up questions email's own link. The worker issues the token when it sends. The three interview emails, the invitation to
apply and the three data request emails carry it as well. The saved-application link and mail to the team do not. The composer's
preview and a template's test send show the line with the page where anyone asks for a link, since only the worker can issue a
candidate's own.

## Sending provider

[ADR-0022](../adr/0022-sendgrid-through-azure-marketplace.md): ScreenHire sends through SendGrid, bought on the Azure
Marketplace, so email is billed with the rest of the hosting. Everything above talks to `IEmailSender`; which adapter is
registered depends on configuration (`ScreenHire:Email:*`, `EmailOptions`), the same in the API and the worker:

- **SendGrid** when `SendGrid:ApiKey` is set (the `sendgrid-api-key` secret parameter, asked for only with
  `Email:SendGrid:Enabled=true` in the AppHost). `SendGridEmailSender` posts to the v3 Mail Send API with plain
  `HttpClient`, in a client of its own with no retries, because a retried POST could send one email twice. Click and open
  tracking are off. Each email carries the organization and the send as custom arguments, which SendGrid echoes in its
  events.
- **Mailpit** (`SmtpEmailSender`) locally, from the AppHost's connection string. It honours From and Reply-To, so a
  developer sees the reply address in Mailpit.
- **Neither:** no sender, and email is hidden, as under "Rules".

Another provider (Azure Communication Services Email) would be one more class behind `IEmailSender` and
`ISendingDomainProvider`. `ScreenHire:Email:From` is the default From address.

## Replies

Every email about an application goes out with `Reply-To: reply+<token>@<reply host>` (`ReplyAddresses`,
`CandidateMailRouting`), the three interview emails included. The token is lowercase base32 of the organization, the send (the `application_emails` id) and a
4-byte HMAC under the generated `email-reply-key`: 58 characters, so the local part is exactly 64, the limit. The send's
id, not the application's, so the original sender is known. The sign-in, invitation and saved-application emails carry
none: they are not correspondence on an application. The talent pool invitation does, and a flagged address gets none. Without a reply key and a reply host
(`ScreenHire:Email:ReplyDomain`, or the sandbox) no Reply-To is added and nothing else changes.

SendGrid Inbound Parse posts a reply to `POST /v1/webhooks/email/inbound`, which is anonymous, not in the generated
client, rate limited, and at most 25 MB. Inbound Parse cannot sign a post, so the URL SendGrid is given holds HTTP Basic
credentials (user `sendgrid`, the `sendgrid-inbound-secret` as the password): they travel in a header, never in a logged
path. Behind that, the signed reply address must be valid, and the webhook reads it from the envelope, not the To header
(candidates reply to all, forward and edit). A post with no valid address, or for an email that never went, changes
nothing and still answers 200, so SendGrid does not retry it. In one transaction `InboundEmailIngest` then stores:

- the reply (`inbound_emails`): who it is from, the subject, the plain text, and the HTML cleaned to the editor's
  elements by `RichText.Sanitize`, shown only in a sandboxed frame like a sent email. The same `Message-ID` twice is one
  reply. An out-of-office or other automatic answer (`Auto-Submitted`, an auto-reply `Precedence`) is kept and notifies
  nobody;
- its attachments (`inbound_email_attachments`), only PDF, Word and PNG or JPEG files whose first bytes match, at most
  10 MB each and ten in all. Anything else is dropped, and the text says how many. Each goes to the private
  `email-attachments` container, then a `ScanEmailAttachment` message runs the same `IMalwareScanner` as an applicant's
  upload. A reviewer downloads one through a short-lived read link, and the route answers 409 while the scan is pending
  and for an infected file. Erasing the candidate deletes the replies and these files ([candidates](candidates.md#erasure));
- an `EmailReceived` timeline entry with the reply's id, no actor, in Activity under Emails. It counts as the candidate's
  activity;
- a `CandidateReplied` event. Open dashboards refetch Messages and Activity. The person who sent the original gets a card
  in the tray, and the worker's `CandidateRepliedHandler` emails them once (the reply's `notified_at`): fixed wording, a
  link to Messages, never the reply's text, and only if they can still see the job. An email an automation sent has
  no one to email, so every member who sees the job gets the card and nobody gets mail.

A candidate can also write from their own page. `InboundEmailIngest.IngestForAsync` takes the reply's target (the organization and
the send) directly, so a reply from the page runs the same steps as one from the webhook: the reply, its `EmailReceived` entry, its
attachments and their scans, and the event that tells the sender. The page names the target itself (the latest email that went for
the application), uses a `Message-ID` of its own form id so sending a form twice stores one reply, and never clears a bounce or
spam flag. The two stay in one thread because both answer the same sent email.

Messages lists sent and received together, newest first (`direction`); a reply opens to who it is from, the message and its
files. `GET /v1/replies/{replyId}` and its attachment route are read like sent mail, row-level security and job access
included. A person's reply from a flagged address clears the flag (see below).

Locally and in tests, `ScreenHire:Email:Sandbox:Enabled` (Development only; `EmailSandboxGate` stops a real host with it
on) maps `POST /v1/dev/inbound-email`, which delivers a reply as SendGrid would, taking the Reply-To address of an email in
Mailpit as `to`. No test needs SendGrid. [Local demo](../operations/local-demo.md) has the steps.

## Bounces and complaints

`POST /v1/webhooks/email/events` takes SendGrid's signed event webhook: an ECDSA P-256 signature over the timestamp and the
body, checked against `SendGrid:EventWebhookPublicKey` (`SendGridEventSignature`), within five minutes. Without the key
the route is a 404; a bad signature is a 401. It reads three kinds of event on an email carrying our custom arguments: a
hard bounce (a `blocked` bounce is soft and ignored), a drop because the address bounced before, and a spam report. The
candidate is found through the send, and must still have the event's address.

The candidate then carries `emailIssue` (`Bounced` or `SpamComplaint`, with when and the provider's short reason):

- ScreenHire sends the address nothing more. A person's send counts the candidate as skipped, the worker checks again, and
  an automation records that nothing was sent. The composer and Email buttons are hidden for that candidate.
- The candidate's page shows a status beside the name ("Email bounced", "Marked as spam") and a notice that says what it
  means and that it says nothing else about the candidate. People who may email candidates get **Allow email again**,
  and a reply from the address clears it too, unless the reply is automatic: an out-of-office is not the candidate
  writing. `CandidateEmailStatusChanged` refreshes open dashboards.
- It is a delivery fact only. It never feeds a verification level, a score or a rejection (ADR-0004).

## Sending domain

Out of the box, mail comes from ScreenHire's address (`ScreenHire:Email:From`). An Owner or Admin can add the company's own
domain under Settings, Organization, Email domain (`/v1/organization/sending-domain`, `EditSettings`; every member reads it). The page is
hidden, with a line saying so, when this deployment has no provider. The field takes what a person copies (`https://www.example.com/careers`, `hiring@example.com`): the dashboard cuts it to the domain (`tidyDomain`) before it asks, and the API stays strict about what it stores.

- **Records.** The provider returns them (`ISendingDomainProvider`; SendGrid's domain authentication with automated
  security): two DKIM CNAMEs and a return-path CNAME that carries SPF on its own subdomain, so the company never adds a
  second SPF record at the apex. One more, an MX on `reply.<domain>`, brings replies to ScreenHire. It is on a subdomain
  because an MX on the domain itself would take all of the company's mail.
- **Checking.** Check records looks each one up (`IDnsRecordChecker`, DnsClient, no cache) and shows Found, Not found or
  Different value. When all are in place ScreenHire asks the provider to validate. Only the provider's yes makes the
  domain Verified, and only then does mail come from `hiring@<domain>` with the organization's name and its replies come to
  `reply.<domain>`. Until then mail keeps ScreenHire's address, so nothing goes out unauthenticated in the company's name.
  Adding a different domain replaces the first; removing it goes back to ScreenHire's address.
- **Rules.** One domain per organization, and one organization per domain. Mail providers' domains and ScreenHire's own
  (`ScreenHire:Domains:*`, ADR-0003) are refused. Nothing names a host in code.
- **Locally**, the sandbox provider makes up records and validates once a dev route (`/v1/dev/sending-domains/publish`)
  publishes the domain, so the whole flow runs with no DNS. `/v1/dev/sending-domains/unpublish` takes the records out again, so
  a verified domain can be seen failing a check.

- **Checked daily.** Once a day the worker looks every domain up again, a Pending one included, so a domain waiting for DNS becomes
  Verified by itself. A verified domain that fails is looked at once more an hour later before anything changes; if it still fails
  it falls back to Pending and the Owners get an alert in the notification bell and one email. A Check records that finds a
  verified domain failing tells them the same way. The daily check does not run for
  the sandbox, whose DNS lives in the API process ([notifications](notifications.md), "The daily email domain check").

Adding, verifying, losing verification and removing a domain are recorded in the page's History (`ChangeSubjectKind.SendingDomain`,
one history per organization, so a replacement reads as the old domain removed and the new one added); a check that only
refreshes the records' states records nothing ([change history](change-history.md)).

## Not built

- An inbox across candidates. Replies are read on the candidate's Messages tab.
- Attachments on outgoing mail, scheduled sends and per-person signatures.
- A Slack direct message for a reply (the card and the email are the notifications).
- A check of a reply's sender. Inbound Parse posts DKIM and SPF results that nothing reads yet, so whoever holds a reply
  address can write any From, and Messages shows it as written.
