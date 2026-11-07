# Log

## 2026-11-07

**Organizations have a free trial and plans, and a plan can stop a new job.** Every new organization starts a 14-day trial on the
Growth plan (`Trial:Days`, `Trial:Plan`); an older one starts it the first time billing is asked about it. An Owner chooses a
plan and pays through Stripe Checkout, and manages the card, switches plan or cancels in Stripe's portal, from Settings, Billing.
A plan limits how many jobs are open at once, and the only thing it ever refuses is opening a draft job (a 402 with a
plan-limit or subscription-ended reason): open jobs stay open and everything stays readable. The plans and their limits live in
`packages/plans/plans.json`, which the pricing page reads too; the shipped file sets no prices or limits yet, so nothing is
limited until the owner sets them. Stripe is called over plain HTTP with a pinned API version, a signed webhook applies each
event once, and a local sandbox stands in for Stripe in `pnpm dev` and the tests. With no Stripe keys and no sandbox, billing is
hidden and limits nothing. ([billing](architecture/billing.md), ADR-0029)

**Candidates have a page of their own.** A candidate asks for a link on the careers site with the address they applied with
and gets one only if we have an application from it; the page says the same either way. The link opens each application with
a four-step status (Applied, In review, Interview, Decision, then Hired, Not moving forward or Withdrawn) that never shows an
employer's own stage names, a saved application to finish, a follow-up questionnaire or an interview to book, and the
messages with a reply box. A reply typed there and a reply by email are one thing, in Messages and Activity, with attachments
scanned. Every email to a candidate now ends with a link to the page, and the interview emails carry a Reply-To, which they
did not before. The notice version is `2026-11-07.1`. ([candidate portal](architecture/candidate-portal.md), ADR-0027)

**An AI assistant that signs in only with OAuth can now connect to the MCP server.** A claude.ai custom connector needs only the
server URL: it finds the authorization server from the 401, registers itself, and sends an Owner or Admin to a consent page in
the dashboard, where they choose what it may do (Read always; Write and Verification read only if they hold them, both opt-in).
The approval is a connection with an API key's scopes, rate limit and audit trail; its tokens work on `/v1/mcp` only, and
Settings, MCP server lists each connection and disconnects it at once. The authorization server is written here rather than
with OpenIddict because OpenIddict's token tables would sit outside row-level security (ADR-0028, which closes ADR-0011
decision 6; [OAuth sign-in](architecture/oauth-server.md)). An emailed sign-in link now keeps the page it was asked from.

**A webhook endpoint has a change history.** An endpoint's signing secret was never stored (it is derived from the platform key,
the endpoint's id and a version), but its version and the time of its last rotation sat on the endpoint's row, which kept the row
off the history's allow-list. They now live in `webhook_endpoint_secrets`, one row per endpoint, so the endpoint's page shows
Last updated and History: its address by host (an address often carries a token), its events, its switch and each rotation.
The migration copies every version, so no endpoint's signatures change.

**Email template and scorecard edits toast.** A teammate's edit of an email template or a scorecard shows as a toast to the member
who changed it last, as a published hiring template version does. `TemplateChanged` now carries that member. A hiring template's
other edits are drafts and stay quiet.

**API keys can read reports over HTTP.** `GET /v1/reports/hiring` and the funnel list accept an organization API key with the Read
scope, as the MCP report tool already did. A key sees every job and only its own organization's numbers.

## 2026-11-05

**Design rule S9: design for how many there could be.** Tabs and toggles are for sets that cannot grow. Anything that can
grow is a list, table or grid that takes more with no redesign.

**The owner's review of the fourth build.** The pipeline tracker is a bar of stages (ADR-0025). Reports opens with Open jobs
and sets the weekly chart beside the funnel on a wide page. A table row keeps one height (L1). Record retention is a plain
choice, and says deletion happens at the next nightly run, which is when it always happened. Sending domain is now Email
domain, with its steps before anyone starts. The apply notice and privacy page say that applications are checked and what
is collected, not how a check works. Every editor now uses the Edit / Preview switch at every width (ADR-0026). The dashboard's
Save view button stays in the filter row, disabled with its reason until there is something to save, and every native
dropdown has one arrow with room around it. The workflow graph hides Svelte Flow's attribution while nothing is sold.

**An edit to an open job can now reach the boards.** Saving changed the careers page and left every board on the version
it had, with no way to send the new one from the dashboard: the only publish button was on drafts. The job page now says
so in a notice with Update boards, which publishes with no boards named. Customize still changes one board.

