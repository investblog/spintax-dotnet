using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spintax.Corpus
{
    /// <summary>
    /// One fixture, as the corpus schema defines it (<c>schema/fixture.schema.json</c>, README
    /// "Case shape"). <see cref="Expect"/> and <see cref="Rng"/> stay raw: their shape is
    /// discriminated by <see cref="Op"/> and by strategy, and the checks read them directly.
    /// </summary>
    public sealed class CorpusCase
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("op")] public string Op { get; set; } = "";
        [JsonPropertyName("template")] public string Template { get; set; } = "";
        [JsonPropertyName("context")] public Dictionary<string, string>? Context { get; set; }
        [JsonPropertyName("locale")] public string? Locale { get; set; }
        [JsonPropertyName("knownIncludes")] public List<string>? KnownIncludes { get; set; }
        [JsonPropertyName("neutralizeContext")] public List<string>? NeutralizeContext { get; set; }
        [JsonPropertyName("seed")] public JsonElement? Seed { get; set; }
        [JsonPropertyName("postProcess")] public bool? PostProcess { get; set; }
        [JsonPropertyName("rng")] public JsonElement? Rng { get; set; }
        [JsonPropertyName("engines")] public List<string>? Engines { get; set; }
        [JsonPropertyName("expect")] public JsonElement Expect { get; set; }

        /// <summary>The fixture file this case came from; set by the loader, not the JSON.</summary>
        [JsonIgnore] public string File { get; set; } = "";

        /// <summary>
        /// A number seed becomes its decimal text: the engine takes string seeds only, and
        /// <c>kind:rng</c> reproducibility is within-engine, so the mapping is free to choose.
        /// </summary>
        public string? SeedText =>
            Seed is null ? null
            : Seed.Value.ValueKind == JsonValueKind.String ? Seed.Value.GetString()
            : Seed.Value.GetRawText();
    }
}
