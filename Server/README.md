# ModLauncher Server

HTTP-сервер для раздачи модов и проверки обновлений.

## Быстрый старт

1. Положите `.pak` файл мода в папку `mods/`:
   ```
   Server/
   └── mods/
       └── Chebovka1.5.2.pak
   ```

2. Настройте `mod_info.json` (создаётся автоматически при первом запуске):
   ```json
   {
     "mod_name": "Chebovka",
     "version": "1.5.2",
     "file_name": "Chebovka1.5.2.pak",
     "changelog": "Initial release"
   }
   ```

3. Запустите сервер:
   ```bash
   python server.py --port 8080
   ```

## API

| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/version` | Текущая версия мода (JSON) |
| GET | `/api/download` | Скачать файл мода |
| GET | `/api/health` | Проверка работоспособности |

### Пример ответа `/api/version`
```json
{
  "mod_name": "Chebovka",
  "version": "1.5.2",
  "file_name": "Chebovka1.5.2.pak",
  "changelog": "Initial release",
  "available": true,
  "sha256": "abc123...",
  "file_size": 12345678
}
```

## Обновление мода

1. Замените файл в `mods/` на новую версию
2. Обновите `version` и `file_name` в `mod_info.json`
3. Перезапустите сервер

## Настройка лаунчера

Создайте файл `server_config.json` рядом с лаунчером:
```json
{
  "server_url": "http://your-server-ip:8080"
}
```