**The Azure definition and a deploy runbook exist, and nothing is deployed.** `apps/apphost/AzurePublish.cs` is the publish model;
`dotnet aspire publish` writes it to `infra/`, and a CI job fails when the two differ. [Deploy ScreenHire to Azure](operations/deploy.md)
has the order, the commands and the settings. A new `apps/migrator` creates the schema as the database owner, so the API and worker
hold only `screenhire_app`. A host outside Development now stops at start naming each required setting it lacks, and the health
endpoints answer in every environment. The one-day storage lifecycle rule that `apply-flow.md` asked for would have deleted
submitted resumes, so a daily worker sweep, `PurgeExpiredApplicantUploads`, removes the files of applications that were never
sent instead, which makes the privacy page's "deleted after a day" true.

**A job's Postings tab is now Job postings, a small table.** One row per board: Status, Board, Posted, Last updated and a
row menu. The menu has View posting and Copy link where we know the live address, which today is only the careers page,
then Edit details, Retry, Post, Post again or Take down. A posting now carries `viewUrl`. The tab's count is a plain
number, because 7/8 read as seven of eight boards live when most boards never confirm (E2).

**The hand cursor is one rule.** A browser gives a link the hand and a button the arrow, so anything that can be pressed
gets the hand from the base layer of `packages/ui/src/theme.css`, on the dashboard, careers and marketing alike (A10). No
component sets a cursor, and `tokens-only.test.ts` refuses one that does.

## 2026-11-02

**Interview scheduling runs on the Google Calendar and Microsoft Graph APIs** ([ADR-0024](adr/0024-interview-scheduling-on-calendar-apis.md)).
The owner's decision recorded on 2026-10-31 is built. Members connect their own calendar from the account menu, a
candidate books from an emailed link with no account, and each interviewer gets the event in their own calendar. The
workflow engine now accepts the `ScheduleInterview` action and the `InterviewScheduled` trigger, never from a
verification signal or a passed time limit. [Interview scheduling](architecture/interview-scheduling.md) describes it;
the roadmap's item 4 moved to done, and [local demo](operations/local-demo.md) shows how to try the sandbox calendar.
**A candidate's header is the name, a pipeline tracker and three mini cards.** The meta line is gone. The first line is the
name and one status badge (Verification suggested while they are in the pipeline, Saved, Rejected, Withdrawn or Hired once
they left), with Email and Move to stage on the right, or Message and Invite to another job for a saved candidate. A
tracker of dots replaces the stage pill, and Contact, Application and Also applied to (or Rating) are mini cards. The
tracker's dots are the one exception to square steps. See ADR-0023 and design system sections 7, 11 and 21.

**The talent pool is now Saved candidates, with no expiry.** The owner renamed it: Keep in mind is Save candidate, the
status is Saved, and the page is Saved candidates at `/saved` (G then S; `/talent-pool` redirects). A saved candidate has
no end date. They stay until the team removes them or they use the stop link in an invitation, so the permission period,
its setting (`/v1/organization/talent-pool-settings`), the daily expiry sweep and the `future_roles_consent_months`
column are gone. A permission that had already run out stays ended: the migration withdraws it and takes anyone still saved
on it off the list. The apply form's "Keep my details for future roles" box stays, with new wording and a new notice
version, `2026-11-02.1`; application reads carry `allowsFutureRoles` in place of `futureRolesUntil`. The list gained
Rating (with the reviewer gate), Last contacted (the newest email to the candidate, or reply from them, over the applications the
reader sees), a Message button, and a note the team can edit, with History. Code and wire names keep
the old words (`KeptInMind`, `/v1/talent-pool`). Because nothing expires a saved candidate, a legal review of keeping
details without an end date is an owner action ([status](status.md#owner-actions)), and record retention matters more
([saved candidates](architecture/talent-pool.md)). Earlier entries below keep their old words.

**Hardening from the last workflow's follow-ups.** Saving a draft application needs the bot check to pass; the
verification ruleset moved to `2026-11-02.1` for the automation analyzer's version 2 (an analyzer that changes what its
signals mean now moves it); `TemplateChanged` refreshes open dashboards after any template edit; brand images nobody
saved are removed after a day; a job has its own embed snippet; and the database got trigram indexes, a foreign key from
a review action to its signal and a backfill of `change_records.created`. See [realtime](architecture/realtime.md),
[branding](architecture/branding.md), [careers widget](architecture/careers-widget.md) and
[testing](process/testing.md) for the details.

**Verification read, an API key scope only an Owner can give** ([ADR-0021](adr/0021-verification-read-scope-for-api-keys.md)).
This is the owner's decision that the MCP server sees verification results. An Owner can add a third scope,
Verification read, when creating a key (a new Owner-only permission, so no Admin or custom role can). A key with it
reads an application's verification and lists applications by level, over HTTP and as two MCP tools; every read adds
a "Read through the API key" line to the application's review log; a key without it sees exactly what it did. A level
is still never a reason to reject: the tools and the server instructions say so, and nothing here dismisses, notes or
moves anyone. The MCP server also gains read tools for the hiring report and the saved candidates (today's talent
pool; the rename and rating work for saved candidates is its own task).

**One candidate across jobs** ([candidates](architecture/candidates.md)). A candidate's page at `/candidates/{id}` lists
every application they made and their history across them; each application's header links to it as Also applied to.
A later application that shares a detail with an earlier one re-checks the earlier one's duplicates, and the review log
says why ([verification engine](architecture/verification-engine.md) section 10.2). An Owner or Admin can erase a
candidate with their name typed to confirm, leaving a line in the team log with no personal data. Erasing also deletes
their replies' attachments and the history of their saved notes, which sit outside the candidate's cascade.

## 2026-10-31

**The test gate runs on GitHub Actions** ([ADR-0020](adr/0020-test-gate-on-github-actions.md)). Agents run
`pnpm verify:fast` and the tests their branch added or changed; the whole suite runs in CI behind the `Gate` check.
[Board pipeline](process/board-pipeline.md), [testing](process/testing.md) and [dev tooling](process/dev-tooling.md)
describe the new Testing and Merge columns: one push, one CI run, and a merge that relies on that run.

**Owner review of the second workflow's build.** Settings has three named groups (Organization, Hiring, Integrations);
Organization's page is General, and a new Careers site page holds the careers address, the apply form's questions per
page and the sites that may embed it. The Talent pool settings page is gone: everyone keeps 12 months until the owner's
retention decision lands. Every settings header is one row, so moving between them never shifts the menu (S8); Slack's
workspace facts moved into a panel. A job's Distribution tab is Postings, and its old address still opens it. The
board's Closed column counts a candidate kept in mind in a row of its own. On My tasks the candidate's name is body
weight. The owner's other decisions (saved candidates, one candidate across jobs, saved views, the candidate header,
reports, SendGrid through the Azure Marketplace, calendar scheduling on Google and Microsoft APIs, the MCP server seeing
verification results) go to the next workflow.

