# syntax=docker/dockerfile:1
# Build with the SDK, run on Microsoft's chiseled ASP.NET image: no shell, no package manager, non-root user.
# Base images are pinned by digest; Dependabot proposes updates.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/TicketApi/TicketApi.csproj src/TicketApi/
RUN dotnet restore src/TicketApi/TicketApi.csproj
COPY src/ src/
# /data is created here because the runtime image has no shell to run mkdir.
RUN dotnet publish src/TicketApi/TicketApi.csproj -c Release -o /app --no-restore && mkdir /data

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:9651fa59abcdf177c30392cb44a820605ca5d618429ab37acbf6e7c644510b02
WORKDIR /app
COPY --from=build /app .
# The only writable path: the SQLite database. Run the container with --read-only.
COPY --from=build --chown=$APP_UID /data /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Tickets="Data Source=/data/tickets.db"
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TicketApi.dll"]
