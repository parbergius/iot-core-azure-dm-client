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

    internal interface ICommandLineProxy
    {
        Task<IDictionary<string, ApplicationTypes>> IotStartup(IotStartupCommands command);

        Task<string> ScreenCapture(string filename);
    }
}
