$ErrorActionPreference = 'Stop'
try {
    $sendTo = [Environment]::GetFolderPath('SendTo')
    $lnkPath = Join-Path $sendTo 'Мои файлы.lnk'
    if (Test-Path -LiteralPath $lnkPath) {
        Remove-Item -LiteralPath $lnkPath -Force
        Write-Host "Готово! Пункт 'Отправить -> Мои файлы' удалён." -ForegroundColor Green
        Write-Host "Удалён ярлык: $lnkPath"
    }
    else {
        Write-Host "Пункт 'Отправить -> Мои файлы' не найден — удалять нечего."
    }
    Write-Host "Сам файл MyFiles.exe не удаляется — при желании удалите папку с программой вручную."
    exit 0
}
catch {
    Write-Host ("ОШИБКА: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
