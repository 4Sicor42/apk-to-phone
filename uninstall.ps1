#Requires -Version 5.1
<#
    Удаление «APK → телефон»: убирает пункт меню, автозапуск и (по желанию) файлы.
#>
[CmdletBinding()]
param(
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Continue'

$target = Join-Path $env:LOCALAPPDATA 'ApkToPhone'
$exe = Join-Path $target 'ApkToPhone.exe'

if (Test-Path -LiteralPath $exe) {
    & $exe uninstall
    Start-Sleep -Milliseconds 600
}

Get-Process -Name ApkToPhone -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}

# На всякий случай — чистим реестр напрямую
Remove-Item 'HKCU:\Software\Classes\SystemFileAssociations\.apk\shell\ApkToPhone' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\ApkToPhone.Devices' -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ApkToPhone' -ErrorAction SilentlyContinue

if (-not $KeepFiles) {
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Файлы программы удалены.'
} else {
    Write-Host "Файлы оставлены: $target"
}

Write-Host 'Пункт «Установить на телефон» удалён из контекстного меню.' -ForegroundColor Green
