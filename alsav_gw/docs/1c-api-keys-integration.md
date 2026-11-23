# ALSAV Gateway: интеграция API-ключей клиентов

Документ описывает внутренний endpoint `alsav_gw` для управления API-ключами внешних клиентов (`/clientApi/*`, заголовок `X-API-Key`).

## Общие правила

- Базовый URL: внутренний адрес gateway.
- Аутентификация: заголовок `X-Internal-Secret: <InternalApi:SecretKey>`.
- Формат: `Content-Type: application/json; charset=utf-8`.
- Кодировка: UTF-8.
- Даты: ISO 8601 (`2026-11-23T17:34:56.560Z` или с offset).
- Хранение: ключи сохраняются в SQLite (`api_keys`) и кэшируются в памяти; после успешной операции кэш обновляется.

## Модель ключа

`ApiKeyItem`:

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `key` | string | да | Значение API-ключа, передаваемое клиентом в `X-API-Key` |
| `clientId` | string | да | Идентификатор/имя клиента-владельца ключа |
| `createdAt` | datetime | да | Дата создания/регистрации ключа |

Пример:
```json
{
  "key": "client-key-001",
  "clientId": "client-001",
  "createdAt": "2026-11-23T17:34:56.560Z"
}
```

## Endpoint

`/api/internal/v1/api-keys` (`X-Internal-Secret` обязателен)

### 1. Получить список ключей

`GET /api/internal/v1/api-keys`

Ответ `200 OK`:
```json
{
  "data": [
    {
      "key": "client-key-001",
      "clientId": "client-001",
      "createdAt": "2026-11-23T17:34:56.560Z"
    }
  ],
  "total": 1
}
```

### 2. Добавить или обновить ключи (upsert)

`POST /api/internal/v1/api-keys`

Тело: JSON-массив `ApiKeyItem[]`. Если `key` уже существует, запись обновляется (`clientId`, `createdAt`).

```json
[
  {
    "key": "client-key-001",
    "clientId": "client-001",
    "createdAt": "2026-11-23T17:34:56.560Z"
  },
  {
    "key": "client-key-002",
    "clientId": "client-002",
    "createdAt": "2026-11-23T17:35:00.000Z"
  }
]
```

Ответ `200 OK`:
```json
{ "success": true, "count": 2, "timestamp": "2026-11-23T17:40:15.056611+00:00" }
```

### 3. Полностью заменить список ключей

`PUT /api/internal/v1/api-keys`

Тело: JSON-массив `ApiKeyItem[]`. Все существующие ключи удаляются, записывается переданный набор. Пустой массив `[]` очищает все ключи.

```json
[
  {
    "key": "client-key-001",
    "clientId": "client-001",
    "createdAt": "2026-11-23T17:34:56.560Z"
  }
]
```

Ответ `200 OK`:
```json
{ "success": true, "count": 1, "timestamp": "2026-11-23T17:40:15.056611+00:00" }
```

### 4. Удалить один ключ

`DELETE /api/internal/v1/api-keys`

Тело:
```json
{ "key": "client-key-001" }
```

Ответ `200 OK`:
```json
{ "success": true, "count": 1, "timestamp": "2026-11-23T17:40:15.056611+00:00" }
```

Если ключ не найден — `404 Not Found`:
```json
{ "error": "API key not found" }
```

## Ошибки

- `400 Bad Request` — пустой payload для `POST`, `null`/невалидный payload, отсутствующий `key` для `DELETE`.
- `401 Unauthorized` — отсутствующий/неверный `X-Internal-Secret`.
- `404 Not Found` — ключ для удаления не найден.
- Формат ошибок: `{ "error": "...", "details": "..." }` (`details` опционально).

## Примеры curl

```bash
# Список ключей
curl -X GET "http://localhost:5000/api/internal/v1/api-keys" \
  -H "X-Internal-Secret: <secret>"

# Добавить/обновить
curl -X POST "http://localhost:5000/api/internal/v1/api-keys" \
  -H "Content-Type: application/json" \
  -H "X-Internal-Secret: <secret>" \
  -d '[{"key":"client-key-001","clientId":"client-001","createdAt":"2026-11-23T17:34:56.560Z"}]'

# Полная замена
curl -X PUT "http://localhost:5000/api/internal/v1/api-keys" \
  -H "Content-Type: application/json" \
  -H "X-Internal-Secret: <secret>" \
  -d '[{"key":"client-key-001","clientId":"client-001","createdAt":"2026-11-23T17:34:56.560Z"}]'

# Удаление
curl -X DELETE "http://localhost:5000/api/internal/v1/api-keys" \
  -H "Content-Type: application/json" \
  -H "X-Internal-Secret: <secret>" \
  -d '{"key":"client-key-001"}'
```

## Замечания по интеграции 1С

- Используйте `POST` для добавления/обновления отдельных ключей без затрагивания остальных.
- Используйте `PUT`, когда 1С является источником истины и нужно синхронизировать полный список.
- Используйте `DELETE` для отзыва одного ключа.
- После изменения ключа клиент должен вызывать `/clientApi/*` с заголовком `X-API-Key: <key>`.
- Не логируйте `X-Internal-Secret` и значения API-ключей в открытом виде.
