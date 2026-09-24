# Total Updater Next — Phase 11 / 0.11.0 — WCX New Installation

WPF-программа для Windows 7 SP1+ под .NET Framework 4.8. Это независимая новая реализация: старый Total Updater и его бинарные ресурсы не используются.

## Возможности

* обнаружение `wincmd.ini`, включая portable Total Commander и конфигурацию из AppData;
* чтение WCX, WLX, WFX и WDX, FileVersion/ProductVersion и текстовый fallback (`version`, `history`, `changelog`, `change`, `readme`);
* встроенный каталог на 931 запись со стабильными ID и проверенными alias-именами файлов;
* консервативное remote-сопоставление по двум индексам и имени бинарника в ZIP; неоднозначные варианты блокируются;
* явные состояния проверки, безопасное сравнение beta/rc/финальных версий;
* HTTP-провайдеры Ghisler и Totalcmd.net, скачивание в отдельный каталог;
* безопасная установка ZIP для уже установленных WCX/WLX/WFX/WDX с backup, проверкой после записи, повторяемым crash recovery и откатом выбранного плагина;
* установка новых WFX/WLX/WDX из каталога с транзакционной регистрацией в фактическом INI и manifest v4;
* шесть вкладок: «Обновления», «Каталог», «Настройки», «Локальная БД», «Источники», «О программе».

## Сборка и тесты

```powershell
dotnet build .\TotalUpdaterNext.slnx
dotnet run --project .\TotalUpdater.Next.Tests\TotalUpdater.Next.Tests.csproj
```

## Single EXE

Release-архив [`../release/TotalUpdater-0.11.0-win7plus.zip`](../release/TotalUpdater-0.11.0-win7plus.zip) содержит только `TotalUpdater.exe`. Стандартный каталог находится внутри EXE как embedded resource; внешний JSON не требуется на первом запуске.

Maintenance-команды тестового приложения: `--audit-catalog-aliases` проверяет collisions и расширения, `--audit-installed-coverage --ini=<путь>` показывает Exact/Alias/RemoteExact/Ambiguous/NotFound и процент покрытия; `--offline` отключает сетевой fallback. Remote lookup ничего не устанавливает и не переписывает каталог пользователя.

Новая установка WFX/WLX/WDX проверяет архитектуры TC и бинарников ZIP и останавливается с `ArchitectureMismatch` при несовпадении. INI patch не заменяет непредставимые в исходной кодировке символы на `?`: он выдаёт `EncodingConflict`. При восстановлении `RecoveryConflict` относится к файлу плагина, `ConfigRecoveryConflict` — к INI; после восстановления ожидаемого SHA-256 откат можно повторить.

Пользовательская БД создаётся только по явной команде: рядом с EXE в portable-режиме либо в `%APPDATA%\TotalUpdaterNext` при read-only установке.

Загруженные EXE/MSI не запускаются. Новая регистрация WCX, установка Total Commander и self-update не реализованы. Требующие прав администратора операции остаются недоступными без UAC/helper.
0.9.4 maintenance: `TotalUpdater.Next.Tests.exe --harvest-full-catalog` явно запускает `..\tools\Harvest-FullSourceCatalog.ps1`, который импортирует все типизированные записи TotalCmd.net и официальной страницы Ghisler как `MetadataOnly`, с official name/type/package URL, но без сгенерированных aliases. `..\tools\Harvest-CatalogEvidence.ps1` проверяет TotalCmd ZIP, а `..\tools\Harvest-GhislerEvidence.ps1` — ZIP из Ghisler; только извлечённые из архива точные aliases переводят запись в `VerifiedPackage` и `MetadataAndDownload`. `..\tools\Merge-CatalogEvidence.ps1` сохраняет проверенные правила. `TotalUpdater.Next.Tests.exe --audit-full-catalog` выводит размер, типы, evidence, отсутствующие sources и alias collisions.
