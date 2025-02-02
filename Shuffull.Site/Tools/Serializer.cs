using OpenAI_API.Moderation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shuffull.Site.Tools
{
    public class Serializer
    {
        public static readonly JsonNamingPolicy NamingPolicy = new LowerCamelCaseNamingPolicy();

        public static string Serialize(object data)
        {
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = NamingPolicy
            };
            return JsonSerializer.Serialize(data, options);
        }
    }
}
