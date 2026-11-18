# Build context is the repository root:
#
#   docker build -t split .
#
# One image containing both halves of the app — that is the whole point of the project. The
# Vue bundle is built first and left where the API expects it, so the published output is a
# single process serving the client and the API from one origin, with no CORS and no second
# deployment to keep in step.

# ------------------------------------------------------------------ client ----

FROM node:20-alpine AS client
WORKDIR /src

# The directory layout is preserved deliberately. vite.config.ts writes its build to
# ../Split.Api/wwwroot, resolved relative to the config file, so Split.Client has to sit
# beside Split.Api exactly as it does in the repository or the output lands nowhere useful.
COPY src/Split.Client/package.json src/Split.Client/package-lock.json src/Split.Client/

# `npm ci` rather than `npm install`: it installs exactly the lockfile, so a transitive
# update cannot quietly change what ships.
RUN cd src/Split.Client && npm ci

COPY src/Split.Client/ src/Split.Client/
RUN cd src/Split.Client && npm run build

# ------------------------------------------------------------------- build ----

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files on their own layer, so restore only re-runs when a dependency actually
# changes rather than on every source edit.
COPY Split.slnx ./
COPY src/Split.Domain/Split.Domain.csproj src/Split.Domain/
COPY src/Split.Api/Split.Api.csproj       src/Split.Api/

RUN dotnet restore src/Split.Api/Split.Api.csproj

COPY src/Split.Domain/ src/Split.Domain/
COPY src/Split.Api/    src/Split.Api/

# The built client, dropped in before publish so it is picked up as content and ends up in
# the published output. Copied after the source, because the source copy above would
# otherwise overwrite it.
COPY --from=client /src/src/Split.Api/wwwroot/ src/Split.Api/wwwroot/

RUN dotnet publish src/Split.Api/Split.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# ----------------------------------------------------------------- runtime ----

# The aspnet image rather than the SDK: a fraction of the size, and it carries no compiler,
# so a container that is somehow compromised has much less to work with.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app ./

# Where the SQLite file lives. This path is expected to be a mounted persistent disk; a
# container filesystem is thrown away on every restart, so without one the database would
# vanish each time the service redeployed or woke from sleep.
ENV ConnectionStrings__Split="Data Source=/data/split.db"
RUN mkdir -p /data

# Deliberately left running as root, unlike the sibling CafeOps image.
#
# A mounted disk arrives owned by root, and a non-root process cannot write to it without an
# entrypoint that starts privileged purely to chown the mount. That is more moving parts than
# this is worth, and a half-working non-root setup that silently cannot write to its database
# is worse than an honest root container. Noted here rather than left to be discovered.

# A default for platforms that do not assign one. Where a platform does set PORT — Render,
# Fly, Cloud Run — Program.cs binds that instead.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Split.Api.dll"]
