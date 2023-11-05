﻿using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class AuthenticateRequest : Request
    {
        [Required]
        public string Username { get; set; }
        [Required]
        public string UserHash { get; set; }

        public AuthenticateRequest()
        {
            RequestType = RequestType.Authenticate;
            RequestName = RequestType.Authenticate.ToString();
        }

        public override void UpdateLocalDb(ShuffullContext context)
        {

        }
    }
}
