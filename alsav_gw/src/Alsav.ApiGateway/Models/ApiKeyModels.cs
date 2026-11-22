using System.ComponentModel.DataAnnotations;

namespace Alsav.ApiGateway.Models;

public sealed class ApiKeyItem
{
    [Required] public string Key { get; set; } = string.Empty;
    [Required] public string ClientId { get; set; } = string.Empty;
    [Required] public DateTimeOffset CreatedAt { get; set; }
}

public sealed record ApiKeyDeleteRequest([Required] string Key);

public sealed record ApiKeysResponse(IReadOnlyList<ApiKeyItem> Data, int Total);
