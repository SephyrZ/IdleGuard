using System.Text.Json.Serialization;
using IdleGuard.Models;

namespace IdleGuard.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "LanguageDictionary")]
internal partial class AppJsonContext : JsonSerializerContext;
