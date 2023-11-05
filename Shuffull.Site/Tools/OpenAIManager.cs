using OpenAI_API;
using OpenAI_API.Chat;
using Shuffull.Site.Database.Models;

namespace Shuffull.Site.Tools
{
    public static class OpenAIManager
    {
        private static OpenAIAPI _api;
        private static Conversation _tagConversation = null;

        public static void Init(string apiKey)
        {
            _api = new OpenAIAPI(apiKey);

            // Tag conversation setup
            var instruction = "I will send you a song name and artist(s). Return a list of all genres ";
            var chatRequest = new ChatRequest()
            {
                Model = OpenAI_API.Models.Model.ChatGPTTurbo,
                TopP = 0.4f
            };

            _tagConversation = _api.Chat.CreateConversation(chatRequest);
            _tagConversation.AppendSystemMessage(instruction);
        }

        public static void RequestTagResponse(List<Tag> tags, string message)
        {

            if (_tagConversation == null)
            {
                
            }

        }
    }
}
