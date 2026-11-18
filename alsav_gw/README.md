# Alsav API Gateway

API Gateway на .NET 8 для интеграции внешних клиентов с 1С и кэширования актуальных
остатков номенклатуры в оперативной памяти.

Сервис выполняет две роли:

1. **Внешний API Gateway** — принимает запросы клиентов по `/clientApi/*`, проверяет
   API-ключ и либо отдаёт остатки из локального кэша, либо проксирует запрос в 1С через YARP.
2. **Внутренний сервис синхронизации** — принимает полный снимок остатков из 1С
   и атомарно заменяет in-process кэш.

## Архитектура

```text
Клиент
  │  X-API-Key
  ▼
Публичный Nginx (TLS, rate limiting)
  │
  ▼
Внутренний шлюз
  │
  ▼
┌──────────────────────────────────────────────┐
│ Alsav.ApiGateway (.NET 8)                    │
│                                              │
│  /clientApi/balances  ──► BalancesController │
│                            │                 │
│                            ▼                 │
│                     BalanceCacheService      │
│                     ImmutableDictionary      │
│                                              │
│  /clientApi/*         ──► YARP ──────────────┼──► 1С httpc
│                                              │    /hs/ClientAPI/*
│  /api/internal/v1/sync/balances              │
│       ▲                                      │
│       │ X-Internal-Secret                    │
│  SyncController ──► BalanceCacheService      │
│                                              │
│  ApiKeyCacheService (BackgroundService)      │
│       └── GET /hs/ClientAPI/keys ────────────┼──► 1С httpc
└──────────────────────────────────────────────┘
       ▲
       │ POST полного снимка остатков
       │
  1С (регламентное задание)
```

Ключевые решения:

- кэш остатков — `ImmutableDictionary<string, BalanceItem>` с атомарной заменой ссылки,
  чтение без блокировок;
- кэш API-ключей — `ImmutableHashSet<string>`, обновляется фоновой службой;
- при ошибке обновления ключей используется предыдущий успешный снимок;
- секреты не хранятся в коде, передаются через конфигурацию или переменные окружения.

## Структура решения

```text
.
├── Alsav.ApiGateway.sln
├── src/
│   ├── Alsav.ApiGateway/
│   │   ├── Configuration/
│   │   │   ├── InternalApiSettings.cs
│   │   │   └── OneCSettings.cs
│   │   ├── Controllers/
│   │   │   ├── External/BalancesController.cs
│   │   │   ├── Internal/SyncController.cs
│   │   │   └── HealthController.cs
│   │   ├── Middleware/
│   │   │   ├── ApiKeyValidationMiddleware.cs
│   │   │   └── ExceptionHandlingMiddleware.cs
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs
│   │   │   └── BalanceItem.cs
│   │   ├── Services/
│   │   │   ├── ApiKeyCacheService.cs
│   │   │   └── BalanceCacheService.cs
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Dockerfile
│   │   └── Alsav.ApiGateway.csproj
│   └── Alsav.ApiGateway.Tests/
│       ├── Controllers/SyncControllerTests.cs
│       ├── Middleware/ApiKeyValidationMiddlewareTests.cs
│       ├── Services/ApiKeyCacheServiceTests.cs
│       └── Services/BalanceCacheServiceTests.cs
├── onec-stub/default.conf
├── docker-compose.yml
└── README.md
```

## Быстрый старт

Требования: Docker и Docker Compose.

```bash
docker compose up --build
```

В составе compose запускаются:

- `api-gateway` — приложение на порту `5000`;
- `onec-http` — заглушка 1С на базе `nginx:alpine` (внутренняя сеть `internal_net`).

После старта проверьте health:

```bash
curl http://localhost:5000/health
```

Загрузите тестовый снимок остатков:

```bash
curl -X POST http://localhost:5000/api/internal/v1/sync/balances \
  -H "Content-Type: application/json" \
  -H "X-Internal-Secret: demo-internal-secret" \
  -d '[{"Article":"ART-1","Name":"Тестовый товар","Unit":"шт","Type":"Товар","Price":199.99,"Balance":10,"Barcode":"4600000000001"}]'
```

Получите остатки с демонстрационным API-ключом:

```bash
curl http://localhost:5000/clientApi/balances \
  -H "X-API-Key: demo-api-key-1"
```

Заглушка 1С возвращает ключи `demo-api-key-1` и `demo-api-key-2`.

Остановить сервисы:

```bash
docker compose down
```

## Локальный запуск без Docker

1. Запустите заглушку 1С:

   ```bash
   docker run --rm -p 8080:80 \
     -v "${PWD}/onec-stub/default.conf:/etc/nginx/conf.d/default.conf:ro" \
     nginx:alpine
   ```

