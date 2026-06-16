# RecallHub

[English](README.md) | [Русский](Readme.ru.md)

[![Версия](https://img.shields.io/badge/version-1.0.0-6f42c1?style=for-the-badge)](https://github.com/whitecristafer/oxide-RecallHub)
[![Платформа](https://img.shields.io/badge/platform-Oxide%20%2F%20Rust-orange?style=for-the-badge)](https://github.com/whitecristafer/oxide-RecallHub)
[![Язык](https://img.shields.io/badge/language-C%23-239120?style=for-the-badge)](https://github.com/whitecristafer/oxide-RecallHub)
[![Статус](https://img.shields.io/badge/status-stable-success?style=for-the-badge)](https://github.com/whitecristafer/oxide-RecallHub)
[![Auto Update](https://img.shields.io/badge/auto%20update-latest%20enabled-blue?style=for-the-badge)](https://github.com/whitecristafer/oxide-RecallHub)

`RecallHub` — плагин для Rust на Oxide/uMod, который добавляет телепортацию на **Outpost** и **Bandit Camp** с собственными точками спавна, таймером ожидания, cooldown-системой, отменой телепортации и автоматической проверкой обновлений через GitHub.

## Назначение

Плагин нужен для серверов, где требуется удобный и управляемый способ перемещения игроков между ключевыми монументами без использования тяжёлых teleport-решений.

Он подходит для серверов, где важно:

- ограничить телепортацию правилами и cooldown
- оставить гибкую настройку точек появления
- использовать отдельные команды для разных направлений
- автоматически подтягивать актуальную стабильную версию
- игнорировать dev-сборки при обновлении

## Возможности

- Телепортация на **Outpost**
- Телепортация в **Bandit Camp**
- Поддержка кастомных точек спавна
- Автоопределение точек спавна для монументов
- Countdown перед телепортом
- Отмена телепортации через команду
- Отдельные cooldown для каждого направления
- Блокировка телепортации при определённых условиях
- Поддержка `NoEscape` для raid/combat block
- Сброс hostile-таймера после телепорта
- Красивый стартовый вывод в консоль
- Автопроверка обновлений при запуске сервера
- Фильтрация dev-версий при обновлении

## Как работает автообновление

При запуске плагин обращается к GitHub и сравнивает локальную версию с версией из `latest`.

Правила обновления:

| Ситуация | Результат |
|---|---|
| Удалённая версия — стабильная release и выше локальной | Обновление скачивается |
| Удалённая версия равна локальной или ниже | Обновление не выполняется |
| Удалённая версия имеет формат dev, например `d1.0.1` | Игнорируется |
| Локальная версия dev | Не участвует в скачивании через `latest` |

Только стабильные релизы считаются допустимыми целями для автообновления.

## Команды

| Команда | По умолчанию | Назначение |
|---|---:|---|
| `/otp` | Outpost | Запуск телепортации на Outpost |
| `/btp` | Bandit Camp | Запуск телепортации в Bandit Camp |
| `/ttc` | Cancel Teleport | Отмена текущего телепорта |

## Права доступа

| Permission | Назначение |
|---|---|
| `recallhub.outpost` | Разрешает использовать телепортацию на Outpost |
| `recallhub.bandit` | Разрешает использовать телепортацию в Bandit Camp |
| `recallhub.nocooldown` | Убирает cooldown для телепортации |

## Установка

1. Поместите `RecallHub.cs` в папку `oxide/plugins/`.
2. Перезапустите сервер или загрузите плагин вручную.
3. Дождитесь создания конфигурации и файла данных.
4. При необходимости настройте точки спавна и параметры обновления.

## Настройка

После первого запуска плагин создаёт конфиг с основными параметрами:

- команды телепортации
- cooldown и countdown
- блокировка телепорта при mounted
- блокировка телепорта с Cargo Ship
- автоопределение спавнов Outpost и Bandit Camp
- параметры обновления и URL источника

Пример ключевых настроек:

```json
{
  "OutpostCommand": "otp",
  "BanditCommand": "btp",
  "CancelCommand": "ttc",
  "Update": {
    "Enabled": true,
    "CheckOnStartup": true,
    "SourceUrl": "https://raw.githubusercontent.com/whitecristafer/oxide-RecallHub/main/RecallHub.cs",
    "TimeoutSeconds": 15
  }
}
```

## Зависимости

- Rust server
- Oxide/uMod
- `NoEscape` для raid/combat block

## Статус проекта

Проект демонстрационный и может дорабатываться под конкретный сервер.

## Поддержка

Если нужны дополнительные функции, обычно добавляют:

- отдельные права на разные зоны
- ограничения по миру или биому
- логирование телепортаций
- интеграцию с economy
- кастомные сообщения и форматирование чата
