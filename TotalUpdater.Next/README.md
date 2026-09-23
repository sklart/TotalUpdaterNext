# Total Updater Next — Phase 8.1

WPF-программа для Windows 7 SP1+ под .NET Framework 4.8. Это независимая новая реализация: старый Total Updater и его бинарные ресурсы не используются.

## Возможности

* обнаружение `wincmd.ini`, включая portable Total Commander и конфигурацию из AppData;
* чтение WCX, WLX, WFX и WDX, FileVersion/ProductVersion и текстовый fallback (`version`, `history`, `changelog`, `change`, `readme`);
* встроенный каталог со стабильными ID и alias-именами файлов;
* явные состояния проверки, безопасное сравнение beta/rc/финальных версий;
* HTTP-провайдеры Ghisler и Totalcmd.net, скачивание в отдельный каталог;
* безопасная установка ZIP для уже установленных WCX/WLX/WFX/WDX с backup, проверкой после записи, повторяемым crash recovery и откатом выбранного плагина;
* установка новых WFX/WLX/WDX из каталога с транзакционной регистрацией в фактическом INI и manifest v4;
* пять вкладок: «Обновления», «Каталог», «Настройки», «Локальная БД», «О программе».

## Сборка и тесты

```powershell
dotnet build .\TotalUpdaterNext.slnx
dotnet run --project .\TotalUpdater.Next.Tests\TotalUpdater.Next.Tests.csproj
```

## Single EXE

Release-архив [`../release/TotalUpdater-0.9.1-win7plus.zip`](../release/TotalUpdater-0.9.1-win7plus.zip) содержит только `TotalUpdater.exe`. Стандартный каталог находится внутри EXE как embedded resource; внешний JSON не требуется на первом запуске.

Новая установка WFX/WLX/WDX проверяет архитектуры TC и бинарников ZIP и останавливается с `ArchitectureMismatch` при несовпадении. INI patch не заменяет непредставимые в исходной кодировке символы на `?`: он выдаёт `EncodingConflict`. При восстановлении `RecoveryConflict` относится к файлу плагина, `ConfigRecoveryConflict` — к INI; после восстановления ожидаемого SHA-256 откат можно повторить.

Пользовательская БД создаётся только по явной команде: рядом с EXE в portable-режиме либо в `%APPDATA%\TotalUpdaterNext` при read-only установке.

Загруженные EXE/MSI не запускаются. Новая регистрация WCX, установка Total Commander и self-update не реализованы. Требующие прав администратора операции остаются недоступными без UAC/helper.
