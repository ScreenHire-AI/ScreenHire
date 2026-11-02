---
type: Decision
title: "ADR-0021: Email goes through SendGrid bought on the Azure Marketplace, and candidates can reply"
description: "SendGrid, bought through the Azure Marketplace, sends candidate email, receives replies with Inbound Parse and reports bounces; a reply address that names the send and is signed; the company's own sending domain verified by the provider; ACS Email stays a possible sender behind IEmailSender; amends the Email row of ADR-0007."
tags: [adr, email, sendgrid, azure]
---
# ADR-0021: Email goes through SendGrid bought on the Azure Marketplace, and candidates can reply

- **Status:** Accepted (2026-11-02)
- **Deciders:** Owner, through task #96 on the board
- **Amends:** the Email row of ADR-0007 (Azure Communication Services Email, with SES as the fallback)
- **Detail:** [`docs/architecture/email.md`](../architecture/email.md)

## Context

ADR-0007 named Azure Communication Services (ACS) Email for sending. The owner has since chosen SendGrid, bought through
the Azure Marketplace, so email is billed on the Azure subscription with the rest of the hosting.

The reason to move is replies. Email to candidates went out from a no-reply address, and a candidate's answer reached
nobody. ACS Email only sends. Replies need inbound mail, which SendGrid's Inbound Parse provides, and a way to learn that
an address bounced, which its event webhook provides. Both come with the provider that sends.

## Decision

1. **SendGrid sends candidate email.** `SendGridEmailSender` implements `IEmailSender` against SendGrid's v3 Mail Send
   API with plain `HttpClient`: no SendGrid package. It is on when `ScreenHire:Email:SendGrid:ApiKey` is set, from the
   `sendgrid-api-key` secret parameter. Mailpit stays the local and test sender, and with neither configured email is
   hidden, as before. Click and open tracking are off.
2. **ACS Email stays possible.** The seam is `IEmailSender` and `ISendingDomainProvider`. A second adapter for ACS Email
   (or SES) is a new class behind them, with no change to a caller. Nothing in this decision builds it.
3. **Every email about an application carries a reply address**, `reply+<token>@<reply host>`. The token holds the
   organization, the send (an `application_emails` id) and a short HMAC under the `email-reply-key` secret, in lowercase
   base32 because mail systems may change the case of a local part. It is exactly 64 characters with the prefix, the
   RFC 5321 limit, and a test holds that. The two 128-bit ids are the real capability; the MAC only rejects damaged
   addresses cheaply. The token names the send, not the application, so the person who sent the original is known. The
   sign-in link, invitation and saved-application emails are not correspondence and carry none.
4. **The reply host is a subdomain, never the company's own domain.** `reply+<token>@reply.example.com` reads the brief's
   "at the organization's sending domain" safely: an MX record on `example.com` itself would send all of the company's
   normal mail to SendGrid. Without a sending domain of its own, replies arrive on `ScreenHire:Email:ReplyDomain`.
5. **Inbound Parse posts to a webhook that checks two things.** Inbound Parse cannot sign its posts. The webhook's URL
   carries HTTP Basic credentials, which SendGrid sends in a header, so the secret never reaches a request log or trace,
   and the signed reply address must be valid for anything to change. A post that fails the second check changes nothing
   and still answers 200, so SendGrid stops retrying. The recipient is read from the envelope, not the To header, which a
   candidate may have edited. A reply is stored with its sanitized HTML, kept attachments (PDF, Word and PNG or JPEG
   under 10 MB, ten at most) behind the malware scan, an `EmailReceived` timeline entry, and a notification to whoever
   sent the original. An automatic answer (out of office) is stored and notifies nobody.
6. **Bounces and spam complaints come from SendGrid's signed event webhook** (ECDSA, with the public key in
   configuration). A hard bounce or a spam report flags the candidate's address, and ScreenHire sends it nothing more
   until a person says it works or the candidate writes from it. **A flag is a delivery fact. It never feeds a
   verification level, a score or a rejection** (ADR-0004).
7. **The company's own sending domain is verified by SendGrid.** An Owner or Admin adds a domain under Settings, Sending
   domain. The records are the ones SendGrid's domain authentication returns, with automated security: two DKIM CNAMEs
   and a return-path CNAME that carries SPF on its own subdomain, so the owner never adds a second SPF record at the
   apex (a domain may have only one). One MX record on `reply.<domain>` receives replies. ScreenHire looks each record up
   itself to say which is missing, but the domain is Verified, and mail switches to `hiring@<domain>`, only when the
   provider's own validation also passes. Until then mail keeps ScreenHire's address, so nothing goes out unauthenticated
   in the company's name.
8. **Local and test runs need no provider.** `ScreenHire:Email:Sandbox:Enabled` (Development only; a host refuses to start
   with it on elsewhere) adds a dev route that delivers a reply as SendGrid would, and a sandbox provider and DNS whose
   records "exist" once a dev route publishes the domain. No test needs SendGrid or the internet.

## Consequences

- Email costs move from ACS's per-message price to a SendGrid plan on the Azure bill. The cost line in ADR-0007 should be
  re-checked when the plan is chosen.
- Replies make Messages two-way. A reply can carry a file, so the careers form's malware scan now guards a second door.
- The inbound secret and the event webhook's public key are configuration the owner sets in SendGrid and Key Vault.
- Inbound Parse posts are unsigned. If SendGrid adds a signature, check it as well as the Basic credentials.
- A custom sending domain needs the owner to add DNS records at the company's host; ScreenHire cannot do it for them.
- Slack direct messages for replies, a scheduled re-check of pending domains and change history for the sending domain
  are not built.

## Alternatives considered

- **ACS Email.** Sends only, so replies would still need another inbound provider.
- **SES.** Cheaper per message, but outside the one Azure bill the owner wants.
- **An MX record on the company's domain.** Simpler to explain and wrong: it takes all of their mail.
- **A second SPF record on the apex.** A domain may have one; adding another breaks the company's existing SPF.
