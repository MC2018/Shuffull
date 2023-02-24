using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Mobile.Services
{
    public interface IForegroundService
    {
        bool StartService();
        bool StopService();
    }
}
