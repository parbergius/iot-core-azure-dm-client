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
    class CommandLineProxy : ICommandLineProxy
    {
        private const string _csCommandBasePath = @"C:\Windows\System32\";

        enum Commands
        {
            IotStartup,
            ScreenCapture
        }

        public async Task<string> ScreenCapture(string filename)
        {
            return await RunCommand(Commands.ScreenCapture, filename);
        }

        public async Task<IDictionary<string, ApplicationTypes>> IotStartup(IotStartupCommands command)
        {
            IDictionary<string, ApplicationTypes> resposne = new Dictionary<string, ApplicationTypes>();

            var result = await RunCommand(Commands.IotStartup, command.ToString());

            System.IO.StringReader sr = new System.IO.StringReader(result);

            string line = null;
            while ((line = sr.ReadLine()) != null)
            {
                string applicationType = line.Split(':')[0].Trim();
                string application = line.Split(':')[1].Trim();

                resposne.Add(application, (ApplicationTypes)Enum.Parse(typeof(ApplicationTypes), applicationType));
            };

            return resposne;
        }

        private async Task<string> RunCommand(Commands command, params string[] args)
        {
            var processLauncherOptions = new ProcessLauncherOptions();
            var standardOutput = new InMemoryRandomAccessStream();

            processLauncherOptions.StandardOutput = standardOutput;
            processLauncherOptions.StandardError = null;

            string commandFilename = $"{_csCommandBasePath}{command}.exe";
            string commandArgs = string.Join(" ", args);

            var processLauncherResult = await ProcessLauncher.RunToCompletionAsync(commandFilename, commandArgs, processLauncherOptions);
            if (processLauncherResult.ExitCode == 0)
            {
                using (var outStreamRedirect = standardOutput.GetInputStreamAt(0))
                {
                    var size = standardOutput.Size;

                    using (var dataReader = new DataReader(outStreamRedirect))
                    {
                        var bytesLoaded = await dataReader.LoadAsync((uint)size);
                        return  dataReader.ReadString(bytesLoaded);
                    }
                }
            }
            else
            {   
                throw new Exception("CommProxy cannot read data from the input pipe");
            }
        }
    }
}
