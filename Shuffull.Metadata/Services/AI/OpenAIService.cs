using Newtonsoft.Json;
using Nut.Results;
using OpenAI.Chat;
using Shuffull.Metadata.Configuration;
using Shuffull.Metadata.Models.AI;

namespace Shuffull.Metadata.Services.AI;

public class OpenAIService(OpenAIConfiguration config) : IAIService
{
    private readonly OpenAIConfiguration _config = config;

    public async Task<Result<GenerateMainGenresResponse>> GenerateMainGenresAsync(GenerateMainGenresRequest request, CancellationToken cancellationToken = default!)
    {
        var messages = new List<ChatMessage>()
        {
            new UserChatMessage(
                "You will be provided a song name, its artist(s), and list of main genres.\n" +
                "Based on the song info, determine between 1 and 3 main genres that best fit the song, only from the list provided.\n" +
                "Validate your work."),
            new AssistantChatMessage("Understood. Send the information."),
            new UserChatMessage(
                $"Song name: {request.SongName}\n" +
                $"Artist(s): {string.Join(",", request.ArtistNames)}\n" +
                $"Main Genres: {string.Join(", ", request.MainGenres)}" +
                (string.IsNullOrWhiteSpace(request.MainGenresContext) ? "" : $"\n\nAdditional context:\n{request.MainGenresContext}")),
        };
        var options = new ChatCompletionOptions()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat
            (
                jsonSchemaFormatName: "main_genres_response",
                jsonSchema: BinaryData.FromBytes("""
                    {
                        "type": "object",
                        "properties": {
                            "mainGenres": {
                                "type": "array",
                                "items": { "type": "string" }
                            }
                        },
                        "required": ["mainGenres"],
                        "additionalProperties": false
                    }
                 """u8.ToArray()),
                jsonSchemaIsStrict: true
            )
        };

        try
        {
            var client = new ChatClient(model: _config.ModelName, apiKey: _config.ApiKey);
            var completion = (await client.CompleteChatAsync(messages, options, cancellationToken)).Value;
            var resultStr = completion.Content[0].Text;
            var result = JsonConvert.DeserializeObject<GenerateMainGenresResponse>(resultStr);

            if (result == null)
            {
                throw new Exception("Failed to parse MainGenresResponse from AI response.");
            }
            else if (result.MainGenres.Count > 3)
            {
                throw new Exception($"AI returned more than 3 main genres: {string.Join(", ", result.MainGenres)}. Please ensure the AI is configured correctly to return a maximum of 3 main genres.");
            }
            else if (result.MainGenres.Count == 0)
            {
                throw new Exception("AI returned no genres. Please ensure the AI is configured correctly to return at least one genre.");
            }
            else if (result.MainGenres.Any(x => !request.MainGenres.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                throw new Exception(
                    $"AI returned main genres that were not in the provided list: {string.Join(", ", result.MainGenres.Where(x => !request.MainGenres.Contains(x, StringComparer.OrdinalIgnoreCase)))}. " +
                    $"Please ensure the AI is configured correctly to return only main genres from the provided list.");
            }
            return result;
        }
        catch (Exception e)
        {
            return Result.Error<GenerateMainGenresResponse>(e);
        }
    }

    public async Task<Result<GenerateSubGenresResponse>> GenerateSubGenresAsync(GenerateSubGenresRequest request, CancellationToken cancellationToken = default!)
    {
        var messages = new List<ChatMessage>()
        {
            new UserChatMessage(
                "You will be provided a song name, its artist(s), and list of sub-genres.\n" +
                "Look up the song online and determine the genres that accurately describe the song from the list provided, provide at least 1.\n" +
                "Do not guess."),
            new AssistantChatMessage("Understood. Send the information."),
            new UserChatMessage(
                $"Song name: {request.SongName}\n" +
                $"Artist(s): {string.Join(",", request.ArtistNames)}\n" +
                $"Sub-Genres: {string.Join(", ", request.SubGenres)}" +
                (string.IsNullOrWhiteSpace(request.SubGenresContext) ? "" : $"\n\nAdditional context:\n{request.SubGenresContext}")),
        };
        var options = new ChatCompletionOptions()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat
            (
                jsonSchemaFormatName: "sub_genres_response",
                jsonSchema: BinaryData.FromBytes("""
                    {
                        "type": "object",
                        "properties": {
                            "subGenres": {
                                "type": "array",
                                "items": { "type": "string" }
                            }
                        },
                        "required": ["subGenres"],
                        "additionalProperties": false
                    }
                 """u8.ToArray()),
                jsonSchemaIsStrict: true
            )
        };

