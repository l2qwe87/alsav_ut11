# LSP Сервер для 1С BSL

## Обзор

Данный каталог содержит файлы для сборки и запуска LSP сервера для проверки синтаксиса 1С BSL файлов в Docker контейнере.

## Файлы

### `Dockerfile`
Dockerfile для создания образа LSP сервера на основе:

- **Базовый образ:** `eclipse-temurin:17-jre-alpine`
- **BSL Language Server:** версия `v0.24.2` от 1c-syntax
- **Порт:** 9090 (WebSocket)
- **Рабочая директория:** `/app`

**Основные компоненты:**
- Установка необходимых утилит (curl, wget, ca-certificates, bash)
- Скачивание BSL Language Server JAR файла
- Настройка окружения и переменных
- Создание временной директории для отчетов

### `run.cmd`
Командный файл для запуска Docker контейнера:

```cmd
docker run --rm -d --name mcp-bsl-server-checker -p 9080:9080 -v 'Z:\src\.conf_1c:/workspaces/.conf_1c:ro' -v 'Z:\src\alsav\alsav_ut11:/workspaces/alsav_ut11:ro' mcp-bsl-server:latest
```

**Параметры запуска:**
- `--rm` - удалять контейнер после остановки
- `-d` - запуск в фоновом режиме
- `--name` - имя контейнера
- `-p 9080:9080` - проброс портов (хост:контейнер)
- `-v` - монтирование директорий в режиме только для чтения

## Использование

### 1. Сборка образа

```bash
docker build -t mcp-bsl-server:latest .
```

### 2. Запуск контейнера

```cmd
run.cmd
```

### 3. Проверка работы

После запуска LSP сервер будет доступен по адресу:
- WebSocket: `ws://localhost:9090/lsp`

## Переменные окружения

| Переменная | Значение по умолчанию | Описание |
|-----------|---------------------|----------|
| `WEB_UI_PORT` | 9090 | Порт для веб-интерфейса |
| `MCP_TRANSPORT` | http | Тип транспорта MCP |
| `MCP_PORT` | 9090 | Порт MCP сервера |
| `LOGGING_ENABLED` | true | Включить логирование |
| `BSL_JAR_PATH` | /opt/bsl/bsl-language-server.jar | Путь к JAR файлу |
| `BSL_MAX_HEAP` | 4g | Максимальный размер heap |
| `SPRING_PROFILES_ACTIVE` | default | Активный профиль Spring |
| `MOUNT_HOST_ROOT` | /workspaces | Корневая директория монтирования |

## Монтируемые директории

- `Z:\src\.conf_1c` → `/workspaces/.conf_1c` (только для чтения)
- `Z:\src\alsav\alsav_ut11` → `/workspaces/alsav_ut11` (только для чтения)

## Интеграция с проектом

LSP сервер используется MCP инструментом `1c-syntax-checker` для:
- Проверки синтаксиса BSL файлов
- Анализа качества кода
- Предоставления рекомендаций по улучшению
- Интеграции с IDE через opencode

## Требования

- Docker
- Доступ к GitHub для скачивания BSL Language Server
- Права на монтирование локальных директорий

## Примеры использования WebSocket

### Файлы примеров
- `lsp.init.txt` - Пример инициализации LSP сессии
- `lsp.req.txt` - Пример запроса на проверку синтаксиса файла

### Процесс работы с LSP

1. **Инициализация сессии** (`lsp.init.txt`):
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "rootUri": "file:///workspaces/alsav_ut11",
    "rootPath": "/workspaces/alsav_ut11",
    "workspaceFolders": [
      {
        "uri": "file:///workspaces/alsav_ut11",
        "name": "alsav_ut11"
      }
    ]
  }
}
```

2. **Открытие документа и проверка синтаксиса** (`lsp.req.txt`):
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "textDocument/didOpen",
  "params": {
    "textDocument": {
      "uri": "file:///workspaces/alsav_ut11/ext_b24/CommonModules/алсавб24_Http/Ext/Module.bsl",
      "languageId": "bsl",
      "version": 1,
      "text": "Функция ПолучитьСекретнуюСсылку()\n\tВозврат алсав_ВыгрузкаCsvJson.ПолучитьСекрет(\"secret_alsav_b24_webhook_url\");\nКонецФункции\n\n..."
    }
  }
}
```

3. **Получение результатов диагностики**
LSP сервер автоматически отправляет уведомление `textDocument/publishDiagnostics` с результатами проверки синтаксиса.

### Интеграция с MCP инструментом

MCP инструмент `1c-syntax-checker` использует этот протокол для:
- Установки соединения с LSP сервером
- Отправки содержимого BSL файлов на проверку
- Получения и форматирования результатов диагностики
- Предоставления результатов пользователю в удобном виде

## Примечания

- Контейнер работает в режиме WebSocket на порту 9090
- Все директории монтируются в режиме только для чтения для безопасности
- Используется JRE 17 для совместимости с BSL Language Server
- Логирование включено по умолчанию для отладки
- Примеры в `lsp.init.txt` и `lsp.req.txt` демонстрируют реальный протокол обмена с LSP сервером