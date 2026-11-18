# สำหรับ deploy บน Render / Railway / เซิร์ฟเวอร์ที่ใช้ Docker
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ItHelpdesk/ ItHelpdesk/
RUN dotnet publish ItHelpdesk/ItHelpdesk.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# ให้เวลาในประวัติสถานะเป็นเวลาไทย (เซิร์ฟเวอร์ Render ใช้ UTC)
ENV TZ=Asia/Bangkok
EXPOSE 8080
ENTRYPOINT ["dotnet", "ItHelpdesk.dll"]
