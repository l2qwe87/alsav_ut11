using System.Collections.Immutable;
using Alsav.ApiGateway.Models;

namespace Alsav.ApiGateway.Services;

/// <summary>
/// Потокобезопасный in-process кэш актуальных остатков номенклатуры.
/// </summary>
/// <remarks>
/// Чтение выполняется без блокировок: состояние кэша хранится в неизменяемом
/// <see cref="ImmutableDictionary{TKey,TValue}"/> и заменяется одной атомарной
/// операцией записи ссылки.
/// </remarks>
public sealed class BalanceCacheService
{
    private sealed record CacheState(
        ImmutableDictionary<string, BalanceItem> Items,
        DateTimeOffset LastUpdate);

    private static readonly CacheState EmptyState = new(
        ImmutableDictionary.Create<string, BalanceItem>(StringComparer.Ordinal),
        DateTimeOffset.MinValue);

    private CacheState _state = EmptyState;

    /// <summary>
    /// Количество уникальных позиций в кэше.
    /// </summary>
    public int Count => Volatile.Read(ref _state).Items.Count;

    /// <summary>
    /// Возвращает время последнего обновления кэша.
    /// </summary>
    /// <returns>Время обновления в UTC или <see cref="DateTimeOffset.MinValue"/>, если кэш ещё не заполнялся.</returns>
    public DateTimeOffset GetLastUpdateTime() => Volatile.Read(ref _state).LastUpdate;

    /// <summary>
    /// Атомарно заменяет всё содержимое кэша новым набором позиций.
    /// </summary>
    /// <param name="items">Позиции остатков. Пустая коллекция очищает кэш.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="items"/> равен <see langword="null"/>.</exception>
    public void UpdateBalances(IEnumerable<BalanceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var builder = ImmutableDictionary.CreateBuilder<string, BalanceItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            builder[item.CompositeKey] = item;
        }

        var newState = new CacheState(builder.ToImmutable(), DateTimeOffset.UtcNow);
        Volatile.Write(ref _state, newState);
    }

    /// <summary>
    /// Возвращает снимок всех позиций кэша.
    /// </summary>
    /// <returns>Неизменяемый по содержимому массив позиций.</returns>
    public IReadOnlyCollection<BalanceItem> GetAll()
    {
        return Volatile.Read(ref _state).Items.Values.ToArray();
    }

    /// <summary>
    /// Возвращает позицию по композитному ключу.
    /// </summary>
    /// <param name="compositeKey">Ключ вида «Артикул|Характеристика|Серия».</param>
    /// <returns>Найденная позиция или <see langword="null"/>.</returns>
    public BalanceItem? GetByKey(string compositeKey)
    {
        if (string.IsNullOrEmpty(compositeKey))
        {
            return null;
        }

        return Volatile.Read(ref _state).Items.TryGetValue(compositeKey, out var item)
            ? item
            : null;
    }
}
