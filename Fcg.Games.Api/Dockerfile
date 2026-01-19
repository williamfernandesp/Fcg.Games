# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# copy csproj and restore first
COPY Fcg.Games/Fcg.Games.Api/Fcg.Games.Api.csproj Fcg.Games/Fcg.Games.Api/
COPY Fcg.Payment/src/Fcg.Contracts/Fcg.Contracts.csproj Fcg.Payment/src/Fcg.Contracts/
RUN dotnet restore Fcg.Games/Fcg.Games.Api/Fcg.Games.Api.csproj

# copy everything and publish
COPY Fcg.Games Fcg.Games
COPY Fcg.Payment Fcg.Payment
RUN dotnet publish Fcg.Games/Fcg.Games.Api/Fcg.Games.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Fcg.Games.Api.dll"]
