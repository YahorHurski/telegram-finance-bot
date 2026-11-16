# Build on the SDK, run on the smaller aspnet runtime (same multi-arch tag for amd64 and arm64)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, from project files only: the layer is cached until a package changes
COPY global.json Directory.Packages.props ./
COPY src/FinanceBot/FinanceBot.csproj src/FinanceBot/
RUN dotnet restore src/FinanceBot/FinanceBot.csproj
COPY src/ src/
RUN dotnet publish src/FinanceBot/FinanceBot.csproj -c Release -o /app --no-restore

# Full Ubuntu noble, not chiseled: has a shell for debugging and fewer surprises with SkiaSharp natives (Phase 6)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Non-root user built into the .NET images; Kestrel listens on 8080 (ASPNETCORE_HTTP_PORTS)
USER $APP_UID
ENTRYPOINT ["dotnet", "FinanceBot.dll"]
