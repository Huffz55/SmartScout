using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SmartScout.Application.DTOs;

public class BalldontliePlayerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("height")]
    public string Height { get; set; } = string.Empty;

    [JsonPropertyName("weight_pounds")]
    public string Weight { get; set; } = string.Empty;

    [JsonPropertyName("team")]
    public BalldontlieTeamDto? Team { get; set; }
}

public class BalldontlieTeamDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;
}

public class BalldontlieMetaDto
{
    [JsonPropertyName("next_cursor")]
    public int? NextCursor { get; set; }
}

public class BalldontlieResponseDto
{
    [JsonPropertyName("data")]
    public List<BalldontliePlayerDto> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public BalldontlieMetaDto? Meta { get; set; }
}
