# syntax=docker/dockerfile:1
# Build context: repository root.  docker build -f sources/ExpenseHub.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Api/ExpenseHub.Api.csproj sources/ExpenseHub.Api/
RUN dotnet restore sources/ExpenseHub.Api/ExpenseHub.Api.csproj
COPY sources/ExpenseHub.Api/ sources/ExpenseHub.Api/
RUN dotnet publish sources/ExpenseHub.Api/ExpenseHub.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG REVISION=unknown
ARG SOURCE_URL=
LABEL org.opencontainers.image.title="expensehub-api" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.source="${SOURCE_URL}"
ENV EXPENSEHUB_REVISION=${REVISION}
WORKDIR /app
COPY --from=build /app .
# Same directory and owner in both images: the SQLite file and the keys live in the shared /data volume.
RUN mkdir -p /data && chown -R "$APP_UID:$APP_UID" /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseHub.Api.dll"]
