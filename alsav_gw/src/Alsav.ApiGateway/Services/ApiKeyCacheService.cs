using System.Collections.Immutable;
using System.Text.Json;
using Alsav.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Services;

/// <summary>
/// Фоновая служба синхронизации и кэширования API-ключей клиентов.
/// </summary>
/// <remarks>
/// Ключи загружаются из HTTP-сервиса 1С при старте и далее с заданным интервалом.
/// При ошибке синхронизации используется предыдущий успешный снимок ключей.
/// </remarks>
public class ApiKeyCacheService : BackgroundService
{
    /// <summary>
    /// Имя именованного <see cref="HttpClient"/>, используемого для запросов в 1С.
    /// </summary>
    public const string HttpClientName = "OneCApiKeys";

    private const string InternalSecretHeaderName = "X-Internal-Secret";
    private const string DefaultKeysEndpointPath = "/hs/ClientAPI/keys";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OneCSettings _settings;
    private readonly ILogger<ApiKeyCacheService> _logger;

    private ImmutableHashSet<string> _keys = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private long _lastSuccessfulSyncTicks;

    /// <summary>
    /// Создаёт экземпляр службы.
    /// </summary>
    /// <param name="httpClientFactory">Фабрика HTTP-клиентов.</param>
    /// <param name="settings">Настройки интеграции с 1С.</param>
    /// <param name="logger">Логгер.</param>
    public ApiKeyCacheService(
        IHttpClientFactory httpClientFactory,
        IOptions<OneCSettings> settings,
        ILogger<ApiKeyCacheService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Количество ключей в текущем кэше.
    /// </summary>
    public int KeyCount => Volatile.Read(ref _keys).Count;

    /// <summary>
    /// Возвращает время последней успешной синхронизации ключей.
    /// </summary>
    /// <returns>Время в UTC или <see langword="null"/>, если синхронизация ещё не выполнялась успешно.</returns>
    public DateTimeOffset? GetLastSuccessfulSync()
    {
        var ticks = Interlocked.Read(ref _lastSuccessfulSyncTicks);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// Проверяет, является ли API-ключ действующим.
    /// </summary>
    /// <param name="key">Значение заголовка <c>X-API-Key</c>.</param>
    /// <returns><see langword="true"/>, если ключ присутствует в кэше.</returns>
    public virtual bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return Volatile.Read(ref _keys).Contains(key);
    }

    /// <summary>
    /// Выполняет одну синхронизацию API-ключей.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если кэш был успешно обновлён.</returns>
    public async Task<bool> RefreshKeysAsync(CancellationToken cancellationToken = default)
    {
        var url = BuildKeysUrl();

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(InternalSecretHeaderName, _settings.InternalSecret);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to refresh API keys from {Url}. Status code: {StatusCode}. Keeping previous cache ({Count} keys).",
                    url,
                    (int)response.StatusCode,
                    KeyCount);
                return false;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var keys = await JsonSerializer
                .DeserializeAsync<string[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (keys is null)
            {
                _logger.LogWarning(
                    "API keys response from {Url} is null. Keeping previous cache ({Count} keys).",
                    url,
                    KeyCount);
                return false;
            }

            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                builder.Add(key.Trim());
            }

            var newKeys = builder.ToImmutable();
            Volatile.Write(ref _keys, newKeys);
            Interlocked.Exchange(ref _lastSuccessfulSyncTicks, DateTime.UtcNow.Ticks);

            _logger.LogInformation(
                "API keys cache refreshed from {Url}. Keys loaded: {Count}.",
                url,
                newKeys.Count);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while refreshing API keys from {Url}. Keeping previous cache ({Count} keys).",
                url,
                KeyCount);
            return false;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("API key cache service started. Initial synchronization.");

        await RefreshKeysSafelyAsync(stoppingToken).ConfigureAwait(false);

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.SyncIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshKeysSafelyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка приложения.
        }

        _logger.LogInformation("API key cache service stopped.");
    }

    private async Task RefreshKeysSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshKeysAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Штатная остановка приложения.
        }
    }

    private string BuildKeysUrl()
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var path = _settings.KeysEndpointPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultKeysEndpointPath;
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return baseUrl + path;
    }
}
