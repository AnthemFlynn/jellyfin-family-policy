FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY global.json ./
COPY src/Directory.Build.props ./src/
COPY src/FamilyPolicy.Guard/ ./src/FamilyPolicy.Guard/
RUN dotnet restore src/FamilyPolicy.Guard/FamilyPolicy.Guard.csproj --locked-mode && dotnet publish src/FamilyPolicy.Guard/FamilyPolicy.Guard.csproj -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/runtime:10.0.12@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5
WORKDIR /app
COPY --from=build /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "FamilyPolicy.Guard.dll"]
