# The Cloud Run deployment artifact. `gcloud run deploy --source .` builds it with Cloud Build, and
# `docker build -t hardened1 .` builds it here. Two stages, so the image that runs carries the
# runtime and the published output and not the SDK.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Hardened1.Host/Hardened1.Host.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Cloud Run sets PORT and sends to it; Program.cs reads it, 5080 when it is unset.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hardened1.Host.dll"]
