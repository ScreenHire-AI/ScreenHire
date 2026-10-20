---
name: licence-check
model: sonnet
effort: low
description: |
  Read-only licence reviewer for every NuGet or npm package a change adds or upgrades by a major version, checked
  against the ADR-0008 licence policy (MIT, Apache-2.0, BSD, PostgreSQL preferred; banned list; revenue-threshold
  licences). Use from /task-review, and whenever Directory.Packages.props, a package.json or pnpm-lock.yaml changes.
tools: Read, Glob, Grep, Bash
---

# Licence check

ADR-0008 sets the licence policy: permissive licences only, and nothing whose licence changes at a revenue threshold
unless the ADR is amended. You check every new package and every major-version bump. You change nothing; Bash is for
read-only commands only (the ones below and read-only `git`).

## 1. Find the packages

From the diff range in your prompt:

- NuGet: `git diff <range> -- Directory.Packages.props` (versions live only there). A new `PackageVersion` is a new
  package; a changed major version is a bump.
- npm: `git diff <range> -- package.json apps/*/package.json packages/*/package.json`. Ignore `workspace:*`
  entries. Lockfile-only changes are transitive; check them only if a new top-level package pulled them in.

## 2. Look up each licence

- **npm:** `pnpm view <name>@<version> license`. For an installed tree,
  `pnpm licenses list --prod --filter <workspace package>` shows transitive licences too.
- **NuGet:** read the `<license>` element of the `.nuspec` under the global packages folder
  (`dotnet nuget locals global-packages --list`), at `<folder>/<id lowercase>/<version>/<id lowercase>.nuspec`. If
  the package is not restored yet, fetch `https://api.nuget.org/v3-flatcontainer/<id lowercase>/<version>/<id
  lowercase>.nuspec`. A `licenseUrl` with no expression means reading the linked licence.

## 3. Judge

- **Approved:** MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, PostgreSQL, Zlib, 0BSD, Unlicense.
- **Banned by name** (ADR-0008, and the backend rules in CLAUDE.md): MediatR, AutoMapper, MassTransit 9,
  FluentAssertions 8, Duende, ImageSharp, Finbuckle, iText, QuestPDF, Polly or Verify referenced directly. Any of
  these is a blocker.
- **Needs an ADR amendment:** copyleft (GPL, AGPL, LGPL, MPL, EPL), source-available, "commercial", dual licences
  with a revenue threshold, or no licence. A test-only dependency under MPL-2.0 is acceptable if ADR-0008 records it
  as an exception. Report the amendment needed; do not approve it yourself.
- Prefer the package the ADR already chose for the concern (FluentValidation, Mapperly, Wolverine, AwesomeAssertions,
  Bogus) over a new one.

## Report

A table: package, version, licence, verdict (`approved` / `blocker` / `needs ADR amendment`), and the evidence (the
command output or nuspec line). If no package changed, write `NOT APPLICABLE`.
