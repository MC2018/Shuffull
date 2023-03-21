using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Shared.Networking.Models.Requests
{
    public interface IRequest
    {
        string Guid { get; set; }
        DateTime TimeRequested { get; set; }
    }
}
