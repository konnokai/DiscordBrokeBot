FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/DiscordBrokeBot/DiscordBrokeBot.csproj src/DiscordBrokeBot/
RUN dotnet restore src/DiscordBrokeBot/DiscordBrokeBot.csproj

COPY src/DiscordBrokeBot/ src/DiscordBrokeBot/
RUN dotnet publish src/DiscordBrokeBot/DiscordBrokeBot.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME ["/keys"]
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DiscordBrokeBot.dll"]
