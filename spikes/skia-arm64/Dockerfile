# Decisive run is native on the arm64 VM: docker build -t skia-spike spikes/skia-arm64 && docker run --rm skia-spike
# Under QEMU on an x64 host (--platform linux/arm64) the .NET runtime itself crashes, so that result proves nothing.
# Build on the host architecture (fast, no emulation); the portable output carries runtimes/linux-*/native for every arch
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SkiaSpike.csproj ./
RUN dotnet restore
COPY Program.cs ./
RUN dotnet publish -c Release -o /out --no-restore

# Same base image as the app, so the native libraries are tested where they will run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "SkiaSpike.dll"]
