# ALSAV API Gateway

API Gateway на .NET 8 для приёма данных через внутренние endpoint’ы, кэширования остатков и отдачи клиентского API.

## Что делает сервис

1. **Внешний API** — принимает запросы клиентов по `/clientApi/*`, проверяет `X-API-Key` и отдаёт остатки из локального кэша.
2. **Внутренний API синхронизации** — принимает данные из внешней системы через `/api/internal/v1/*` по заголовку `X-Internal-Secret`: API-ключи, готовые остатки, сырые данные для расчёта.
3. **In-memory / SQLite** — остатки хранятся in-memory; API-ключи и сырые данные для расчёта — в SQLite.

## Архитектура

```text
┌──────────────────────────────────────────────────────────────┐
│                        ALSAV API Gateway                     │
│                                                              │
│  /clientApi/stocks              [X-API-Key]               │
│            │                                                 │
│            ▼                                                 │
│      Stock cache / calculation                             │
│                                                              │
│  /api/internal/v1/api-keys        [X-Internal-Secret]       │
│  /api/internal/v1/sync/stocks   [X-Internal-Secret]       │
│  /api/internal/v1/sync/*          [X-Internal-Secret]       │
│            │                                                 │
│            ▼                                                 │
│      SQLite (api_keys, raw data)                             │
└──────────────────────────────────────────────────────────────┘
```

## Структура проекта

```text
alsav_gw/
├── src/
│   ├── Alsav.ApiGateway/
│   │   ├── Configuration/
│   │   │   ├── StockCalculationSettings.cs
│   │   │   └── InternalApiSettings.cs
│   │   ├── Controllers/
│   │   │   ├── External/
│   │   │   │   └── StocksController.cs
│   │   │   ├── Internal/
│   │   │   │   ├── ApiKeysSyncController.cs
│   │   │   │   ├── RawDataSyncController.cs
│   │   │   │   └── SyncController.cs
│   │   │   └── HealthController.cs
│   │   ├── Data/
│   │   ├── Middleware/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── appsettings.json
│   │   ├── Dockerfile
│   │   └── Program.cs
│   └── Alsav.ApiGateway.Tests/
├── docs/
│   └── 1c-raw-sync-integration.md
├── docker-compose.yml
├── Alsav.ApiGateway.sln
└── README.md
```

## Быстрый старт

### Docker Compose

```bash
cd alsav_gw
docker compose up -d --build
```

После запуска:

- Gateway: `http://localhost:5000`
- Swagger UI: `http://localhost:5000/swagger`
- Внешний Swagger UI: `http://localhost:5000/clientApi/swagger`
- Health: `http://localhost:5000/health`

### Локальный запуск

```bash
cd alsav_gw
dotnet restore
dotnet run --project src/Alsav.ApiGateway/Alsav.ApiGateway.csproj
```

## Конфигурация

| Параметр | Переменная окружения | Назначение |
|---|---|---|
| `InternalApi:SecretKey` | `InternalApi__SecretKey` | Секрет для внутренних endpoint’ов (`X-Internal-Secret`) |
| `ConnectionStrings:GatewayDb` | `ConnectionStrings__GatewayDb` | SQLite БД (`api_keys`, сырые данные) |
| `StockCalculation:DefaultWarehouse` | `StockCalculation__DefaultWarehouse` | Склад по умолчанию для расчёта |

Для локальной отладки можно создать `appsettings.Development.private.json` (игнорируется git).

## API

### Внешние endpoint’ы (`X-API-Key`)

#### `GET /clientApi/stocks?article=...&barcode=...&name=...&limit=...&offset=0`
Возвращает остатки. Если есть сырые срезы/обороты в SQLite (`stock_snapshots` + `stock_movements`), выполняется расчёт; иначе возвращаются заранее подготовленные остатки из `StockCacheService`.

### Trace id и логирование

Каждый ответ gateway содержит заголовки `X-Trace-Id` и `trace_id` с идентификатором запроса. Этот же идентификатор пишется в логи Serilog и дублируется в поле `traceId` ошибочных ответов.

Клиент может передать свой `X-Trace-Id` (строку до 128 символов из букв, цифр, `-`, `_`, `.`, `:`). Если заголовок отсутствует или некорректен, gateway создаёт новый trace id. Заголовки документированы в Swagger для всех операций.

Поиск в логах Docker:

```bash
docker logs alsav-api-gateway 2>&1 | grep "<trace_id>"
```

### Внутренние endpoint’ы (`X-Internal-Secret`)

#### `POST /api/internal/v1/sync/stocks`
Принимает полный снимок готовых остатков и атомарно заменяет in-memory кэш.

```json
[
  {
    "series": "",
    "barcode": "4600000000010",
    "article": "ALS-001",
    "unit": "шт",
    "name": "Товар 1",
    "characteristic": "",
    "price": 1250.50,
    "type": "Готовая продукция",
    "stock": 15
  }
]
```

#### `GET /api/internal/v1/api-keys`
Возвращает список API-ключей.

#### `POST /api/internal/v1/api-keys`
Добавляет/обновляет ключи (upsert).

```json
[
  { "key": "client-key", "clientId": "client-001", "createdAt": "2026-11-22T00:00:00Z" }
]
```

#### `PUT /api/internal/v1/api-keys`
Полностью заменяет список ключей.

#### `DELETE /api/internal/v1/api-keys`
Удаляет ключ: `{ "key": "client-key" }`.

#### Raw sync для расчёта
- `POST /api/internal/v1/sync/product-compositions`
- `POST /api/internal/v1/sync/stock-snapshot`
- `POST /api/internal/v1/sync/stock-movements`

Подробности: [docs/1c-raw-sync-integration.md](docs/1c-raw-sync-integration.md).

## Health

`GET /health` — проверка живости и базовая статистика кэша.

## Тесты

```bash
cd alsav_gw
dotnet test Alsav.ApiGateway.sln
```



