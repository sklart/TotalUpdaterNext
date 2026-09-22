# Total Updater Next — Phase 1

WPF-программа для Windows 7 SP1+ под .NET Framework 4.8. Это независимая новая реализация: старый Total Updater и его бинарные ресурсы не используются.

## Возможности

* обнаружение `wincmd.ini`, включая portable Total Commander и конфигурацию из AppData;
* чтение WCX, WLX, WFX и WDX, FileVersion/ProductVersion и текстовый fallback (`version`, `history`, `changelog`, `change`, `readme`);
* встроенный каталог со стабильными ID и alias-именами файлов;
* явные состояния проверки, безопасное сравнение beta/rc/финальных версий;
* HTTP-провайдеры Ghisler и Totalcmd.net, скачивание в отдельный каталог без установки;
* четыре вкладки: «Обновления», «Настройки», «Локальная БД», «О программе».

## Сборка и тесты

```powershell
dotnet build .\TotalUpdaterNext.slnx
dotnet run --project .\TotalUpdater.Next.Tests\TotalUpdater.Next.Tests.csproj
```

## Single EXE

Release-архив [`../release/TotalUpdater-0.5.2-win7plus.zip`](../release/TotalUpdater-0.5.2-win7plus.zip) содержит только `TotalUpdater.exe`. Стандартный каталог находится внутри EXE как embedded resource; внешний JSON не требуется на первом запуске.

Пользовательская БД создаётся только по явной команде: рядом с EXE в portable-режиме либо в `%APPDATA%\TotalUpdaterNext` при read-only установке.

Автоматическая установка, распаковка поверх существующих плагинов и запуск загруженных EXE/MSI намеренно не реализованы.
