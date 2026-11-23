using System.Text.Json;
using System.Text.Json.Serialization;
using Alsav.ApiGateway.Models;
using Microsoft.Extensions.Options;

namespace Alsav.ApiGateway.Services;

public sealed class BalanceSeedOptions
{
    public const string SectionName = "BalanceSeed";
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "https://alsav.ru/csv_alsav/ostatki_alsav_price.json";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class BalanceSeedService(
    IHttpClientFactory httpClientFactory,
    BalanceCacheService balanceCache,
    IOptions<BalanceSeedOptions> options,
    ILogger<BalanceSeedService> logger) : IHostedService
{
    public const string HttpClientName = "balance-seed";
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly BalanceCacheService _balanceCache = balanceCache;
    private readonly BalanceSeedOptions _options = options.Value;
    private readonly ILogger<BalanceSeedService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogInformation("Balance seed disabled.");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 300));
            using var stream = await client.GetStreamAsync(_options.Url, cancellationToken);
            var items = await JsonSerializer.DeserializeAsync<List<SeedItem>>(stream, JsonOptions, cancellationToken) ?? [];
            var mapped = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Article) && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new BalanceItem
                {
                    Series = x.Series ?? string.Empty,
                    Barcode = x.Barcode ?? string.Empty,
                    Article = x.Article!.Trim(),
                    Unit = x.Unit ?? string.Empty,
                    Name = x.Name!.Trim(),
                    Characteristic = x.Characteristic ?? string.Empty,
                    Price = x.Price,
                    Type = x.Type ?? string.Empty,
                    Balance = x.Balance
                })
                .ToList();

            _balanceCache.UpdateBalances(mapped);
            _logger.LogInformation("Balance cache seeded from {Url}. Items: {Count}.", _options.Url, mapped.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed balance cache from {Url}.", _options.Url);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class SeedItem
    {
        [JsonPropertyName("Серия")] public string? Series { get; set; }
        [JsonPropertyName("ВидНоменклатуры")] public string? Type { get; set; }
        [JsonPropertyName("Артикул")] public string? Article { get; set; }
        [JsonPropertyName("Номенклатура")] public string? Name { get; set; }
        [JsonPropertyName("Характеристика")] public string? Characteristic { get; set; }
        [JsonPropertyName("Штрихкод")] public string? Barcode { get; set; }
        [JsonPropertyName("ЕдиницаИзмерения")] public string? Unit { get; set; }
        [JsonPropertyName("Остатки")] public decimal Balance { get; set; }
        [JsonPropertyName("ОтпускнаяЦена")] public decimal Price { get; set; }
    }
}