## 2026-10-31

**Integrity is called Verification everywhere** ([ADR-0019](adr/0019-rename-integrity-to-verification.md)). The
integrity engine's doc is now [verification engine](architecture/verification-engine.md), and the
`integrity-guardrails` agent is `verification-guardrails`. Code, API routes, the `verification.assessed` webhook event,
the `VerificationAssessed` realtime event, `ScreenHire:Verification:*` configuration and the `verification_*` tables
follow; a migration renames the tables and rewrites the names stored as data, and drops nothing. ScreenHire's Software
engineer template gains a version 5 whose automation and tag say verification. Accepted ADRs, research and earlier
entries here keep the word they used.

**Rich text for job descriptions and email templates** ([rich text](architecture/rich-text.md)). New job, Edit
details, the email template editor and the composer have a small editor with a toolbar (bold, italic, a heading and a
subheading, bulleted and numbered lists, links) in place of the plain-text hints. Both are stored as sanitized HTML
limited to those elements, which the server writes in one canonical form, so a save that changes nothing records
nothing. Email bodies became HTML with Liquid in their text: values are encoded, the result is sanitized again, and the
plain-text part prints lists and link addresses. In an email, each variable is a chip. A migration converted every
plain-text body. The design system gains a Rich text editor row, and its shortcuts join the `?` dialog; typed
formatting stops while single-key shortcuts are off.

## 2026-10-30

