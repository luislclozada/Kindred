# One recipe for the three .NET hosts. Build context is the repo root; APP picks the host:
# Kindred.StudentService.Api, Kindred.MatchingService.Api or Kindred.Gateway.
# The build stage is the same for all three, so Docker compiles the repo once and reuses it.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/Services/MatchingService/Kindred.MatchingService.Api -c Release -o /out/Kindred.MatchingService.Api \
 && dotnet publish src/Services/StudentService/Kindred.StudentService.Api   -c Release -o /out/Kindred.StudentService.Api \
 && dotnet publish src/Gateway/Kindred.Gateway                              -c Release -o /out/Kindred.Gateway

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP
WORKDIR /app
COPY --from=build /out/$APP .
# A fixed name for the host's launcher, so the entrypoint needs no shell: a shell would drop
# settings whose names contain a hyphen (ReverseProxy__Clusters__student-service__...).
RUN ln -s /app/$APP /app/run
# Healthy = listening on 8080 (so migrations and seeding are done) and still up two seconds
# later, which gives a service's event consumer time to declare and bind its queue.
HEALTHCHECK --interval=3s --timeout=10s --retries=40 \
    CMD bash -c 'exec 3<>/dev/tcp/localhost/8080 && sleep 2'
ENTRYPOINT ["/app/run"]
