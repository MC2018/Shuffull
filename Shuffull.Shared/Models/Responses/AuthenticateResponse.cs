using Shuffull.Shared.Models.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shuffull.Shared.Models.Responses
{
    [Serializable]
    public class AuthenticateResponse
    {
        [Required]
        public User User { get; set; }
        [Required]
        public string Token { get; set; }
        [Required]
        public DateTime Expiration { get; set; }

        public AuthenticateResponse(User user, string token, DateTime expiration)
        {
            User = user;
            Token = token;
            Expiration = expiration;
        }
    }
}
