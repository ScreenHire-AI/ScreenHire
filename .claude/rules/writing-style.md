# Rule: writing style

Prose an agent writes here goes straight to people: `docs/`, `README.md`, PR titles and bodies, review comments,
commit messages, task descriptions and UI copy. Default model prose has recognizable tells, and it reads as
machine-written even when every fact in it is right. Em-dashes carry a second problem: they render as garbled
characters on Windows console code pages, and this project is built on Windows.

## The rule

Applies to every piece of prose you author: source comments, docs, commit messages, PR titles and bodies, review
comments, task descriptions, UI copy, and chat back to the user. Candidate-facing and recruiter-facing copy also
follows the voice section of `docs/design/design-system.md`.

### Punctuation and characters

- No em-dash (U+2014), en-dash (U+2013), `&mdash;`, or `--` as a separator. Use a period, a comma, or a single `-`.
  Do not reach for a parenthetical instead. If a thought needs separating, end the sentence.
- Straight quotes only. No curly quotes (U+2018, U+2019, U+201C, U+201D).
- A colon introduces a list or an example, not a mid-sentence connector.
- Do not bold every proper noun or acronym.
- Sentence case for headings. No decorative emoji in headings or bullets.
- No bold label and colon that restates its own line ("**Performance:** Performance improved"). A bold lead-in
  that names the thing and is followed by new detail is fine.

### Words

- Cut model vocabulary: additionally, crucial, delve, enduring, enhance, foster, garner, interplay, intricate,
  landscape (abstract), pivotal, showcase, tapestry, testament, underscore, vibrant.
- Cut puffery: groundbreaking, renowned, seamless, must-have, game-changing.
- Prefer the plain word. Use, not utilize or leverage. Help, not facilitate. If, not in the event that. To, not in
  order to. Because, not due to the fact that.
- Say "is" or "has", not "serves as", "stands as", "boasts", "features".
- Avoid abstract metaphor nouns (substrate, vector, locus, bedrock, paradigm, north star, flywheel, "API surface").
  Name the concrete thing.

### Sentences

- One idea per sentence. Active voice; name the actor.
- Say what it does: the mechanism, the flag, the number. A sentence that would read the same in another project's
  docs says nothing about this one.
- Use the natural number of items. Do not force groups of three.
- Pick one term for a thing and repeat it. No synonym cycling.
- Drop hedge stacks and "It is important to note that".
- Name a source or delete the claim.
- No chatbot filler ("I hope this helps", "Certainly", "Great question") and no generic closer.
- Match the length to the artifact. A PR body or review comment is a short summary, not the session's transcript.

### Voice

Have an opinion instead of listing pros and cons. Vary sentence length. Say the awkward part out loud when it is
true. First person is fine when you did the thing.

## Enforcement

- **Review.** `/task-review`'s conventions finder (the `review-finder` agent) checks prose in every changed file,
  which gives docs-only changes a reviewer.
- No mechanical test yet. A scan for U+2013, U+2014 and curly quotes across `.claude/`, `docs/process/` and new
  files is a candidate; until it exists, run a grep over your changed files before you commit.

## Scope

Prose you write or rewrite, in any file type. Content you did not write is exempt: quoted output, fixtures, and
characters that are data. Pre-existing prose is exempt too. Much of `docs/` predates this rule and uses em-dashes;
restyling it is its own job, not a finding against a change that touches the file.
