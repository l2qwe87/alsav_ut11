# ALSAV Gateway: интеграция сырых данных для расчёта остатков

Документ описывает внутренние endpoint’ы `alsav_gw` для загрузки сырых данных. Существующие endpoint’ы не меняются:

- `POST /api/internal/v1/sync/balances` — как раньше (готовые остатки в in-memory кэш)
- `GET /clientApi/balances` — как раньше (отдаёт готовые остатки из in-memory кэша)

Новые endpoint’ы используются для расчёта “остатков для клиента” по формуле: **возможность собрать** = минимум по компонентам `FLOOR((ДоступноКомпонента - ВЗаказеОплаченоНеОбеспечено) / КоличествоНаЕдиницу)`. Результат отдаётся внешним методом `GET /clientApi/balances/calculated` (контракт ответа тот же `BalancesResponse`).

## Общие правила

- Базовый URL: внутренний адрес gateway.
- Аутентификация: заголовок `X-Internal-Secret: <InternalApi:SecretKey>`.
- Формат: `Content-Type: application/json; charset=utf-8`.
- Кодировка: UTF-8.
- Даты: ISO 8601 (`2026-11-21T10:00:00Z` или с offset).
- Числа: decimal, разделитель `.` в JSON.
- Стратегия загрузки: **полная замена** данных в разрезе `warehouse` (склада). При загрузке среза/движений/состава по складу все ранее имеющиеся строки этого склада удаляются и записываются новые.
- Рекомендуемый порядок загрузки: 1) `product-compositions`, 2) `stock-snapshot`, 3) `stock-movements`.

## 1. Загрузка состава изделий

`POST /api/internal/v1/sync/product-compositions`

Тело: JSON-массив объектов:

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `productArticle` | string | да | Артикул товара (собираемое изделие) |
| `productCharacteristic` | string | нет | Характеристика товара |
| `productSeries` | string | нет | Серия товара (если используется) |
| `componentArticle` | string | да | Артикул компонента/материала |
| `componentCharacteristic` | string | нет | Характеристика компонента |
| `componentSeries` | string | нет | Серия компонента |
| `warehouse` | string | нет* | Склад, для которого валиден состав. Рекомендуется передавать явно: `"Склад адресного хранения"` |
| `quantityPerUnit` | decimal | да | Количество компонента на 1 единицу товара (`> 0`) |
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

Срез — состояние на дату/время `snapshotDate`. При загрузке среза по складу также очищаются движения этого склада (чтобы не суммировать старые дельты по новому срезу).

Минимально нужны поля, участвующие в расчёте доступности:

`Доступно = InStock - ReservedInWarehouse - ReservedForOrder`

`ВЗаказеОплаченоНеОбеспечено` хранится как `PaidUnsecuredDemand` (дельта `НеОбеспечено`).

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
| `reservedInWarehouse` | decimal | да | В резерве в складе |
| `reservedForOrder` | decimal | да | В резерве под заказ |
| `paidUnsecuredDemand` | decimal | да | В заказе оплачен, но не обеспечен (потребность, уменьшающая возможность собрать) |

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

## 3. Загрузка движений к срезу

`POST /api/internal/v1/sync/stock-movements`

Движения — это дельты с даты `snapshotDate` по текущий момент (или по момент выгрузки). Gateway суммирует дельты по ключу `warehouse+article+characteristic+series`.

| Поле | Тип | Обяз. | Описание |
|---|---|---:|---|
| `period` | datetime | да | Дата/время движения/регистрации |
| `warehouse` | string | нет* | Склад |
| `article` | string | да | Артикул |
| `characteristic` | string | нет | Характеристика |
| `series` | string | нет | Серия |
| `inStockDelta` | decimal | да | Дельта остатка (`+приход`, `-расход`) |
| `reservedInWarehouseDelta` | decimal | да | Дельта резерва в складе |
| `reservedForOrderDelta` | decimal | да | Дельта резерва под заказ |
| `paidUnsecuredDemandDelta` | decimal | да | Дельта оплаченого необеспеченого потребности |

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

Ответ — тот же формат, что и `GET /clientApi/balances`:

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

- `400 Bad Request` — пустой массив, отсутствующий payload/model binding.
- `401 Unauthorized` — отсутствующий/неверный `X-Internal-Secret`.
- Ошибки формата: `{ "error": "...", "details": "..." }`.

## 6. Замечания по интеграции

- Передавать `warehouse` единообразно; рекомендуется явно: `Склад адресного хранения`.
- `article+characteristic+series` должны совпадать между срезом, движениями и составом.
- Для товаров без состава (или “одна упаковка”) можно не передавать строки состава: gateway считает возможность собрать по самому товару.
- Если на складе уходят несколько партий в одной номенклатуре, gateway суммирует/агрегирует по загруженным данным; предпочтительно передавать уже агрегированные значения по ключу.
- Регулярная загрузка готовых остатков в `/api/internal/v1/sync/balances` не отключена по умолчанию для обратной совместимости.
