using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shuffull.Shared.Enums;

namespace Shuffull.Shared.Networking.Models.Requests
{
    [Serializable]
    public class OverallSyncRequest : Request
    {
        public OverallSyncRequest()
        {
            RequestType = RequestType.OverallSync;
            RequestName = RequestType.OverallSync.ToString();
            ProcessingMethod = ProcessingMethod.OnlyOnce;
        }

        public override void UpdateLocalDb(ShuffullContext context)
        {

        }
    }
}
