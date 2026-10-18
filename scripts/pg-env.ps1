# Dipakai oleh db.ps1 dan restore.ps1: baca .env (tidak masuk git) dan pecah connection string
# supaya tidak ada kredensial yang ditulis di dalam skrip.
# shellcheck disable=PSUseDeclaredVarsMoreThanAssignments

function Get-DotEnvValue {
    param([string]$Key)

    # Variabel lingkungan menang atas .env — sama seperti EnvironmentConfig di aplikasi C#.
    # Catatan: "${env:$Key}" tidak mengembang jadi nama variabelnya (selalu kosong), jadi
    # nilainya diambil lewat API .NET.
    $dariEnvironment = [Environment]::GetEnvironmentVariable($Key)
    if ($dariEnvironment) { return $dariEnvironment }
    $dir = Split-Path -Parent $PSScriptRoot
    while ($dir) {
        $file = Join-Path $dir ".env"
        if (Test-Path $file) {
            foreach ($line in Get-Content $file) {
                $t = $line.Trim()
                if (-not $t -or $t.StartsWith("#")) { continue }
                $ix = $t.IndexOf("=")
                if ($ix -le 0) { continue }
                if ($t.Substring(0, $ix).Trim() -eq $Key) { return $t.Substring($ix + 1).Trim() }
            }
            return $null
        }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}

function Get-PgBin {
    param([string]$Tool)
    $bin = Get-DotEnvValue "PG_BIN"
    if (-not $bin) { $bin = Join-Path $env:LOCALAPPDATA "pgsql17\pgsql\bin" }
    $path = Join-Path $bin "$Tool.exe"
    if (-not (Test-Path $path)) {
        throw "Tidak menemukan $Tool.exe di $bin. Atur PG_BIN di berkas .env."
    }
    return $path
}

# Folder hasil backup: sama dengan yang dipakai aplikasi (BACKUP_DIR), supaya arsip yang
# dibuat manual dan arsip pekerja otomatis tidak hidup di dua tempat berbeda.
function Get-BackupDir {
    $dir = Get-DotEnvValue "BACKUP_DIR"
    if (-not $dir) { $dir = Join-Path $env:LOCALAPPDATA "laundry-pg\backups" }
    return $dir
}

# Connection string Npgsql (Host=...;Port=...;Database=...;Username=...;Password=...) jadi objek.
function Parse-PgConn {
    param([string]$Key)

    $conn = Get-DotEnvValue $Key
    if (-not $conn) { throw "$Key belum diisi di .env." }
    $out = @{ Host = "localhost"; Port = 5432; Database = ""; Username = ""; Password = "" }
    foreach ($part in $conn.Split(";")) {
        $t = $part.Trim()
        if (-not $t) { continue }
        $ix = $t.IndexOf("=")
        if ($ix -le 0) { continue }
        switch -Regex ($t.Substring(0, $ix).Trim()) {
            "^Host$" { $out.Host = $t.Substring($ix + 1).Trim() }
            "^Port$" { $out.Port = [int]$t.Substring($ix + 1).Trim() }
            "^Database$" { $out.Database = $t.Substring($ix + 1).Trim() }
            "^Username$" { $out.Username = $t.Substring($ix + 1).Trim() }
            "^Password$" { $out.Password = $t.Substring($ix + 1).Trim() }
        }
    }
    if (-not $out.Database -or -not $out.Username) { throw "$Key tidak lengkap (butuh Database dan Username)." }
    return $out
}

function Invoke-PgDump {
    param($Conn, [string]$Out)

    $exe = Get-PgBin "pg_dump"
    $env:PGPASSWORD = $Conn.Password
    try {
        & $exe -h $Conn.Host -p $Conn.Port -U $Conn.Username -d $Conn.Database -F c -w -f $Out
        if ($LASTEXITCODE -ne 0) { throw "pg_dump keluar dengan kode $LASTEXITCODE." }
    }
    finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
    return (Get-Item $Out).Length
}

function Test-PgDump {
    param([string]$File)

    $exe = Get-PgBin "pg_restore"
    $daftar = & $exe --list $File 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Arsip $File tidak terbaca pg_restore - jangan dipakai untuk restore." }
    $objek = @($daftar | Where-Object { $_ -and -not $_.StartsWith(";") }).Count
    if ($objek -eq 0) { throw "Arsip $File kosong: tidak ada objek di dalamnya." }
    return $objek
}
