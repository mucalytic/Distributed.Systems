# One Dockerfile for all three services — the project to publish is passed
# as a build arg (PROJECT=Orders.Api | Orders.Worker | Billing.Worker).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish src/$PROJECT -c Release -o /app

# aspnet runtime image works for the workers too (it's a superset of runtime).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV PROJECT_DLL=$PROJECT.dll
ENTRYPOINT dotnet $PROJECT_DLL
