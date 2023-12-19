using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public abstract class Request
    {
        [Required]
        [Key]
        public string Guid { get; set; }
        [Required]
        public DateTime TimeRequested { get; set; }
        [Required]
        public RequestType RequestType { get; protected set; }
        [Required]
        public ProcessingMethod ProcessingMethod { get; protected set; }
        [Required]
        public string RequestName { get; protected set; }


        // TODO: should get rid of this maybe? this logic should prob just be whereever the request is used
        public abstract void UpdateLocalDb(ShuffullContext context);
    }
}
