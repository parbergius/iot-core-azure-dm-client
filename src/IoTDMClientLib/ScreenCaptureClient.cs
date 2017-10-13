using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Devices.Management
{
    public class ScreenCapture
    {
        private void LogError(string message, Exception ex)
        {
            using (var channel = new Windows.Foundation.Diagnostics.LoggingChannel("IoTDMClientLib", null)) // null means use default options.
            {
                // Use this Id in xperf parameter
                System.Diagnostics.Debug.WriteLine(channel.Id);
                channel.LogMessage($"{message}. {ex.Message}", Windows.Foundation.Diagnostics.LoggingLevel.Error);
            }
        }

        private ScreenCapture(string filename, ICommandLineProxy commandLineProxy, TwinCollection desiredProperties)
        {
            this._filename = filename;
            this._commandLineProxy = commandLineProxy;
            this._timer = new Timer(TimerCallback, null, 500, _minimumInterval);

            UpdateConfiguration(desiredProperties);
        }

        public static async Task<ScreenCapture> CreateAsync(IDeviceTwin deviceTwin, string deviceId)
        {
            var filename = $"{deviceId}.jpg";

            var desiredProperties = await deviceTwin.GetDesiredPropertiesAsync();
            return new ScreenCapture(filename, new CommandLineProxy(), desiredProperties);
        }

        public void UpdateConfiguration(TwinCollection desiredProperties)
        {
            foreach (KeyValuePair<string, object> dp in desiredProperties)
            {
                if (dp.Key == "microsoft" && dp.Value is JObject)
                {
                    JToken managementNode;
                    if ((dp.Value as JObject).TryGetValue("management", out managementNode))
                    {
                        foreach (var managementProperty in managementNode.Children().OfType<JProperty>())
                        {
                            switch (managementProperty.Name)
                            {
                                case "screenCapture":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        JObject subProperties = (JObject)managementProperty.Value;

                                        int interval = int.Parse(subProperties.Property("interval").Value.ToString());
                                        interval = interval < _minimumInterval ? _minimumInterval : interval;
                                        _timer.Change(500, interval);

                                        JObject externalStorageProperties = (JObject)subProperties.Property("externalStorage").Value;

                                        _connectionString = (string)externalStorageProperties.Property("connectionString").Value;
                                        _containerName = (string)externalStorageProperties.Property("containerName").Value;
                                    }
                                    break;

                            }
                        }
                    }
                }
            }
        }

        private async void TimerCallback(object state)
        {
            string commandResult = "";
            Windows.Storage.StorageFile localDataFile = null;

            if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_containerName))
                return;

            try
            {
                string fullPath = $"{Windows.Storage.ApplicationData.Current.TemporaryFolder.Path}\\{_filename}";
                commandResult = await _commandLineProxy.ScreenCapture(fullPath);
            }
            catch (Exception ex)
            {
                LogError($"An exception occurred when trying to execute ScreenCapture. {commandResult}", ex);
            }

            try
            {
                localDataFile = await Windows.Storage.ApplicationData.Current.TemporaryFolder.GetFileAsync(_filename);
            }
            catch (Exception ex)
            {
                LogError("An exception occurred when trying to read screen capture file", ex);
            }

            try
            {
                var info = new Message.AzureFileTransferInfo()
                {
                    ConnectionString = _connectionString,
                    ContainerName = _containerName,
                    BlobName = _filename,
                    Upload = true,
                    LocalPath = ""
                };

                await IoTDMClient.AzureBlobFileTransfer.UploadFile(info, localDataFile);
            }
            catch (Exception ex)
            {
                LogError("An exception occurred when trying to upload screen capture to Azure Storage Account", ex);
            }
        }

        ICommandLineProxy _commandLineProxy;        
        readonly string _filename;
        const string _csLocalStoragePath = "c:\\Data\\Users\\DefaultAccount\\AppData\\Local\\Temp\\IotDm\\";
        const int _minimumInterval = 60000;        
        string _connectionString;
        string _containerName;
        Timer _timer;

    }
}
