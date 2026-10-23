# Research

Research written before a decision, as a snapshot of 2026-10-18. Each ADR names the research behind it.

* [Competitive Teardown](competitors.md) - ATS competitors' prices, AI features and fraud defences as of 2026-10-18, the AI and fraudulent applicant threat landscape, the gaps in the market, and ScreenHire's positioning against GoHire.
* [Svelte 5 UI Libraries: Evaluation](component-libraries.md) - Whether the Svelte 5 ecosystem covers the dashboard's needs, shadcn-svelte against the other component libraries with sizes and ARIA gaps, the recommendation behind ADR-0009, risks, and an ecosystem map with versions and downloads.
* [Recruiter Dashboard: Svelte 5 vs Angular 22](dashboard-framework.md) - The scored comparison behind ADR-0006: Svelte 5 with SvelteKit against Angular 22 on speed, measured fit with the Astro candidate pages, component libraries and risks.
* [.NET Building Blocks: Licence Audit & Recommendations](dotnet-building-blocks.md) - The licence audit of .NET libraries (MediatR, AutoMapper, MassTransit, Finbuckle, Duende, Polly and more) with commercial thresholds and verdicts, the recommended stack behind ADR-0008, and its risks.
* [Frontend Framework Benchmarks & Stack Options](frontend.md) - Benchmarks and stack options for the two frontends, the SEO-critical candidate surface and the recruiter dashboard: Astro, Blazor, Angular and Svelte compared on size and speed, behind ADR-0003.
* [Bootstrap Hosting (< $250/mo) with a Path to SOC 2](hosting-bootstrap.md) - Bootstrap hosting configurations under $250 a month compared, startup credits, Cloudflare Free in front, transactional email providers, and the path to SOC 2 Type II, behind ADR-0007.
* [Hosting: AWS vs DigitalOcean vs Hetzner](hosting.md) - AWS, DigitalOcean and Hetzner compared for a small EU production setup: the 2026 price changes, managed platforms, GDPR regions, compliance attestations and hybrid options.
* [Job-Board Multiposting: Integration Landscape](job-boards.md) - Which job boards can be integrated and how: Google for Jobs as the only self-serve channel, partner gating at Indeed and LinkedIn, EU and UK boards on the customer's credentials, unified APIs, traffic rankings and a phased plan.
* [Legal & Regulatory Landscape: Applicant Fraud / AI-Applicant Detection](legal.md) - Not legal advice: the EU AI Act, GDPR, NYC Local Law 144 and the US state rules as they bear on detecting fraudulent and AI applicants, a per-signal risk matrix, the product principles that follow, and open uncertainties.
