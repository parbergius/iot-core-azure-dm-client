using Microsoft.Devices.Management.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.System;

namespace Microsoft.Devices.Management
{
    


    class IotStartupProxy : IIotStartupProxy
    {   
        public async Task<IDictionary<string, ApplicationTypes>> SendCommandAsync(IotStartupCommands command)
        {
            IDictionary<string, ApplicationTypes> result = new Dictionary<string, ApplicationTypes>();

            var processLauncherOptions = new ProcessLauncherOptions();            
            var standardOutput = new InMemoryRandomAccessStream();

            processLauncherOptions.StandardOutput = standardOutput;
            processLauncherOptions.StandardError = null;
            
            var processLauncherResult = await ProcessLauncher.RunToCompletionAsync(@"C:\Windows\System32\IotStartup.exe", command.ToString(), processLauncherOptions);
            if (processLauncherResult.ExitCode == 0)
            {
                using (var outStreamRedirect = standardOutput.GetInputStreamAt(0))
                {
                    var size = standardOutput.Size;

                    using (var dataReader = new DataReader(outStreamRedirect))
                    {
                        var bytesLoaded = await dataReader.LoadAsync((uint)size);
                        var stringRead = dataReader.ReadString(bytesLoaded);

                        System.IO.StringReader sr = new System.IO.StringReader(stringRead);

                        string line = null;
                        while ((line = sr.ReadLine()) != null)
                        {
                            string applicationType = line.Split(':')[0].Trim();
                            string application = line.Split(':')[1].Trim();

                            result.Add(application, (ApplicationTypes)Enum.Parse(typeof(ApplicationTypes), applicationType));
                        };                        
                    }

                    return result;
                }
            }
            else
            {
                throw new Exception("CommProxy cannot read data from the input pipe");
            }

        }
    }
}
