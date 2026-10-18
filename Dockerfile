# Hanya sisi API yang dibangun di sini. Laundry.Desktop adalah WPF (khusus Windows) dan
# Laundry.Sync adalah worker yang di laptop tetap dijalankan sendiri, jadi keduanya tidak
# ikut masuk image — jalur build ini tidak pernah menyentuh proyek net8.0-windows.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Berkas proyek disalin lebih dulu supaya lapisan restore ikut terpakai ulang selama kode
# sumbernya belum berubah (build ulang jadi cepat, dan itu penting di paket gratis).
COPY Directory.Build.props ./
COPY src/Laundry.Domain/Laundry.Domain.csproj src/Laundry.Domain/
COPY src/Laundry.Application/Laundry.Application.csproj src/Laundry.Application/
COPY src/Laundry.Infrastructure/Laundry.Infrastructure.csproj src/Laundry.Infrastructure/
COPY src/Laundry.Api/Laundry.Api.csproj src/Laundry.Api/
RUN dotnet restore src/Laundry.Api/Laundry.Api.csproj

COPY src/ src/
RUN dotnet publish src/Laundry.Api/Laundry.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# ASPNETCORE_URLS sengaja tidak diset di sini. Program.cs menerjemahkan variabel PORT dari
# host menjadi bind ke 0.0.0.0:$PORT; begitu ASPNETCORE_URLS diisi, nilai itu yang menang dan
# port dari host diabaikan sehingga layanan terlihat hidup tapi tidak pernah bisa dibuka.
ENV ENVIRONMENT=Production
EXPOSE 10000
USER $APP_UID

ENTRYPOINT ["dotnet", "Laundry.Api.dll"]
