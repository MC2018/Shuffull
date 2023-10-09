using Shuffull.Shared.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
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
        public string RequestName { get; protected set; }

        public abstract void UpdateLocalDb(ShuffullContext context);
    }
}
