# Builds the server (Todo.Api) together with the Client it serves. Build from the repo root: docker build -t todo-api .

# Build stage: has the .NET SDK. Only its publish output reaches the final image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from only the files restore reads, so this layer (and the NuGet download) is reused
# until a project file or a package version changes. Restore must not depend on the source: anything the SDK
# decides from source files (such as RequiresAspNetWebAssets, set in Todo.Api.csproj) has to be set explicitly.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Todo.Api/Todo.Api.csproj src/Todo.Api/
COPY src/Todo.Client/Todo.Client.csproj src/Todo.Client/
COPY src/Todo.Contracts/Todo.Contracts.csproj src/Todo.Contracts/
# The RuntimeIdentifier property, not restore's -r: -r makes every project in the graph restore for linux-x64,
# including the Blazor Client, which only runs as browser-wasm, and that fails (NU1102, no Mono linux-x64 runtime pack).
RUN dotnet restore src/Todo.Api -p:RuntimeIdentifier=linux-x64

# Then the source, which changes on almost every build.
# Framework-dependent for linux-x64: the runtime comes from the final image, and other platforms' native libraries are left out.
COPY src/ src/
RUN dotnet publish src/Todo.Api -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish

# Final stage: the ASP.NET Core runtime only. Chiseled means no shell or package manager, and it runs as the
# non-root "app" user. -extra adds ICU and time zone data, so culture and time zone behavior match a full OS.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app/publish .

# The .NET images listen on plain HTTP port 8080. In production, TLS ends at the hosting platform's ingress.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Todo.Api.dll"]
