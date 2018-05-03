/*
Copyright 2017 Microsoft
Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
and associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Devices.Management;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Diagnostics;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace IoTDMBackground
{
    class DeviceManagementRequestHandler : IDeviceManagementRequestHandler
    {
        public DeviceManagementRequestHandler()
        {
        }

        // It is always ok to reboot
        Task<bool> IDeviceManagementRequestHandler.IsSystemRebootAllowed()
        {
            return Task.FromResult(true);
        }
    }

    public sealed class DMClientBackgroundApplication : IBackgroundTask
    {
        private string _deviceId = "";
        private DeviceManagementClient _dmClient;
        private BackgroundTaskDeferral _deferral;
        private ScreenCapture _scClient;
        private Logger _logger;

        private Logger Logger {
            get
            {
                if (_logger == null)
                    _logger = new Logger("cd27397d-8444-4d02-ae7a-0fbed4c0027b");

                return _logger;
            }
        }
        
        private async Task<string> GetConnectionStringAsync()
        {
            var tpmDevice = new TpmDevice(0);

            string connectionString = "";
            bool firstTimeException = false;

            do
            {
                try
                {
                    connectionString = await tpmDevice.GetConnectionStringAsync();
                    break;
                }
                catch (Exception ex)
                {
                    if (!firstTimeException)
                        Logger.LogEvent($"tpmDevice.GetConnectionStringAsync. Exception: {ex.Message}, StrackTrace: {ex.StackTrace}");

                    firstTimeException = true;
                }
                Debug.WriteLine("Waiting...");
                await Task.Delay(1000);

            } while (true);

            return connectionString;
        }

        private async Task<string> GetDeviceIdAsync()
        {
            var tpmDevice = new TpmDevice(0);

            string deviceId = "";
            bool firstTimeException = false;

            do
            {
                try
                {
                    deviceId = await tpmDevice.GetDeviceIdAsync();
                    break;
                }
                catch (Exception ex)
                {
                    if (!firstTimeException)
                        Logger.LogEvent($"tpmDevice.GetDeviceIdAsync. Exception: {ex.Message}, StrackTrace: {ex.StackTrace}");

                    firstTimeException = true;                    
                }
                Debug.WriteLine("Waiting...");
                await Task.Delay(1000);

            } while (true);

            return deviceId;
        }

        private async Task ResetConnectionAsync(DeviceClient existingConnection)
        {
            Logger.LogEvent("ResetConnectionAsync start");
            
            // Attempt to close any existing connections before
            // creating a new one
            if (existingConnection != null)
            {
                await existingConnection.CloseAsync().ContinueWith((t) =>
                {
                    var e = t.Exception;
                    if (e != null)
                    {   
                        var msg = "existingClient.CloseAsync exception: " + e.Message + "\n" + e.StackTrace;
                        System.Diagnostics.Debug.WriteLine(msg);
                        Logger.LogEvent(msg);
                    }
                });
            }

            // Get new SAS Token
            var deviceConnectionString = await GetConnectionStringAsync();            

            // Get device id
            _deviceId = await GetDeviceIdAsync();
            Logger.SetDeviceId(_deviceId);            

            // Create DeviceClient. Application uses DeviceClient for telemetry messages, device twin
            // as well as device management
            var newDeviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);            

            // IDeviceTwin abstracts away communication with the back-end.
            // AzureIoTHubDeviceTwinProxy is an implementation of Azure IoT Hub
            IDeviceTwin deviceTwin = new AzureIoTHubDeviceTwinProxy(newDeviceClient, ResetConnectionAsync, Logger);            

            // IDeviceManagementRequestHandler handles device management-specific requests to the app,
            // such as whether it is OK to perform a reboot at any givem moment, according the app business logic
            // ToasterDeviceManagementRequestHandler is the Toaster app implementation of the interface
            IDeviceManagementRequestHandler appRequestHandler = new DeviceManagementRequestHandler();            

            // Create the DeviceManagementClient, the main entry point into device management
            this._dmClient = await DeviceManagementClient.CreateAsync(deviceTwin, appRequestHandler);            

            // Create the CaptureScreenClient 
            _scClient = await ScreenCapture.CreateAsync(deviceTwin, _deviceId);            

            // Set the callback for desired properties update. The callback will be invoked
            // for all desired properties -- including those specific to device management
            await newDeviceClient.SetDesiredPropertyUpdateCallback(OnDesiredPropertyUpdated, null);            

            // Tell the deviceManagementClient to sync the device with the current desired state.
            //await this._dmClient.ApplyDesiredStateAsync();

            Logger.LogEvent("ResetConnectionAsync end");
        }

        private async Task InitializeDeviceClientAsync()
        {
            while (true)
            {
                try
                {
                    await ResetConnectionAsync(null);
                    break;
                }
                catch (Exception e)
                {                    
                    var msg = "InitializeDeviceClientAsync exception: " + e.Message + "\n" + e.StackTrace;
                    System.Diagnostics.Debug.WriteLine(msg);
                    Logger.LogEvent(msg);
                }

                await Task.Delay(5 * 60 * 1000);
            }
        }

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();

            await InitializeDeviceClientAsync();
        }

        private async Task OnDesiredPropertyUpdated(TwinCollection twinProperties, object userContext)
        {
            // Let the device management client process properties specific to device management
            try
            {
                _dmClient.ProcessDeviceManagementProperties(twinProperties);
                _scClient.UpdateConfiguration(twinProperties);
            }
            catch (System.Exception ex)
            {
                Logger.LogException("Un exception occurred when trying to process device desired properties.", ex);
            }
        }
    }
}