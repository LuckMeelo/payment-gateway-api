# ---- Build stage --------------------------------------------------------
# Use the full SDK image to restore, build and publish.
# Copying the project files first lets Docker cache the restore layer —
# a code-only change won't re-download packages.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/PaymentGateway.Api/PaymentGateway.Api.csproj src/PaymentGateway.Api/
RUN dotnet restore src/PaymentGateway.Api/PaymentGateway.Api.csproj

COPY src/ src/
RUN dotnet publish src/PaymentGateway.Api/PaymentGateway.Api.csproj \
    -c Release -o /app/publish --no-restore

# ---- Runtime stage ------------------------------------------------------
# The aspnet runtime image is much smaller than the SDK image (~200 MB vs ~800 MB).
# .NET 8+ images already set ASPNETCORE_HTTP_PORTS=8080, so Kestrel listens on
# HTTP 8080 without any extra configuration.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# $APP_UID is pre-set in the official .NET runtime images to a non-root UID.
# Running as non-root limits the blast radius of a container escape.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentGateway.Api.dll"]
