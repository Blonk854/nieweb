#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$bin = 'C:\VS_Project\Nieweb\src\Nieweb.Api\bin\Debug\net10.0'
Add-Type -Path (Join-Path $bin 'SQLitePCLRaw.core.dll')
Add-Type -Path (Join-Path $bin 'SQLitePCLRaw.provider.e_sqlite3.dll')
Add-Type -Path (Join-Path $bin 'SQLitePCLRaw.batteries_v2.dll')
Add-Type -Path (Join-Path $bin 'Microsoft.Data.Sqlite.dll')
[SQLitePCL.Batteries_V2]::Init()
$cn = New-Object Microsoft.Data.Sqlite.SqliteConnection('Data Source=C:\VS_Project\Nieweb\src\Nieweb.Api\nieweb-dev.db')
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT Id, Email, DisplayName, MustRotatePassword FROM AspNetUsers"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host ("User: Id={0}  Email={1}  Name={2}  MustRotate={3}" -f $r.GetValue(0), $r.GetValue(1), $r.GetValue(2), $r.GetValue(3))
}
$r.Close()
$cn.Close()
