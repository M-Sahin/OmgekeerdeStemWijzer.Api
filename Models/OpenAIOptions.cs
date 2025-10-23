using System.ComponentModel.DataAnnotations;

namespace OmgekeerdeStemWijzer.Api.Models
{
    public class OpenAIOptions
    {
        [Required]
        public string ApiKey { get; set; } = string.Empty;

        public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    }
}