2. Запустите приложение в режиме Development:

   ```bash
   dotnet run --project src/Alsav.ApiGateway
   ```

   В Development используются адреса `http://localhost:8080` и секрет
   `dev-internal-secret`.

## API

### `GET /health`

Публичный endpoint для мониторинга. Не требует API-ключа.

Пример ответа:

```json
{
  "status": "Healthy",
  "balancesLastUpdate": "2026-11-09T10:15:30+00:00",
  "balancesCount": 1250,
  "apiKeysLastSync": "2026-11-09T10:15:00+00:00",
  "apiKeysCount": 2
}
```

### `POST /api/internal/v1/sync/balances`

Внутренний endpoint для 1С. Требует заголовок `X-Internal-Secret`.

Тело запроса — непустой JSON-массив `BalanceItem`:

```json
[
  {
    "Series": "SER-1",
    "Barcode": "4600000000001",
    "Article": "ART-1",
    "Unit": "шт",
    "Name": "Тестовый товар",
    "Characteristic": "Красный",
    "Price": 199.99,
    "Type": "Товар",
    "Balance": 10
  }
]
```

Обязательные поля: `Article`, `Name`, `Unit`, `Type`. Остальные поля могут быть пустыми.

Успешный ответ:

```json
{
  "success": true,
  "count": 1,
  "timestamp": "2026-11-09T10:15:30.123+00:00"
}
```

Коды ошибок:

- `401` — отсутствует или неверен `X-Internal-Secret`;
- `400` — пустой массив или не заполнены обязательные поля;
- `500` — внутренняя ошибка.

Пример:

```bash
curl -X POST http://localhost:5000/api/internal/v1/sync/balances \
  -H "Content-Type: application/json" \
  -H "X-Internal-Secret: demo-internal-secret" \
  -d @balances.json
```

### `GET /clientApi/balances`

Внешний endpoint. Требует заголовок `X-API-Key`.

Query-параметры:

| Параметр  | Описание                                             | По умолчанию |
|-----------|------------------------------------------------------|--------------|
| `article` | Поиск по подстроке артикула без учёта регистра       | —            |
| `barcode` | Поиск по подстроке штрихкода без учёта регистра      | —            |
| `name`    | Поиск по подстроке наименования без учёта регистра   | —            |
| `limit`   | Размер страницы, максимум 5000                       | 1000         |
| `offset`  | Смещение от начала выборки                           | 0            |

Пример ответа:

```json
{
  "data": [
    {
      "series": "SER-1",
      "barcode": "4600000000001",
      "article": "ART-1",
      "unit": "шт",
      "name": "Тестовый товар",
      "characteristic": "Красный",
      "price": 199.99,
      "type": "Товар",
      "balance": 10
    }
  ],
  "total": 1,
  "lastUpdate": "2026-11-09T10:15:30.123+00:00"
}
```

Примеры:

```bash
curl "http://localhost:5000/clientApi/balances?limit=10" \
  -H "X-API-Key: demo-api-key-1"

curl "http://localhost:5000/clientApi/balances?article=ART&name=товар" \
  -H "X-API-Key: demo-api-key-1"
```

Коды ошибок:

- `401` — отсутствует заголовок `X-API-Key` или ключ недействителен;
- `500` — внутренняя ошибка.

### `GET /clientApi/*` (YARP)

Любые запросы, начинающиеся с `/clientApi/`, кроме `/clientApi/balances`,
проксируются в 1С через YARP:

```text
/clientApi/{**catch-all}  ->  /hs/ClientAPI/{**catch-all}
```

Заголовок `X-API-Key` проверяется до проксирования и передаётся в 1С.

## Конфигурация

Основные настройки находятся в `src/Alsav.ApiGateway/appsettings.json`.

| Секция / ключ                         | Переменная окружения                                       | Назначение                                              |
|---------------------------------------|------------------------------------------------------------|---------------------------------------------------------|
| `OneCSettings:BaseUrl`                | `OneCSettings__BaseUrl`                                    | Базовый адрес HTTP-сервиса 1С                           |
| `OneCSettings:InternalSecret`         | `OneCSettings__InternalSecret`                             | Секрет для запросов в 1С                                |
| `OneCSettings:SyncIntervalMinutes`    | `OneCSettings__SyncIntervalMinutes`                        | Период синхронизации ключей, минуты                     |
| `OneCSettings:KeysEndpointPath`       | `OneCSettings__KeysEndpointPath`                           | Путь получения API-ключей                               |
| `InternalApi:SecretKey`               | `InternalApi__SecretKey`                                   | Секрет для входящих запросов синхронизации из 1С        |
| `ReverseProxy:Clusters:...:Address`   | `ReverseProxy__Clusters__onecCluster__Destinations__onec__Address` | Адрес 1С для YARP                              |

