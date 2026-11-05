# syntax=docker/dockerfile:1
# The careers site (Astro SSR, Node). It is a pnpm workspace app, so the build context is the repository root:
#   docker build -f apps/careers/Dockerfile .
# The AppHost's publish model builds it this way (apps/apphost/AzurePublish.cs). No .NET is needed: the API client in
# packages/api-client is generated and committed.

FROM node:24-slim AS build
ENV CI=true
# The version in the root package.json's packageManager field.
RUN corepack enable
WORKDIR /repo
# Manifests first, so the install layer is reused until a dependency changes. Every workspace project's manifest is
# here because the lockfile lists them all.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/careers/package.json apps/careers/
COPY apps/dashboard/package.json apps/dashboard/
COPY apps/marketing/package.json apps/marketing/
COPY packages/api-client/package.json packages/api-client/
COPY packages/realtime/package.json packages/realtime/
COPY packages/telemetry/package.json packages/telemetry/
COPY packages/ui/package.json packages/ui/
RUN pnpm install --frozen-lockfile --filter "@screenhire/careers..."
COPY apps/careers apps/careers
COPY packages/api-client packages/api-client
COPY packages/telemetry packages/telemetry
COPY packages/ui packages/ui
# Astro reads the hosts it trusts forwarded headers from when it builds (see astro.config.mjs).
ARG CAREERS_ALLOWED_DOMAINS=""
ENV CAREERS_ALLOWED_DOMAINS=$CAREERS_ALLOWED_DOMAINS
RUN pnpm --filter @screenhire/careers build
# The production dependencies only, with the workspace packages copied in.
RUN pnpm --filter @screenhire/careers deploy --legacy --prod /out

FROM node:24-slim
ENV NODE_ENV=production HOST=0.0.0.0 PORT=8080
WORKDIR /app
COPY --from=build /out/node_modules ./node_modules
COPY --from=build /repo/apps/careers/dist ./dist
COPY --from=build /repo/apps/careers/package.json ./package.json
USER node
EXPOSE 8080
CMD ["node", "dist/server/entry.mjs"]
