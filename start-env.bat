@echo off
:: Force UTF-8 encoding for clean text in Windows console
chcp 65001 >nul
cls
echo ====================================================
echo Запуск BSL Language Server (MCP) для 1С через Docker
echo ====================================================

:: Move to the correct drive and path
Z:
cd "Z:\src\alsav\alsav_ut11"

echo Проверка статуса Docker...
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [ОШИБКА] Docker не запущен! Пожалуйста, запустите Docker Desktop.
    goto end
)

echo Сборка образа (при необходимости) и запуск контейнера...
docker compose up -d --build
if %errorlevel% neq 0 (
    echo.
    echo [ОШИБКА] Не удалось запустить контейнеры. Проверьте ошибку выше.
    goto end
)

echo.
echo Контейнеры успешно запущены!
echo MCP endpoint: http://127.0.0.1:4389/mcp
echo Проверить логи: docker compose logs -f bsl-language-server
echo ====================================================

:end
pause
