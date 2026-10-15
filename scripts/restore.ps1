param(
    [Parameter(Mandatory = $true, Position = 0)][string]$File,
    [string]$Target = "admin",
    [string]$Konfirmasi = ""
)

# Restore adalah satu-satunya operasi database yang tidak bisa dibatalkan, jadi skrip ini
# sengaja Cerewet: arsip harus terbaca, nama target harus diketik ulang, dan aplikasi harus mati.
. (Join-Path $PSScriptRoot "pg-env.ps1")

if ($Target -notin @("admin", "cloud")) { throw "Target harus 'admin' atau 'cloud'." }
$connKey = "CONN_" + $Target.ToUpper()

$out = Get-BackupDir
$path = if ([System.IO.Path]::IsPathRooted($File)) { $File } else { Join-Path $out $File }
if (-not (Test-Path $path)) { throw "Berkas backup tidak ada: $path" }

$objek = Test-PgDump $path
Write-Host "Arsip $path terbaca: $objek objek."

if ($Konfirmasi -ne $Target) {
    Write-Host "Restore akan MENIMPA database $Target ($($connKey)) dengan isi berkas di atas."
    Write-Host "Hentikan Laundry.Api, Laundry.Sync, dan Laundry.Desktop sebelum lanjut."
    $Konfirmasi = Read-Host "Ketik '$Target' untuk lanjut (apa pun membatalkan)"
}
if ($Konfirmasi -ne $Target) { Write-Host "Dibatalkan."; exit 1 }

$c = Parse-PgConn $connKey
$exe = Get-PgBin "pg_restore"
$env:PGPASSWORD = $c.Password
try {
    & $exe --clean --if-exists --no-owner --no-privileges `
        -h $c.Host -p $c.Port -U $c.Username -d $c.Database $path
    if ($LASTEXITCODE -ne 0) { throw "pg_restore keluar dengan kode $LASTEXITCODE - periksa pesan di atas." }
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

Write-Host "Restore $Target selesai dari $path."
Write-Host "Catatan: kalau hanya satu sisi yang di-restore, sisi lainnya bisa lebih baru."
Write-Host "Jalankan GET /api/sync/status dan GET /api/backup/status untuk memastikan keadaan."
