namespace Alsav.ApiGateway.Data.Entities;

public sealed class ApiKeyEntity
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
