using Newtonsoft.Json;
using OpenAI.Chat;
using OpenAI.Responses;
using Shuffull.Shared.Enums;
using Shuffull.Site.Configuration;
using Shuffull.Site.Models;
using Shuffull.Site.Models.Database;
using Shuffull.Site.Tools.AI;

namespace Shuffull.Site.Tools;

public class OpenAIManager(IConfiguration configuration) : IAIManager
{
    private readonly OpenAIConfiguration _config = configuration.GetSection(OpenAIConfiguration.OpenAIConfigurationSection).Get<OpenAIConfiguration>() ?? throw new Exception("OpenAIConfiguration not set.");
    private readonly ShuffullFilesConfiguration _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("ShuffullFilesConfiguration not set");

    public async Task<GenerateTagsResponse> GenerateTagsAsync(Song song, List<Artist> artists, List<Tag> allGenreTags)
    {
        return _config.ApiEndpoint switch
        {
            OpenAIConfiguration.SupportedApiEndpoints.ChatCompletions => await RequestTagsChatCompletion(song, artists, allGenreTags),
            OpenAIConfiguration.SupportedApiEndpoints.Responses => await RequestTagsResponse(song, artists, allGenreTags),
            _ => throw new NotSupportedException($"OpenAI API endpoint '{_config.ApiEndpoint}' is not supported. Supported endpoints are: {string.Join(", ", OpenAIConfiguration.SupportedApiEndpoints.All)}."),
        };
    }

    public async Task<GenerateTagsResponse> RequestTagsChatCompletion(Song song, List<Artist> artists, List<Tag> allGenreTags)
    {
        var client = new ChatClient(model: _config.ModelName, apiKey: _config.ApiKey);
        var instruction = File.ReadAllText(_config.InstructionFile)
            + $"\nGenres: {string.Join(",", allGenreTags.Select(x => x.Name))}";
        var messages = new List<ChatMessage>()
        {
            new UserChatMessage(instruction),
            new AssistantChatMessage("Understood. Send the information."),
            new UserChatMessage("Vermilion City (Pokémon Red & Blue Remix) (Mewmore)"),
            new AssistantChatMessage("{\"genres\":[\"Electronic\",\"Pokemon\",\"Game\"],\"genresNotProvided\":[\"\"],languages\":[\"Instrumental\"],\"timePeriod\":\"2010s\"}\r\n"),
            new UserChatMessage($"{song.Name} ({string.Join(",", artists.Select(x => x.Name))})"),
        };
        var options = new ChatCompletionOptions()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat
            (
                jsonSchemaFormatName: "tag_response",
                jsonSchema: BinaryData.FromBytes("""
                    {
                        "type": "object",
                        "properties": {
                            "genres": {
                                "type": "array",
                                "items": { "type": "string" }
                            },
                            "genresNotProvided": {
                                "type": "array",
                                "items": { "type": "string" }
                            },
                            "languages": {
                                "type": "array",
                                "items": { "type": "string" }
                            },
                            "timePeriod": { "type": "string" }
                        },
                        "required": ["genres", "genresNotProvided", "languages", "timePeriod"],
                        "additionalProperties": false
                    }
                 """u8.ToArray()),
                jsonSchemaIsStrict: true
            )
        };

        try
        {
            var completion = (await client.CompleteChatAsync(messages, options)).Value;
            var resultStr = completion.Content[0].Text;
            File.WriteAllText(Path.Combine(_fileConfig.SavedAiResponsesDirectory, $"{song.FileHash}.json"), resultStr);
            var result = JsonConvert.DeserializeObject<GenerateTagsResponse>(resultStr) ?? throw new Exception($"Failed to parse {song.FileHash}.json");
            return result;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<GenerateTagsResponse> RequestTagsResponse(Song song, List<Artist> artists, List<Tag> allGenreTags)
    {
        var client = new OpenAIResponseClient(model: _config.ModelName, apiKey: _config.ApiKey);
        var allGenres = allGenreTags.Where(x => x.Type == TagType.Genre).ToList();
        var instruction = File.ReadAllText(_config.InstructionFile)
            + $"\nGenres: {string.Join(",", allGenres.Select(x => x.Name))}";
        var messages = new List<MessageResponseItem>()
        {
            ResponseItem.CreateUserMessageItem(instruction),
            ResponseItem.CreateAssistantMessageItem("Understood. Send the information."),
            ResponseItem.CreateUserMessageItem("Vermilion City (Pokémon Red & Blue Remix) (Mewmore)"),
            ResponseItem.CreateAssistantMessageItem("{\"genres\":[\"Electronic\",\"Pokemon\",\"Game\"],\"genresNotProvided\":[\"\"],languages\":[\"Instrumental\"],\"timePeriod\":\"2010s\"}\r\n"),
            ResponseItem.CreateUserMessageItem($"{song.Name} ({string.Join(",", artists.Select(x => x.Name))})")
        };
        var options = new ResponseCreationOptions()
        {
            //Tools = { ResponseTool.CreateWebSearchTool() },
        };

        try
        {
            var response = (await client.CreateResponseAsync(messages, options)).Value;
            var resultStr = response.GetOutputText();
            File.WriteAllText(Path.Combine(_fileConfig.SavedAiResponsesDirectory, $"{song.FileHash}.json"), resultStr);
            var result = JsonConvert.DeserializeObject<GenerateTagsResponse>(resultStr) ?? throw new Exception($"Failed to parse {song.FileHash}.json");
            return result;
        }
        catch (Exception)
        {
            throw;
        }
    }
}
