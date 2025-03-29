using Newtonsoft.Json;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;
using Shuffull.Shared.Enums;
using Shuffull.Site.Configuration;
using Shuffull.Site.Models;
using Shuffull.Site.Models.Database;
using System.Text.Json;

namespace Shuffull.Site.Tools
{
    public class OpenAIManager
    {
        private readonly IServiceProvider _services;
        private readonly OpenAIConfiguration _config;
        private readonly ShuffullFilesConfiguration _fileConfig;
        private OpenAIAPI _api;
        private Model _model;
        public bool Enabled { get { return _config.Enabled; } }

        public OpenAIManager(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _config = configuration.GetSection(OpenAIConfiguration.OpenAIConfigurationSection).Get<OpenAIConfiguration>();
            _api = new OpenAIAPI(_config.ApiKey);
            _model = new Model(_config.ModelName) { OwnedBy = "openai" };
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>();
        }

        public async Task<TagsResponse> RequestTagsResponse(Song song, List<Artist> artists, List<Tag> allTags)
        {
            var chatRequest = new ChatRequest()
            {
                Model = _model,
                ResponseFormat = ChatRequest.ResponseFormats.JsonObject
            };
            var tagConversation = _api.Chat.CreateConversation(chatRequest);
            var allGenres = allTags.Where(x => x.Type == TagType.Genre).ToList();
            var message = $"{song.Name} ({string.Join(",", artists.Select(x => x.Name))})\nGenres: {string.Join(",", allGenres.Select(x => x.Name))}";
            var resultNames = new List<string>();
            var instruction = File.ReadAllText(_config.InstructionFile);

            tagConversation.AppendUserInput(instruction);
            tagConversation.AppendExampleChatbotOutput("Understood. Send the information.");
            tagConversation.AppendUserInput("Vermilion City (Pokémon Red & Blue Remix) (Mewmore)");
            tagConversation.AppendExampleChatbotOutput("{\"genres\":[\"Electronic\",\"Pokemon\",\"Game\"],\"genresNotProvided\":[\"\"],languages\":[\"Instrumental\"],\"timePeriod\":\"2010s\"}\r\n");
            tagConversation.AppendUserInput(message);

            try
            {
                var responseStr = await tagConversation.GetResponseFromChatbotAsync();
                var result = JsonConvert.DeserializeObject<TagsResponse>(responseStr) ?? throw new Exception();

                File.WriteAllText(Path.Combine(_fileConfig.SavedAiResponsesDirectory, $"{song.FileHash}.json"), responseStr);
                return result;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
