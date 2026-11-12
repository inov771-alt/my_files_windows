param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
try {
    if (-not $ExePath -or $ExePath.Trim().Length -eq 0) {
        $ExePath = Join-Path $PSScriptRoot 'MyFiles.exe'
    }
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        Write-Host "ОШИБКА: MyFiles.exe не найден: $ExePath" -ForegroundColor Red
        Write-Host "Положите этот скрипт рядом с MyFiles.exe (как в архиве сборки) и запустите снова."
        exit 1
    }
    $ExePath = (Resolve-Path -LiteralPath $ExePath).Path
    $sendTo = [Environment]::GetFolderPath('SendTo')
    if (-not $sendTo -or -not (Test-Path -LiteralPath $sendTo)) {
        Write-Host "ОШИБКА: не найдена папка SendTo текущего пользователя." -ForegroundColor Red
        exit 1
    }
    $lnkPath = Join-Path $sendTo 'Мои файлы.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($lnkPath)
    $shortcut.TargetPath = $ExePath
    $shortcut.WorkingDirectory = (Split-Path -Parent $ExePath)
    $shortcut.IconLocation = "$ExePath,0"
    $shortcut.Description = 'Загрузить файлы на files.spkmd.ru'
    $shortcut.Save()
    Write-Host "Готово! В Проводнике появился пункт: Отправить -> Мои файлы" -ForegroundColor Green
    Write-Host "Ярлык: $lnkPath"
    Write-Host "Программа: $ExePath"
    Write-Host ""
    Write-Host "Как пользоваться: выделите файлы -> правая кнопка мыши ->"
    Write-Host "Показать дополнительные параметры -> Отправить -> Мои файлы."
    exit 0
}
catch {
    Write-Host ("ОШИБКА: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
