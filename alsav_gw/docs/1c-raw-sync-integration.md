# ALSAV Gateway: интеграция 1С (сырые данные для расчёта остатков)

Документ описывает новые внутренние endpoint’ы `alsav_gw` для загрузки **сырых** данных из 1С. Существующие endpoint’ы не меняются:
- `POST /api/internal/v1/sync/balances` — как раньше (готовые остатки в in-memory кэш)
- `GET /clientApi/balances` — как раньше (отдаёт готовые остатки из in-memory кэша)

Новые endpoint’ы используются для расчёта “остатков для клиента” по формуле 1С: **возможность собрать** = минимум по компонентам `FLOOR((ДоступноКомпонента - ВЗаказеОплаченоНеОбеспечено) / КоличествоНаЕдиницу)`. Результат отдаётся новым внешним методом `GET /clientApi/balances/calculated` (контракт ответа тот же `BalancesResponse`).

## Общие правила

- Базовый URL: внутренний адрес gateway (не публиковать наружу).
- Аутентификация: заголовок `X-Internal-Secret: <InternalApi:SecretKey>`.
- Формат: `Content-Type: application/json; charset=utf-8`.
- Кодировка: UTF-8.
- Даты: ISO 8601 (`2026-11-21T10:00:00Z` или с offset).
- Числа: decimal, разделитель `.` в JSON.
- Стратегия загрузки: **полная замена** данных в разрезе `warehouse` (склада). При загрузке среза/движений/состава по складу старые записи этого склада удаляются и записываются новые.
- Рекомендуемый порядок загрузки: 1) `product-compositions`, 2) `stock-snapshot`, 3) `stock-movements`.

## 1. Загрузка состава изделий

`POST /api/internal/v1/sync/product-compositions`

Источник в 1С: `РегистрСведений.алсав_СоставИзделий` (+ при необходимости строки “одна упаковка” как самосостав).

Тело: JSON-массив объектов:

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `productArticle` | string | да | Артикул продукта (собираемого изделия) |
| `productCharacteristic` | string | нет | Характеристика продукта |
| `productSeries` | string | нет | Серия продукта (если используется) |
| `componentArticle` | string | да | Артикул компонента/материала |
| `componentCharacteristic` | string | нет | Характеристика компонента |
| `componentSeries` | string | нет | Серия компонента |
| `warehouse` | string | нет* | Склад, для которого действует состав. Рекомендуется передавать явно: `"Склад адресного хранения"` |
| `quantityPerUnit` | decimal | да | Количество компонента на 1 единицу продукта (`> 0`) |
| `isMaterial` | bool | нет | Признак материала |
| `isSemiFinished` | bool | нет | Признак полуфабриката |

Пример:
```json
[
  {
    "productArticle": "PRD-001",
    "productCharacteristic": "",
    "productSeries": "",
    "componentArticle": "CMP-010",
    "componentCharacteristic": "",
    "componentSeries": "",
    "warehouse": "Склад адресного хранения",
    "quantityPerUnit": 2,
    "isMaterial": false,
    "isSemiFinished": false
  }
]
```

Ответ `200 OK`:
```json
{ "success": true, "count": 1, "timestamp": "2026-11-21T10:00:00+00:00" }
```

## 2. Загрузка среза остатков

`POST /api/internal/v1/sync/stock-snapshot`

Срез — состояние на дату/время `snapshotDate`. При загрузке среза по складу также очищаются движения этого склада (чтобы не накладывать старые дельты на новый срез).

Источник в 1С: остатки/резервы/потребность, соответствующие формуле. Минимально нужны поля, участвующие в расчёте доступности:

`Доступно = InStock - ReservedInWarehouse - ReservedForOrder`

`ВозможностьСобрать` ограничивается `PaidUnsecuredDemand` (аналог `ВЗаказеОплаченоНеОбеспечено`).

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `snapshotDate` | datetime | да | Дата/время среза |
| `warehouse` | string | нет* | Склад. Рекомендуется `"Склад адресного хранения"` |
| `article` | string | да | Артикул номенклатуры |
| `characteristic` | string | нет | Характеристика |
| `series` | string | нет | Серия |
| `barcode` | string | нет | Штрихкод |
| `name` | string | да | Наименование |
| `unit` | string | нет | Единица измерения |
| `type` | string | нет | Вид номенклатуры |
| `price` | decimal | нет | Отпускная цена |
| `inStock` | decimal | да | В наличии (остаток) |
| `reservedInWarehouse` | decimal | да | В резерве со склада |
| `reservedForOrder` | decimal | да | В резерве под заказ |
| `paidUnsecuredDemand` | decimal | да | В заказе оплачено, не обеспечено (потребность, уменьшающая возможность собрать) |

