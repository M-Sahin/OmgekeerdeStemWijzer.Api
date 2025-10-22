using System.ComponentModel.DataAnnotations;

namespace OmgekeerdeStemWijzer.Api.Models
{
    public class GroqOptions
    {
        [Required]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

        public string Model { get; set; } = "groq/compound";
    }
}
