#Requires -Version 5.1
<#
    Установка «APK → телефон»: копирует программу в %LOCALAPPDATA%\ApkToPhone,
    прописывает пункт в контекстное меню .apk и запускает трей.
#>
[CmdletBinding()]
param(
    [string]$AppPath,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $AppPath) {
    $candidates = @(
        (Join-Path $root 'bin\publish\ApkToPhone.exe'),
        (Join-Path $root 'bin\Release\net8.0-windows\win-x64\publish\ApkToPhone.exe'),
        (Join-Path $root 'bin\Release\net8.0-windows\win-x64\ApkToPhone.exe'),
        (Join-Path $root 'ApkToPhone.exe')
    )
    $AppPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $AppPath -or -not (Test-Path -LiteralPath $AppPath)) {
    throw 'Не найден ApkToPhone.exe. Соберите проект: dotnet publish -c Release'
}

Write-Host 'Останавливаю запущенный экземпляр…'
Get-Process -Name ApkToPhone -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}
Start-Sleep -Milliseconds 400

$target = Join-Path $env:LOCALAPPDATA 'ApkToPhone'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath $AppPath -Destination (Join-Path $target 'ApkToPhone.exe') -Force
Write-Host "Скопировано в $target"

$exe = Join-Path $target 'ApkToPhone.exe'

# setup = автозапуск + пункт в меню + запуск трея
& $exe setup
Start-Sleep -Milliseconds 1500

$verbKey = 'HKCU:\Software\Classes\SystemFileAssociations\.apk\shell\ApkToPhone'
if (Test-Path $verbKey) {
    Write-Host 'Пункт «Установить на телефон» добавлен в контекстное меню .apk.' -ForegroundColor Green
} else {
    Write-Warning 'Ключ меню не появился. Запустите ApkToPhone.exe вручную и выберите «Обновить меню».'
}

$tray = Get-Process -Name ApkToPhone -ErrorAction SilentlyContinue
if ($tray) {
    Write-Host 'Трей запущен (иконка возле часов).' -ForegroundColor Green
} else {
    Write-Warning 'Трей не запустился. Запустите ApkToPhone.exe вручную.'
}

Write-Host ''
Write-Host 'Готово. Правый клик по любому .apk → «Установить на телефон» → выберите телефон.'
