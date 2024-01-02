FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT/Hardened.IntegrationTests.WebApp.SUT.csproj", "IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT/"]
RUN dotnet restore "IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT/Hardened.IntegrationTests.WebApp.SUT.csproj"
COPY . .
WORKDIR "/src/IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT"
RUN dotnet build "Hardened.IntegrationTests.WebApp.SUT.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Hardened.IntegrationTests.WebApp.SUT.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hardened.IntegrationTests.WebApp.SUT.dll"]
