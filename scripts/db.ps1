param([Parameter(Position = 0)][string]$Action = "start")

. (Join-Path $PSScriptRoot "pg-env.ps1")

$Base = Join-Path $env:LOCALAPPDATA "laundry-pg"
$InstanceMap = @{
    admin = @{ Dir = "admin"; Port = 5433 }
    cloud = @{ Dir = "cloud"; Port = 5434 }
}

switch ($Action) {
    "start" {
        foreach ($i in $InstanceMap.GetEnumerator()) {
            $d = Join-Path $Base $i.Value.Dir
            & (Get-PgBin "pg_ctl") -D $d -o "-p $($i.Value.Port)" -l (Join-Path $Base "$($i.Value.Dir).log") start
        }
    }
    "stop" {
        foreach ($i in $InstanceMap.GetEnumerator()) {
            $d = Join-Path $Base $i.Value.Dir
            & (Get-PgBin "pg_ctl") -D $d stop
        }
    }
    "status" {
        foreach ($i in $InstanceMap.GetEnumerator()) {
            $d = Join-Path $Base $i.Value.Dir
            Write-Host "== $($i.Key) (port $($i.Value.Port)) =="
            & (Get-PgBin "pg_ctl") -D $d status
        }
    }
    "backup" {
        # Cara manual; pekerja backup aplikasi melakukan hal yang sama + retensi + riwayat.
        $out = Get-BackupDir
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        foreach ($nama in @("admin", "cloud")) {
            # Penanda unik: dua backup dalam detik yang sama tidak saling menimpa.
            $penanda = ([guid]::NewGuid()).ToString("N").Substring(0, 8)
            $file = Join-Path $out "$nama-$stamp-$penanda.dump"
            $ukuran = Invoke-PgDump (Parse-PgConn "CONN_$($nama.ToUpper())") $file
            $objek = Test-PgDump $file
            Write-Host "Backup $nama : $file ($ukuran byte, $objek objek terverifikasi)"
        }
    }
    "psql-admin" {
        $c = Parse-PgConn "CONN_ADMIN"
        $env:PGPASSWORD = $c.Password
        try { & (Get-PgBin "psql") -h $c.Host -p $c.Port -U $c.Username -d $c.Database }
        finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
    }
    default { Write-Host "Gunakan: start | stop | status | backup | psql-admin" }
}
