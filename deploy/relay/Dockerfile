FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/XIVChat.Relay.Protocol/ src/XIVChat.Relay.Protocol/
COPY src/XIVChat.Relay/ src/XIVChat.Relay/
RUN dotnet publish src/XIVChat.Relay/XIVChat.Relay.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir /data && chown app:app /data
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 XIVCHAT_RELAY_DATA=/data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "XIVChat.Relay.dll"]
