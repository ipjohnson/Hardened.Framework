FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["IntegrationTests/Console/Hardened.IntegrationTests.Console.SUT/Hardened.IntegrationTests.Console.SUT.csproj", "IntegrationTests/Console/Hardened.IntegrationTests.Console.SUT/"]
RUN dotnet restore "IntegrationTests/Console/Hardened.IntegrationTests.Console.SUT/Hardened.IntegrationTests.Console.SUT.csproj"
COPY . .
WORKDIR "/src/IntegrationTests/Console/Hardened.IntegrationTests.Console.SUT"
RUN dotnet build "Hardened.IntegrationTests.Console.SUT.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Hardened.IntegrationTests.Console.SUT.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hardened.IntegrationTests.Console.SUT.dll"]
