FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY global.json ./
COPY backend/Directory.Build.props backend/Trade.slnx backend/
COPY backend/Trade.Api/Trade.Api.csproj backend/Trade.Api/packages.lock.json backend/Trade.Api/
COPY backend/Trade.Application/Trade.Application.csproj backend/Trade.Application/packages.lock.json backend/Trade.Application/
COPY backend/Trade.Domain/Trade.Domain.csproj backend/Trade.Domain/packages.lock.json backend/Trade.Domain/
COPY backend/Trade.Infrastructure/Trade.Infrastructure.csproj backend/Trade.Infrastructure/packages.lock.json backend/Trade.Infrastructure/
RUN --mount=type=secret,id=proxy_ca \
    if [ -s /run/secrets/proxy_ca ]; then \
        cp /run/secrets/proxy_ca /usr/local/share/ca-certificates/build-proxy.crt && update-ca-certificates; \
    fi && dotnet restore backend/Trade.Api/Trade.Api.csproj --locked-mode
COPY backend/ backend/
RUN dotnet publish backend/Trade.Api/Trade.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS runtime
WORKDIR /app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
COPY --from=build /app/publish .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Trade.Api.dll"]
