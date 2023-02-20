using System.Text.Json.Serialization;

namespace Senate.Portal;

public class InviteUserModel
{
    public string Email { get; set; }

    public string Permission { get; set; } = "read";
}

public record InviteModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("expiry")]
    public DateTime Expiry { get; set; }

    [JsonPropertyName("redeemed")]
    public bool Redeemed { get; set; } = false;
}
