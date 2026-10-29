# Builds two images from one build stage. Build from the repo root.
#   The server (Todo.Api) with the Client it serves, the default target:  docker build -t todo-api .
#   The migration bundle, run once before the server starts (ADR-0004):  docker build --target migrations -t todo-migrations .
# compose.yaml builds both.

# Build stage: has the .NET SDK. Only its outputs reach the final images.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from only the files restore reads, so this layer (and the NuGet download) is reused
# until a project file or a package version changes. Restore must not depend on the source: anything the SDK
# decides from source files (such as RequiresAspNetWebAssets, set in Todo.Api.csproj) has to be set explicitly.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Todo.Api/Todo.Api.csproj src/Todo.Api/
COPY src/Todo.Client/Todo.Client.csproj src/Todo.Client/
COPY src/Todo.Contracts/Todo.Contracts.csproj src/Todo.Contracts/
# dotnet-ef, for building the migration bundle.
COPY .config/dotnet-tools.json .config/
RUN dotnet tool restore
# The RuntimeIdentifier property, not restore's -r: -r makes every project in the graph restore for linux-x64,
# including the Blazor Client, which only runs as browser-wasm, and that fails (NU1102, no Mono linux-x64 runtime pack).
RUN dotnet restore src/Todo.Api -p:RuntimeIdentifier=linux-x64

# Then the source, which changes on almost every build.
COPY src/ src/

# The server's files. Framework-dependent for linux-x64: the runtime comes from the final image,
# and other platforms' native libraries are left out.
FROM build AS publish
RUN dotnet publish src/Todo.Api -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish

# The migration bundle: one executable that applies any pending migrations to the database it's given.
FROM build AS bundle
# EF tools run Program.cs to configure the DbContext, in Development unless told otherwise.
ENV ASPNETCORE_ENVIRONMENT=Production
RUN dotnet ef migrations bundle --project src/Todo.Api --configuration Release --target-runtime linux-x64 --output /app/efbundle

# The bundle loads Todo.Api.dll to configure its DbContext, so it needs the ASP.NET Core runtime too.
# Same base as the server's image, so the two share its layers.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra AS migrations
WORKDIR /app
COPY --from=bundle /app/efbundle .
# Likewise when the bundle runs (ENV doesn't carry over from the bundle stage: this is a new image).
ENV ASPNETCORE_ENVIRONMENT=Production
# Pass the connection with --connection when running it.
ENTRYPOINT ["./efbundle"]

# Final stage, the default target: the server. The ASP.NET Core runtime only. Chiseled means no shell or package
# manager, and it runs as the non-root "app" user. -extra adds ICU and time zone data, so culture and time zone
# behavior match a full OS.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra
WORKDIR /app
COPY --from=publish /app/publish .

# The .NET images listen on plain HTTP port 8080. In production, TLS ends at the hosting platform's ingress.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Todo.Api.dll"]
