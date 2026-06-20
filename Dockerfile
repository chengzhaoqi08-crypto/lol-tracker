# --- build 阶段:用 SDK 镜像还原 + 发布 ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先单独 COPY csproj 还原,利用 Docker 层缓存
COPY LolTracker.Api/LolTracker.Api.csproj LolTracker.Api/
RUN dotnet restore LolTracker.Api/LolTracker.Api.csproj

COPY . .
RUN dotnet publish LolTracker.Api/LolTracker.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- 运行阶段:精简的 ASP.NET 运行时镜像 ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 本地默认 8080;Render 等平台会在运行时用 PORT 覆盖。
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LolTracker.Api.dll"]
