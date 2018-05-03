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
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Devices.Client.Exceptions;
using Windows.Networking.Connectivity;

namespace Microsoft.Devices.Management
{
    // This IDeviceTwin represents the actual Azure IoT Device Twin
    public class AzureIoTHubDeviceTwinProxy : IDeviceTwin
    {
        DeviceClient deviceClient;
        Logger logger;

        public delegate Task ResetConnectionAsync(DeviceClient existingClient);
        ResetConnectionAsync resetConnectionAsyncHandler;

        public AzureIoTHubDeviceTwinProxy(DeviceClient deviceClient, ResetConnectionAsync resetConnectionAsyncHandler, Logger logger)
        {
            this.deviceClient = deviceClient;
            this.resetConnectionAsyncHandler = resetConnectionAsyncHandler;
            this.logger = logger;

            this.deviceClient.SetConnectionStatusChangesHandler(async (ConnectionStatus status, ConnectionStatusChangeReason reason) =>
            {
                string msg = "Connection changed: " + status.ToString() + " " + reason.ToString();
                System.Diagnostics.Debug.WriteLine(msg);
                this.logger.LogInformation(msg);                

                switch (reason)
                {
                    case ConnectionStatusChangeReason.Connection_Ok:
                        // No need to do anything, this is the expectation
                        break;

                    case ConnectionStatusChangeReason.Expired_SAS_Token:
                    case ConnectionStatusChangeReason.Bad_Credential:
                    case ConnectionStatusChangeReason.Retry_Expired:
                        await InternalRefreshConnectionAsync();
                        break;

                    case ConnectionStatusChangeReason.Client_Close:
                        // ignore this ... part of client shutting down.
                        break;

                    case ConnectionStatusChangeReason.Communication_Error:
                    case ConnectionStatusChangeReason.Device_Disabled:
                        // These are not implemented in the Azure SDK
                        break;

                    case ConnectionStatusChangeReason.No_Network:
                    // This seems to lead to Retry_Expired, so we can 
                    // ignore this ... maybe log the error.

                    default:
                        break;
                }
            });
        }

        async Task<TwinCollection> IDeviceTwin.GetDesiredPropertiesAsync()
        {            
            try
            {
                var azureCollection = await this.deviceClient.GetTwinAsync().AsAsyncOperation<Twin>();
                return azureCollection.Properties.Desired;
            }
            catch (IotHubCommunicationException e)
            {
                this.logger.LogException(e.Message, e);                
                await InternalRefreshConnectionAsync();
            }
            catch (Exception e)
            {
                this.logger.LogException(e.Message, e);
            }

            return null;
        }

        async Task IDeviceTwin.ReportProperties(Dictionary<string, object> collection)
        {
            this.logger.LogInformation("AzureIoTHubDeviceTwinProxy.ReportProperties");

            TwinCollection azureCollection = new TwinCollection();
            foreach (KeyValuePair<string, object> p in collection)
            {
                //Logger.Log("  Reporting: " + p.Key, LoggingLevel.Information);
                if (p.Value is JObject)
                {
                    JObject jObject = (JObject)p.Value;
                    foreach (JProperty property in jObject.Children())
                    {
                        this.logger.LogInformation("    Reporting: " + property.Name);
                    }
                }
                azureCollection[p.Key] = p.Value;
            }

            try
            {
                await this.deviceClient.UpdateReportedPropertiesAsync(azureCollection);
            }
            catch (IotHubCommunicationException e)
            {
                this.logger.LogException(e.Message, e);                
                await InternalRefreshConnectionAsync();
            }
            catch (Exception e)
            {
                this.logger.LogException(e.Message, e);
            }
        }

        async Task IDeviceTwin.SetMethodHandlerAsync(string methodName, Func<string, Task<string>> methodHandler)
        {
            this.logger.LogInformation($"AzureIoTHubDeviceTwinProxy.SetMethodHandlerAsync ({methodName})");

            try
            {
                await this.deviceClient.SetMethodHandlerAsync(methodName, async (MethodRequest methodRequest, object userContext) =>
                {
                    var response = await methodHandler(methodRequest.DataAsJson);
                    return new MethodResponse(Encoding.UTF8.GetBytes(response), 0);
                }, null);
            }
            catch (IotHubCommunicationException e)
            {
                this.logger.LogException(e.Message, e);
                await InternalRefreshConnectionAsync();
            }
            catch (Exception e)
            {
                this.logger.LogException(e.Message, e);
            }
        }

        private static async Task WaitForInternet()
        {
            while (true)
            {
                ConnectionProfile connections = NetworkInformation.GetInternetConnectionProfile();
                bool internet = connections != null && connections.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None;
                if (internet) break;

                await Task.Delay(5 * 1000);
            }
        }

        async Task IDeviceTwin.RefreshConnectionAsync()
        {
            await InternalRefreshConnectionAsync();
        }

        private async Task InternalRefreshConnectionAsync()
        {
            while (true)
            {
                try
                {
                    await WaitForInternet();

                    var devicTwinImpl = this;
                    await devicTwinImpl.resetConnectionAsyncHandler(devicTwinImpl.deviceClient);
                    break;
                }
                catch (IotHubCommunicationException e)
                {
                    this.logger.LogException(e.Message, e);
                }
                catch (Exception e)
                {
                    this.logger.LogException(e.Message, e);
                }
                await Task.Delay(5 * 60 * 1000);
            }
        }
    }
}
