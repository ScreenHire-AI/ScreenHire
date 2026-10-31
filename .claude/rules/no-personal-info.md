# Rule: no personal or machine-specific info in committed files

Hard-coded usernames, emails or machine paths leak personal data and break on the next machine. In an applicant
tracking system there is a second category: real people's application data. Neither belongs in the repository.

## The rule

Never commit, in code, tests, scripts, docs, commit messages or PR bodies:

- Personal usernames, emails or home-directory paths (a real `C:\Users\<name>` or `/home/<name>`). Use placeholders
  such as `C:\Users\dev`, including in examples like this one.
- Machine-specific absolute paths. Derive paths at runtime (`AppContext.BaseDirectory`, `import.meta.url`,
  configuration, environment variables) or use repo-relative ones.
- Real candidate or recruiter data: names, emails, phone numbers, resumes, answers, IP addresses. Seed and test data
  are fictional (the Brightwater Studio seed in `DevelopmentDataSeeder.cs` is the pattern, and ADR-0008 approves
  Bogus for generated data). Use `example.com` / `example.org` addresses.
- Client or employer names, or anything that says where a bug or request came from. Describe the mechanism instead.
- Secrets of any kind. Local credentials come from Aspire parameters or user secrets, never from committed files.

## Enforcement

- **Review.** `/task-review`'s security dimension flags personal data, secrets and real applicant data.
- **Test.** `PersonalInfoTests` (`tests/ScreenHire.UnitTests/Tooling/`, in `pnpm verify` and CI) scans every file
  git would commit and fails on:
  - a home-directory path whose user is not `dev`;
  - an email address outside example.com, example.org, example.net, `.example`, `.test`, `.invalid` and
    `.localhost`, other than GitHub's and Anthropic's noreply addresses;
  - a token, private key, JWT, Azure storage key (other than Azurite's published one), or a password in a
    connection string or a URL whose host is not reserved;
  - the committer's own `git config` name or address, matched whole and read at run time.
  Paths are exempt only in `PersonalInfoScan`, each with its reason: third-party data and lockfiles.

## Scope

All committed files. Local, git-ignored state (`.kangentic/`, `.claude/settings.local.json`, `.env*`) is exempt.
