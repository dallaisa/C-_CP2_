# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f sources/ExpenseHub.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Api/ExpenseHub.Api.csproj sources/ExpenseHub.Api/
COPY sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj sources/ExpenseHub.Worker/
RUN dotnet restore sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj
COPY sources/ExpenseHub.Api/ sources/ExpenseHub.Api/
COPY sources/ExpenseHub.Worker/ sources/ExpenseHub.Worker/
RUN dotnet publish sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj \
    -c Release -o /app --no-restore

# The worker references the API project, so it needs the ASP.NET Core shared framework as well.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG REVISION=unknown
ARG SOURCE_URL=
LABEL org.opencontainers.image.title="expensehub-worker" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.source="${SOURCE_URL}"
ENV EXPENSEHUB_REVISION=${REVISION}
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown -R "$APP_UID:$APP_UID" /data
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseHub.Worker.dll"]