Пример:
```json
[
  {
    "snapshotDate": "2026-11-21T08:00:00Z",
    "warehouse": "Склад адресного хранения",
    "article": "CMP-010",
    "characteristic": "",
    "series": "",
    "barcode": "4600000000010",
    "name": "Компонент 010",
    "unit": "шт",
    "type": "Товар",
    "price": 100.00,
    "inStock": 10,
    "reservedInWarehouse": 1,
    "reservedForOrder": 2,
    "paidUnsecuredDemand": 0
  }
]
```

## 3. Загрузка движений со среза

`POST /api/internal/v1/sync/stock-movements`

Движения — сырые дельты с момента `snapshotDate` до текущего момента (или до момента выгрузки). Gateway суммирует дельты по ключу `warehouse+article+characteristic+series`.

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `period` | datetime | да | Дата/время движения/регистрации |
| `warehouse` | string | нет* | Склад |
| `article` | string | да | Артикул |
| `characteristic` | string | нет | Характеристика |
| `series` | string | нет | Серия |
| `inStockDelta` | decimal | да | Дельта наличия (`+приход`, `-расход`) |
| `reservedInWarehouseDelta` | decimal | да | Дельта резерва со склада |
| `reservedForOrderDelta` | decimal | да | Дельта резерва под заказ |
| `paidUnsecuredDemandDelta` | decimal | да | Дельта оплаченной необеспеченной потребности |

Пример:
```json
[
  {
    "period": "2026-11-21T09:30:00Z",
    "warehouse": "Склад адресного хранения",
    "article": "CMP-010",
    "characteristic": "",
    "series": "",
    "inStockDelta": -2,
    "reservedInWarehouseDelta": 0,
    "reservedForOrderDelta": -1,
    "paidUnsecuredDemandDelta": 0
  }
]
```

## 4. Проверка результата

Внешний endpoint (для клиентов, требует `X-API-Key` как обычный `/clientApi/*`):

`GET /clientApi/balances/calculated?warehouse=Склад%20адресного%20хранения&limit=1000&offset=0`

Ответ — тот же контракт, что и `GET /clientApi/balances`:
```json
{
  "data": [
    {
      "series": "",
      "barcode": "4600000000010",
      "article": "PRD-001",
      "unit": "шт",
      "name": "Изделие 001",
      "characteristic": "",
      "price": 1000.0,
      "type": "Товар",
      "balance": 3
    }
  ],
  "total": 1,
  "lastUpdate": "2026-11-21T10:00:00+00:00"
}
```

`balance` = рассчитанная **возможность собрать** (не физический остаток склада).

## 5. Ошибки

- `400 Bad Request` — пустой массив, невалидный payload/model binding.
- `401 Unauthorized` — отсутствует/неверный `X-Internal-Secret`.
- Ошибка формата: `{ "error": "...", "details": "..." }`.

## 6. Замечания для реализации в 1С

- В расширении `ext_alsav` для загрузки используется общий модуль `алсав_СинхронизацияОстатков`; точка входа — `ЗагрузитьВсеДанные()` (можно вызвать из регламентного задания).
- Адрес шлюза и внутренний секрет хранятся в `РегистрСведений.алсав_КлючиСекретов` под ключами `secret_alsav_gw_base_url` и `secret_alsav_gw_internal_secret`; оба значения можно передать явными параметрами в методы загрузки.
- Передавать `warehouse` единообразно, желательно тем же наименованием, что в 1С запросе: `Склад адресного хранения`.
- `article+characteristic+series` должны совпадать между срезом, движениями и составом.
- Для товаров без состава (или “одна упаковка”) можно не передавать строки состава: gateway посчитает возможность собрать по самому товару.
- Если по складу приходит несколько партий с одним ключом, gateway суммирует/перезаписывает по загруженному набору; предпочтительно передавать уже агрегированные значения по ключу.
- Существующую выгрузку готовых остатков в `/api/internal/v1/sync/balances` не отключать до завершения перехода.
