using System.Text;
using System.Text.Json;

namespace Shuffull.Site.Tools
{
    public class LowerCamelCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            var result = new StringBuilder(name);
            var uppersInARow = 0;

            if (name.Length > 0 && char.IsLower(name[0]))
            {
                throw new ArgumentException("String started with a lowercase character.");
            }

            while (uppersInARow < name.Length)
            {
                if (char.IsLower(name[uppersInARow]))
                {
                    break;
                }

                uppersInARow++;
            }

            for (int i = 0; i < uppersInARow || (uppersInARow == name.Length && i < name.Length); i++)
            {
                result[i] = char.ToLower(result[i]);
            }

            return result.ToString();
        }
    }
}
