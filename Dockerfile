# One Dockerfile, two images (D-11): `--target migrator` (EF migration bundle) and `--target app` (default, last stage).
# Build on the SDK, run on the smaller aspnet runtime (same multi-arch tag for amd64 and arm64)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Packages.props ./
COPY .config/ .config/
# All of src/ before restore: once the code is split into layers the csproj files reference each other,
# and CI has no layer cache anyway
COPY src/ src/
RUN dotnet restore src/FinanceBot/FinanceBot.csproj
RUN dotnet publish src/FinanceBot/FinanceBot.csproj -c Release -o /app --no-restore

FROM build AS bundle
ARG TARGETARCH
RUN dotnet tool restore
# CI builds natively on the arm64 runner → linux-arm64 (D-11); x64 only for local checks on amd64 Docker
RUN dotnet ef migrations bundle --project src/FinanceBot --startup-project src/FinanceBot \
    --configuration Release --self-contained -r linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) -o /efbundle

# Same base as app: the VM downloads almost nothing extra
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
COPY --from=bundle /efbundle /efbundle
USER $APP_UID
ENTRYPOINT ["/efbundle"]

# Last stage = default target: `docker build .` still produces the app.
# Full Ubuntu noble, not chiseled: has a shell for debugging and fewer surprises with SkiaSharp natives (Phase 6)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /app .
# Non-root user built into the .NET images; Kestrel listens on 8080 (ASPNETCORE_HTTP_PORTS)
USER $APP_UID
ENTRYPOINT ["dotnet", "FinanceBot.dll"]