        try
        {
            var client = new ChatClient(model: _config.ModelName, apiKey: _config.ApiKey);
            var completion = (await client.CompleteChatAsync(messages, options, cancellationToken)).Value;
            var resultStr = completion.Content[0].Text;
            var result = JsonConvert.DeserializeObject<GenerateSubGenresResponse>(resultStr);

            if (result == null)
            {
                throw new Exception("Failed to parse MainGenresResponse from AI response.");
            }
            else if (result.SubGenres.Count() == 0)
            {
                throw new Exception("AI returned no sub genres. Please ensure the AI is configured correctly to return at least one sub genre.");
            }
            return result;
        }
        catch (Exception e)
        {
            return Result.Error<GenerateSubGenresResponse>(e);
        }
    }

    // TODO: potentially include metadata as an optional field in the database when importing, and include things like upload date, etc.
    public async Task<Result<GenerateOtherSongDetailsResponse>> GenerateOtherSongDetailsAsync(GenerateOtherSongDetailsRequest request, CancellationToken cancellationToken = default!)
    {
        var messages = new List<ChatMessage>()
        {
            new UserChatMessage(
                "You will be provided a song name and its artist(s).\n" +
                "Based on this info, return a list of prominent languages used in the lyrics, as well as the song's original release time period.\n" +
                "Verify online to see if the song has lyrics, and if it doesn't, include only \"Instrumental\" as a language.\n" +
                "For the time period, return the decade, if it's post-1900, return the decade, such as \"1930s\" or \"2010s\".\n" +
                "Otherwise, return the century, such as \"1700s\" or \"1800s\".\n" +
                "Do not guess, verify work."),
            new AssistantChatMessage("Understood. Send the information."),
            new UserChatMessage(
                $"Song name: {request.SongName}\n" +
                $"Artist(s): {string.Join(",", request.ArtistNames)}\n" +
                (string.IsNullOrWhiteSpace(request.OtherDetailsContext) ? "" : $"\nAdditional context:\n{request.OtherDetailsContext}")),
        };
        var options = new ChatCompletionOptions()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat
            (
                jsonSchemaFormatName: "other_song_details_response",
                jsonSchema: BinaryData.FromBytes("""
                    {
                        "type": "object",
                        "properties": {
                            "timePeriod": { "type": "string" },
                            "languages": {
                                "type": "array",
                                "items": { "type": "string" }
                            }
                        },
                        "required": ["timePeriod", "languages"],
                        "additionalProperties": false
                    }
                 """u8.ToArray()),
                jsonSchemaIsStrict: true
            )
        };

        try
        {
            var client = new ChatClient(model: _config.ModelName, apiKey: _config.ApiKey);
            var completion = (await client.CompleteChatAsync(messages, options, cancellationToken)).Value;
            var resultStr = completion.Content[0].Text;
            var result = JsonConvert.DeserializeObject<GenerateOtherSongDetailsResponse>(resultStr);

            if (result == null)
            {
                throw new Exception("Failed to parse OtherSongDetailsResponse from AI response.");
            }
            else if (result.Languages.Count == 0)
            {
                result.Languages.Add("Instrumental");
            }
            return result;
        }
        catch (Exception e)
        {
            return Result.Error<GenerateOtherSongDetailsResponse>(e);
        }
    }
}
