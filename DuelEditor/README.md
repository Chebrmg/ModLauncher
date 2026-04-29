# Heroes 5 — Редактор Дуэль-Пресетов

Редактор для создания и управления дуэль-пресетами Heroes of Might and Magic 5.

## Возможности

- Загрузка данных юнитов напрямую из `.pak` файлов игры (zip с XML конфигами)
- Отображение иконок существ (конвертация DDS → PNG)
- Настройка двух игроков: герой (уровень, характеристики) + армия (7 слотов)
- Фильтрация существ по фракциям и поиск по имени
- Подробные тултипы со статистикой каждого существа
- Сохранение/загрузка пресетов
- Экспорт/импорт пресетов в JSON формате
- Подсчёт силы армии

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
python app.py /path/to/data1.pak
```

Где `/path/to/data1.pak` — путь к `.pak` файлу игры, содержащему данные существ.

После запуска откройте в браузере: http://localhost:5000

## Структура данных из .pak файла

Редактор парсит следующую структуру из `.pak` (zip) архива:

- `GameMechanics/RefTables/Creatures.xdb` — таблица всех существ
- `GameMechanics/Creature/Creatures/{Faction}/{Name}.xdb` — характеристики существ
- `GameMechanics/CreatureVisual/Creatures/{Faction}/{Name}.xdb` — визуальные данные (иконки, описания)
- `Text/Game/Creatures/{Faction}/*.txt` — названия и описания (UTF-16)
- `Textures/Interface/CombatArena/Faces/{Faction}/*.dds` — иконки существ

## Формат пресета

Пресеты сохраняются в JSON формате:

```json
{
  "name": "Название пресета",
  "player1": {
    "name": "Игрок 1",
    "hero": {
      "level": 10,
      "attack": 5,
      "defense": 3,
      "spellpower": 7,
      "knowledge": 4
    },
    "army": [
      { "id": "CREATURE_ARCHER", "count": 50 },
      { "id": "CREATURE_ANGEL", "count": 5 },
      ...
    ]
  },
  "player2": { ... }
}
```
