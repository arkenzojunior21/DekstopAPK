# Terapkan migrasi skema ke database sisi admin dan/atau sisi cloud.
# Connection string dibaca dari environment, atau dari berkas .env di folder ini (dan induknya).
#
#   powershell -File scripts/migrate.ps1          # kedua sisi
#   powershell -File scripts/migrate.ps1 admin    # hanya sisi admin
#   powershell -File scripts/migrate.ps1 cloud    # hanya sisi cloud (mis. database Neon)
#
# Skema kedua sisi identik, jadi migrate.ps1 selalu memakai konteks migrasi LaundryDbContext:
# lewat CloudDbContext EF Core tidak melihat satu pun migrasi (sudah diuji pada database kosong).
# Untuk sisi cloud, CONN_ADMIN diarahkan sebentar ke CONN_CLOUD di dalam proses skrip ini saja —
# dengan begitu normalisasi bentuk URI (postgresql://...) ikut dilakukan kode aplikasi, tidak
# diduplikasi di PowerShell. Nilai aslinya dikembalikan lagi di akhir.
param([Parameter(Position = 0)][string]$Sisi = "dua")

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "pg-env.ps1")
$root = Split-Path -Parent $PSScriptRoot

function Jalankan-Migrasi([string]$label) {
    Write-Host "== migrasi skema: sisi $label =="
    dotnet ef database update --project src/Laundry.Infrastructure --startup-project src/Laundry.Api `
        --context LaundryDbContext
    if ($LASTEXITCODE -ne 0) { throw "Migrasi sisi $label gagal (kode keluar $LASTEXITCODE)." }
}

if ($Sisi -notin @("dua", "admin", "cloud")) { throw "Gunakan: dua (bawaan) | admin | cloud" }

Push-Location $root
$adminAsli = $env:CONN_ADMIN
try {
    if ($Sisi -in @("dua", "admin")) {
        if (-not (Get-DotEnvValue "CONN_ADMIN")) { throw "CONN_ADMIN belum diisi di environment atau berkas .env." }
        Jalankan-Migrasi "admin"
    }

    if ($Sisi -in @("dua", "cloud")) {
        $connCloud = Get-DotEnvValue "CONN_CLOUD"
        if (-not $connCloud) { throw "CONN_CLOUD belum diisi di environment atau berkas .env." }
        $env:CONN_ADMIN = $connCloud
        Jalankan-Migrasi "cloud"
    }
    Write-Host "Skema selesai diperiksa."
} finally {
    $env:CONN_ADMIN = $adminAsli
    Pop-Location
}
