using System.Text.Json.Serialization;

namespace HnHMapperServer.Core.DTOs;

/// <summary>
/// External JSON food item structure from food-info2.json
/// </summary>
public class JsonFoodItem
{
    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonPropertyName("hunger")]
    public decimal Hunger { get; set; }

    [JsonPropertyName("energy")]
    public decimal Energy { get; set; }

    [JsonPropertyName("feps")]
    public List<JsonFep> Feps { get; set; } = new();

    [JsonPropertyName("ingredients")]
    public List<JsonIngredient> Ingredients { get; set; } = new();
}

public class JsonFep
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}

public class JsonIngredient
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }
}

/// <summary>
/// Result of food import operation
/// </summary>
public class FoodImportResult
{
    public int TotalItems { get; set; }
    public int ItemsImported { get; set; }
    public int ItemsSkipped { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public DateTime CompletedAt { get; set; }
}