Для production рекомендуется:

- задавать секреты только через переменные окружения или Docker secrets;
- не коммитить `appsettings.Production.json` (он исключён в `.gitignore`);
- использовать разные значения для `OneCSettings:InternalSecret` и `InternalApi:SecretKey`;
- публиковать сервис наружу только через Nginx с TLS.

Пример production-переменных:

```bash
export ONEC_INTERNAL_SECRET="<длинный случайный секрет>"
export INTERNAL_API_SECRET="<другой длинный случайный секрет>"
docker compose up -d --build
```

В `docker-compose.yml` предусмотрены значения по умолчанию для демонстрационного
запуска. Перед публикацией в production их необходимо заменить.

## Настройка 1С

### 1. HTTP-сервис выдачи API-ключей

Создайте HTTP-сервис с методом `GET /hs/ClientAPI/keys`, который:

- проверяет заголовок `X-Internal-Secret`;
- возвращает JSON-массив действующих ключей.

Пример ответа:

```json
["client-key-1", "client-key-2"]
```

API Gateway запрашивает этот метод при старте и далее каждые
`OneCSettings:SyncIntervalMinutes` минут.

### 2. Регламентное задание отправки остатков

Регламентное задание должно формировать **полный снимок** актуальных остатков
и отправлять его методом `POST`:

```text
POST http://api-gateway/api/internal/v1/sync/balances
Content-Type: application/json
X-Internal-Secret: <InternalApi:SecretKey>
```

Особенности:

- отправляйте полный снимок, а не изменения: кэш полностью заменяется;
- пустой массив запрещён — вернётся `400`;
- поля `Article`, `Name`, `Unit`, `Type` обязательны;
- композитный ключ кэша формируется как `Артикул|Характеристика|Серия`;
- при дублировании ключа в массиве сохраняется последняя позиция;
- при ошибке отправки 1С должна повторить попытку по расписанию.

Пример формирования JSON в 1С (псевдокод):

```text
Запрос = Новый HTTPЗапрос("/api/internal/v1/sync/balances");
Запрос.Заголовки.Вставить("Content-Type", "application/json");
Запрос.Заголовки.Вставить("X-Internal-Secret", Секрет);
Запрос.УстановитьТелоИзСтроки(СериализоватьВJSON(МассивОстатков));
HTTPСоединение.Вызвать("POST", Запрос);
```

### 3. Проксирование остальных методов

Методы 1С, доступные клиентам через шлюз, должны быть реализованы в HTTP-сервисе
`/hs/ClientAPI/*`. YARP передаёт путь и заголовки без изменений.

## Тестирование

```bash
dotnet test
```

Покрыты:

- обновление и атомарность кэша остатков;
- загрузка, валидация и обработка ошибок кэша API-ключей;
- успешная синхронизация, неверный секрет и пустой payload в `SyncController`;
- отсутствие, невалидность и валидность ключа в `ApiKeyValidationMiddleware`.

## Ограничения и важные замечания

- Кэш хранится **в памяти процесса**. При горизонтальном масштабировании каждый
  экземпляр gateway имеет собственный кэш. В этом случае 1С должна отправлять
  снимок остатков в каждый экземпляр, либо следует использовать один экземпляр
  для записи.
- Redis и Entity Framework не используются намеренно.
- Swagger включён только в окружении `Development`.
- При недоступности 1С на старте сервис продолжает работать: кэш ключей остаётся
  пустым до первой успешной синхронизации, все внешние запросы получают `401`.
- Для production необходима настройка TLS и ограничение частоты запросов на
  уровне Nginx.

## Диагностика

| Симптом                                   | Что проверить                                                                 |
|-------------------------------------------|-------------------------------------------------------------------------------|
| `401 Missing X-API-Key header`            | Клиент не передал заголовок `X-API-Key`                                       |
| `401 Invalid or revoked API Key`          | Ключ отсутствует в кэше; проверьте `/health` и HTTP-сервис ключей 1С           |
| `/health` показывает `apiKeysCount: 0`    | Доступность 1С, `OneCSettings:BaseUrl`, `OneCSettings:InternalSecret`          |
| `/health` показывает `balancesCount: 0`   | Регламентное задание 1С ещё не отправило снимок остатков                       |
| `502` при запросе `/clientApi/*`          | Недоступен `onec-http` или неверный адрес в `ReverseProxy`                     |
| `400 Validation failed`                   | В снимке остатков не заполнены обязательные поля                              |
