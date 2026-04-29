# Heroes 5 — Duel Builder

Многопользовательский билдер дуэлей для Heroes of Might and Magic 5 с поддержкой LAN.

## Возможности

- **LAN мультиплеер** — создание комнат, подключение по IP, WebSocket синхронизация
- **Офлайн режим** — для тестирования без второго игрока
- **Прокачка героя** — 19 уровней, 4 слота выбора (навыки, перки), случайные статы
- **Армия** — 7 слотов, покупка и апгрейд юнитов фракции с учётом бюджета
- **Артефакты** — случайная витрина (6 Minor + 4 Major + 2 Relic), экипировка по слотам
- **Гильдия магов** — 5 кругов заклинаний по школам фракции, руны гномов, кличи орков
- **Генерация .h5u** — экспорт дуэль-пресета для игры
- **Мульти-пак парсер** — сканирует все .pak файлы с приоритетом по дате

## Требования

- Python 3.10+
- pip

## Установка

```bash
cd DuelEditor
pip install -r requirements.txt
```

## Запуск

```bash
python app.py /path/to/game/folder
```

Где `/path/to/game/folder` — путь к папке игры (содержащей `data/` и/или `UserMODs/` с `.pak` файлами).

### LAN режим
1. Игрок 1: запустите `python app.py /path/to/game` → откройте `http://localhost:5000`
2. Игрок 2: откройте `http://<IP игрока 1>:5000` в браузере

### Офлайн режим
Нажмите "Офлайн (тест)" на главном экране — второй игрок будет автоматически заполнен.

## Данные из .pak файлов

Парсер автоматически сканирует все `.pak` файлы и загружает:
- `Creatures.xdb` — существа (179 юнитов)
- `UndividedSpells.xdb` — заклинания (352 заклинания, 6 школ)
- `Skills.xdb` — навыки и перки (220 записей)
- `Artifacts.xdb` — артефакты (96 шт)
- `Any.xdb` — герои (97 героев, 8 фракций)
- `HeroClass.xdb` — классы героев (вероятности навыков и статов)

## Формат .h5u

Генерируемый файл — ZIP-архив:
```
Maps/DuelMode/Heroes/player1/Hero1.xdb  — конфиг героя 1
Maps/DuelMode/Heroes/player2/Hero2.xdb  — конфиг героя 2
Maps/DuelMode/PresetMap/map.xdb         — карта
Maps/DuelMode/PresetMap/map-tag.xdb     — метаданные
Maps/DuelMode/PresetMap/GroundTerrain.bin
UI/MPDMLobby/presets.(DuelPresets).xdb  — UI (только свой герой)
```

## Технологии

- Python + Flask + Flask-SocketIO
- Socket.IO для WebSocket (LAN мультиплеер)
- Pillow для DDS → PNG конвертации
- Vanilla JS фронтенд
