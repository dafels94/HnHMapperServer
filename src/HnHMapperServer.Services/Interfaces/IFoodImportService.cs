using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Services.Interfaces;

public interface IFoodImportService
{
    /// <summary>
    /// Import foods from a JSON file in the external format
    /// </summary>
    /// <param name="jsonFilePath">Path to the food-info2.json file</param>
    /// <returns>Import result with statistics</returns>
    Task<FoodImportResult> ImportFromJsonFileAsync(string jsonFilePath);

    /// <summary>
    /// Import foods from JSON content string
    /// </summary>
    /// <param name="jsonContent">JSON content as string</param>
    /// <returns>Import result with statistics</returns>
    Task<FoodImportResult> ImportFromJsonContentAsync(string jsonContent);
}
