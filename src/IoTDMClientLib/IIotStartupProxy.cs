using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Devices.Management
{
    enum ApplicationTypes
    {
        Headed,
        Headless,
        All
    }

    enum IotStartupCommands
    {
        List,
        Startup
    }

    internal interface IIotStartupProxy
    {
        Task<IDictionary<string, ApplicationTypes>> SendCommandAsync(IotStartupCommands command);
    }
}