**Scorecards: fill in any one, ask a teammate for one, and tasks done where the work is.** On the candidate's
Scorecards tab, Fill in takes a reached stage's own scorecard (the current one first) or any library scorecard, which is
given at the stage the candidate is in now; each submission keeps the scorecard it came from and its name. Ask for one
gives a teammate who sees the job and fills in scorecards a new kind of task, Scorecard. A task is done where its work
is done (ADR-0017 decision 11): submitting the scorecard finishes the reviewer's request for it and their review (a
review for the whole team with anyone's), in the same transaction, and the API refuses to tick a review or a requested
scorecard by hand. My tasks links a requested scorecard to the Scorecards tab with the sheet open on it and every other
task to the candidate. A requested scorecard holds back the rating and the others' scorecards until it is given, as a
review does ([workflow engine](architecture/workflow-engine.md#8-the-engine-v1)).

**The talent pool** has its own design doc, [architecture/talent-pool.md](architecture/talent-pool.md): keeping a
candidate in mind, the permission for future roles from the apply form, the pool, invitations, the link to stop being
kept and the daily expiry. The apply flow, email, messaging, workflow engine, sign-in and tenancy, design system,
roadmap and status docs point to it.

**Verification reasons can be marked explained.** On the candidate's Verification tab each reason takes Mark explained,
with a short reason and an optional note, and its author can undo it; both go in the review log. Once every reason that
counts is explained, Dismiss reads Dismiss all and the dismiss dialog lists the notes. Explaining changes no level and
nothing reads it but the panel; only a dismissal sets an assessment aside. The AI assistance summary no longer repeats
the job's policy, which the panel shows as a pill. That is wording, not a rule, so the ruleset version stays and
assessments already stored keep the old sentence until they are reassessed.

**The owner reviewed the v0.3 build** ([ADR-0017](adr/0017-design-system-v0-3-owner-review.md)) and the dashboard
changed with it. The candidate page has no details rail: who they are and where they stand is one meta line in the
header, with the authenticity level as their status beside the name, and Overview is a grid of panels. The feature
people see is Verification, not Integrity: the tab, the Settings page (`/settings/verification`, the old addresses
redirect), the filter, the workflow trigger and facts, the role permission and the guardrail's message; code, API
routes, events and tables keep "integrity" for now. Each assessment leads with its next step in a callout, groups its
reasons under the check that found them and puts when it was assessed last. My tasks links each task to its candidate,
where it is ticked off. Candidates filter by several jobs and stages at once. Settings gains an MCP server page with the
values any client needs, and every settings header is one row, so nothing jumps between them. The Live indicator moved
to the top bar. Scorecard templates, email templates and questionnaires show the editor and its preview side by side
where both fit. The job form picks a currency by name and a closing date in the person's own time zone.

New rules in `design/design-system.md`: S8 (moving between sibling pages never shifts the frame), A9 (expandable rows
have a chevron), F6 (a field's actions share its message's row), W7 (say it once per screen), W8 (never ask people to
type a code), W9 (a label only where the value cannot explain itself) and W10 (what to do first, audit details last); A8
now lets an editor and its preview share the screen, and L1 says an empty cell is blank. The roadmap moves the MCP
server to Done and adds rich text, Connect to Slack and teammates' changes as toasts.

## 2026-10-25

**The dashboard is built to v0.3.** The last screens landed (Tasks with Mine, For the team and Everyone's, each ticked
done with Undo for 10 seconds, which reopens the task on the server; saved views with short names), and the
`design-review` agent ran over the whole dashboard, code and screenshots at 390, 1024, 1440 and 1920px. Its findings
were fixed: workflow stages and automation actions reorder with a grip (A7), the candidate tables fill the window
(S6), the webhook delivery log sorts (L2), and the Organization, Branding and Integrity settings pages have Last
updated and History (T6). Four findings were rules the rest of the system contradicts; ADR-0016 settles them in the
design system: a page's action that opens another page is a button rendered as a link (A5), a webhook endpoint has no
history because its row holds a secret (T6), a form keeps its actions at its end on a phone, and a table wider than
its panel beside the full sidebar scrolls inside it (section 8). Section 21 of the design system, the components the
build still added, is gone: Task checkbox and Choice card joined section 11, and the screens section is now 21. A
second review pass added Expandable row to section 11 (an email on Messages), put a webhook endpoint's events on the
details rail, and made the Settings menu list only the pages a role may use. The roadmap records the workflow as
done, and CLAUDE.md now says long, complex tasks go through Planning with automatic approval.

**Design system v0.3** ([ADR-0013](adr/0013-design-system-v0-3.md)), approved on the owner's "ScreenHire design v0.3
review" canvas. `design/design-system.md` keeps its section numbers and checklist ids: v0.2 checks were amended in
place and new ones added (S7, A6 to A8, W6, T6, E2, E3, L1, L2), so the `design-review` agent enforces v0.3 from its
next run. New section 21 lists the components the v0.3 build adds, outside section 11, which the style guide's test
reads; section 22 lists the screens it changes. CLAUDE.md, `svelte-dashboard.md`, the `add-dashboard-page` and
`task-review` skills and the `design-review` agent now say a status is an icon, its colour and its label.

## 2026-10-23

**Restructure** of `docs/` into an Open Knowledge Format v0.2 bundle (ADR-0012). Every doc is now a concept with
frontmatter: 37 concepts under 7 indexes, rooted at `index.md`, which alone carries `okf_version: "0.2"`. Each
concept's H1 is its title, so nine H1s lost an em-dash. Each concept's `description` is the line its folder index
shows, and `sources:` names the code it describes, so `node scripts/docs-bundle.mjs sources <file>` finds the docs a
change touches. `DocsBundleTests` checks all of it in `pnpm verify`.

No file was split or moved, so every `docs/...` path cited from code, skills, rules and migrations still holds. The
largest doc is about 690 lines, and the architecture docs and the design system are cited by section number.
`README.md` became the start-here guide by audience. Its MVP status (built, faked locally, owner actions, known gaps)
moved to `status.md`, and its decision log became `adr/index.md`. The roadmap and the status were brought up to date
with the "Platform: team, API access and webhooks" and "Team, collaboration and communication" workflows.
