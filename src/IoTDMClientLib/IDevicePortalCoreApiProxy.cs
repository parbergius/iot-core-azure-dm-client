using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Devices.Management
{
    internal interface IDevicePortalCoreApiProxy
    {
        Task<IList<Models.Process>> GetModernApplicationProcesses();
    }
}
