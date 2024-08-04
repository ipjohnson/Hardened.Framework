FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/LambdaWebTest.Harness/LambdaWebTest.Harness.csproj", "src/LambdaWebTest.Harness/"]
RUN dotnet restore "src/LambdaWebTest.Harness/LambdaWebTest.Harness.csproj"
COPY . .
WORKDIR "/src/src/LambdaWebTest.Harness"
RUN dotnet build "LambdaWebTest.Harness.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LambdaWebTest.Harness.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LambdaWebTest.Harness.dll"]
