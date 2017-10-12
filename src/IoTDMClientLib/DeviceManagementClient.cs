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
using Microsoft.Azure.Devices.Shared;
using Microsoft.Devices.Management.Message;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.Services.Store;
using Windows.Storage;

namespace Microsoft.Devices.Management
{
    // This is the main entry point into DM
    public class DeviceManagementClient
    {
        // Types
        public struct DMMethodResult
        {
            public uint returnCode;
            public string response;
        }

        public struct ExternalStorage
        {
            public string connectionString;
            public string containerName;
        }

        public struct GetCertificateDetailsParams
        {
            public string path;
            public string hash;
            public string connectionString;
            public string containerName;
            public string blobName;
        }

        private DeviceManagementClient(IDeviceTwin deviceTwin, IDeviceManagementRequestHandler requestHandler,
            ISystemConfiguratorProxy systemConfiguratorProxy, IIotStartupProxy iotStartupProxy, IDevicePortalCoreApiProxy devicePortalCoreApiProxy)
        {
            this._deviceTwin = deviceTwin;
            this._requestHandler = requestHandler;
            this._systemConfiguratorProxy = systemConfiguratorProxy;
            this._iotStartupProxy = iotStartupProxy;
            this._devicePortalCoreApiProxy = devicePortalCoreApiProxy;
            this._externalStorage = new ExternalStorage();
        }

        public static async Task<DeviceManagementClient> CreateAsync(IDeviceTwin deviceTwin, IDeviceManagementRequestHandler requestHandler)
        {
            DeviceManagementClient deviceManagementClient = Create(deviceTwin, requestHandler, new SystemConfiguratorProxy(), new IotStartupProxy(), new DevicePortalCoreApiProxy());
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.immediateReboot", deviceManagementClient.ImmediateRebootMethodHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.appInstall", deviceManagementClient.AppInstallMethodHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.appUninstall", deviceManagementClient.AppUninstallMethodHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.reportAllDeviceProperties", deviceManagementClient.ReportAllDevicePropertiesMethodHandler);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.startAppSelfUpdate", deviceManagementClient.StartAppSelfUpdateMethodHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.getCertificateDetails", deviceManagementClient.GetCertificateDetailsHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.factoryReset", deviceManagementClient.FactoryResetHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.getStartupForegroundApp", deviceManagementClient.GetStartupForegroundAppHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.listStartupBackgroundApps", deviceManagementClient.ListStartupBackgroundAppsHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.addStartupApp", deviceManagementClient.AddStartupAppHandlerAsync);
            await deviceTwin.SetMethodHandlerAsync("microsoft.management.listApps", deviceManagementClient.ListAppsHandlerAsync);
            return deviceManagementClient;
        }

        internal static DeviceManagementClient Create(IDeviceTwin deviceTwin, IDeviceManagementRequestHandler requestHandler,
            ISystemConfiguratorProxy systemConfiguratorProxy, IIotStartupProxy iotStartupProxy, IDevicePortalCoreApiProxy devicePortalCoreApiProxy)
        {
            return new DeviceManagementClient(deviceTwin, requestHandler, systemConfiguratorProxy, iotStartupProxy, devicePortalCoreApiProxy);
        }

        //
        // Commands:
        //

        // This command checks if updates are available. 
        // TODO: work out complete protocol (find updates, apply updates etc.)
        public async Task<bool> CheckForUpdatesAsync()
        {
            var request = new Message.CheckForUpdatesRequest();
            var response = await this._systemConfiguratorProxy.SendCommandAsync(request);
            return (response as Message.CheckForUpdatesResponse).UpdatesAvailable;
        }

        internal async Task TransferFileAsync(Message.AzureFileTransferInfo transferInfo)
        {
            // use C++ service to copy file to/from App LocalData
            var request = new Message.AzureFileTransferRequest(transferInfo);
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        internal async Task<string> ListAppsHandlerAsync(string jsonParam)
        {
            var getAppsTask = this._iotStartupProxy.SendCommandAsync(IotStartupCommands.List);
            var getStartupsTask = this._iotStartupProxy.SendCommandAsync(IotStartupCommands.Startup);
            var getAppDetailsTask = this._systemConfiguratorProxy.SendCommandAsync(new Message.ListAppsRequest());
            var getProcessesTask = this._devicePortalCoreApiProxy.GetModernApplicationProcesses();

            // Wait for all tasks to complete!
            await Task.WhenAll(getAppsTask, getStartupsTask, getAppDetailsTask, getProcessesTask);

            // Get reults
            var apps = getAppsTask.Result;
            var startups = getStartupsTask.Result;
            var appDetails = getAppDetailsTask.Result as Message.ListAppsResponse;
            var processes = getProcessesTask.Result;

            Func<string, AppInfo> getAppInfoByEntryPoint = (entryPoint) =>
            {
                var app = appDetails.Apps.FirstOrDefault(ad => entryPoint.StartsWith($"{ad.Value.PackageFamilyName}!"));
                return app.Value;
            };

            Func<string, Models.Process> getProcessEntryPoint = (entryPoint) =>
            {
                var proc = processes.FirstOrDefault(p => p.PackageFullName.StartsWith(getAppInfoByEntryPoint(entryPoint).Name));
                return proc;
            };
            
            var response = JsonConvert.SerializeObject(new
            {
                Apps = apps
                    .Select(app =>
                        new
                        {
                            EntryPoint = app.Key,
                            Type = app.Value == ApplicationTypes.Headed ? "Foreground" : "Background",
                            IsStartup = startups.ContainsKey(app.Key),                            
                            Name = getAppInfoByEntryPoint(app.Key).Name,                            
                            PackageFamilyName = getAppInfoByEntryPoint(app.Key).PackageFamilyName,
                            Version = getAppInfoByEntryPoint(app.Key).Version,
                            IsRunning = getProcessEntryPoint(app.Key) != null,
                            ProcessId = getProcessEntryPoint(app.Key)?.ProcessId,

                        }),
                Status = appDetails.Status,
                Tag = appDetails.Tag
            });

            return response;
        }

        internal async Task<IDictionary<string, Message.AppInfo>> ListAppsAsync()
        {
            var request = new Message.ListAppsRequest();
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request);
            return (result as Message.ListAppsResponse).Apps;
        }
        
        internal async Task<string> AppInstallMethodHandlerAsync(string jsonParam)
        {
            try
            {
                // method should return immediately .. only validate the json param
                var appBlobInfo = JsonConvert.DeserializeObject<IoTDMClient.AppBlobInfo>(jsonParam);
                // task should run without blocking so resonse can be generated right away
                var response = await appBlobInfo.AppInstallAsync(this);

                return response;
            }
            catch (Exception e)
            {
                // response with failure
                var response = JsonConvert.SerializeObject(new { response = "rejected", reason = e.Message });
                return response; // Task.FromResult(response);
            }
        }

        internal async Task InstallAppAsync(Message.AppInstallInfo appInstallInfo)
        {
            var request = new Message.AppInstallRequest(appInstallInfo);
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        internal async Task<string> AppUninstallMethodHandlerAsync(string jsonParam)
        {
            try
            {
                var appUninstallInfo = JsonConvert.DeserializeObject<Message.AppUninstallInfo>(jsonParam);
                await UninstallAppAsync(appUninstallInfo);

                var response = JsonConvert.SerializeObject(new { response = "succeeded" });
                return response;
            }
            catch (Exception e)
            {
                // response with failure
                var response = JsonConvert.SerializeObject(new { response = "rejected", reason = e.Message });
                return response;
            }
        }

        internal async Task UninstallAppAsync(Message.AppUninstallInfo appUninstallInfo)
        {
            var request = new Message.AppUninstallRequest(appUninstallInfo);
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        internal async Task<string> ListStartupBackgroundAppsHandlerAsync(string jsonParam)
        {
            var request = new Message.ListStartupBackgroundAppsRequest();
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.ListStartupBackgroundAppsResponse;

            var response = JsonConvert.SerializeObject(new
            {
                StartupBackgroundApps = result.StartupBackgroundApps.Select(app => app),
                Status = result.Status,
                Tag = result.Tag
            });

            return response;
        }

        internal async Task<string> GetStartupForegroundAppHandlerAsync(string jsonParam)
        {
            var request = new Message.GetStartupForegroundAppRequest();
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetStartupForegroundAppResponse;

            var response = JsonConvert.SerializeObject(result);
            return response;
        }

        internal async Task<string> GetStartupForegroundAppAsync()
        {
            var request = new Message.GetStartupForegroundAppRequest();
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request);
            return (result as Message.GetStartupForegroundAppResponse).StartupForegroundApp;
        }

        internal async Task<IList<string>> ListStartupBackgroundAppsAsync()
        {
            var request = new Message.ListStartupBackgroundAppsRequest();
            var result = await this._systemConfiguratorProxy.SendCommandAsync(request);
            return (result as Message.ListStartupBackgroundAppsResponse).StartupBackgroundApps;
        }

        internal async Task AddStartupAppAsync(Message.StartupAppInfo startupAppInfo)
        {
            var request = new Message.AddStartupAppRequest(startupAppInfo);
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        internal async Task RemoveStartupAppAsync(Message.StartupAppInfo startupAppInfo)
        {
            var request = new Message.RemoveStartupAppRequest(startupAppInfo);
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        internal async Task AppLifecycleAsync(Message.AppLifecycleInfo appInfo)
        {
            var request = new Message.AppLifecycleRequest(appInfo);
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        public enum RebootRequestStatus
        {
            Allowed,
            Disabled,
            InActiveHours,
            RejectedByApp
        }

        private static string RebootRequestStatusString(RebootRequestStatus status)
        {
            switch (status)
            {
                case RebootRequestStatus.Allowed: return "allowed";
                case RebootRequestStatus.Disabled: return "disabled";
                case RebootRequestStatus.InActiveHours: return "inActiveHours";
                case RebootRequestStatus.RejectedByApp: return "rejectedByApp";
            }
            return status.ToString();
        }

        private void ReportImmediateRebootStatus(RebootRequestStatus rebootRequestStatus, string rebootCmdTime)
        {
            Dictionary<string, object> collection = new Dictionary<string, object>();
            collection["microsoft"] = new
            {
                management = new
                {
                    rebootInfo = new
                    {
                        lastRebootCmdTime = rebootCmdTime,
                        lastRebootCmdStatus = (rebootRequestStatus == RebootRequestStatus.Allowed ? "accepted" : RebootRequestStatusString(rebootRequestStatus))
                    }
                }
            };

            _deviceTwin.ReportProperties(collection);
        }

        public async Task<RebootRequestStatus> IsRebootAllowedBySystem()
        {
            var request = new Message.GetWindowsUpdateRebootPolicyRequest();
            var response = await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetWindowsUpdateRebootPolicyResponse;
            if (!response.configuration.allow)
            {
                return RebootRequestStatus.Disabled;
            }

            Message.GetWindowsUpdatePolicyResponse updatePolicy = await this.GetWindowsUpdatePolicyAsync();
            uint nowHour = (uint)DateTime.Now.Hour;
            if (updatePolicy.configuration.activeHoursStart <= nowHour && nowHour < updatePolicy.configuration.activeHoursEnd)
            {
                return RebootRequestStatus.InActiveHours;
            }

            return RebootRequestStatus.Allowed;
        }

        private async Task<RebootRequestStatus> IsRebootAllowedByApp()
        {
            bool allowed = await this._requestHandler.IsSystemRebootAllowed();
            if (!allowed)
            {
                return RebootRequestStatus.RejectedByApp;
            }

            return RebootRequestStatus.Allowed;
        }

        private async Task<string> ImmediateRebootMethodHandlerAsync(string jsonParam)
        {
            string rebootCmdTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");

            RebootRequestStatus rebootRequestStatus = await IsRebootAllowedBySystem();
            if (rebootRequestStatus != RebootRequestStatus.Allowed)
            {
                // Report to the device twin
                ReportImmediateRebootStatus(rebootRequestStatus, rebootCmdTime);

                // Return details in the method return payload
                return JsonConvert.SerializeObject(new { response = RebootRequestStatusString(rebootRequestStatus) });
            }

            // Start the reboot operation asynchrnously, which may or may not succeed
            this.ImmediateRebootAsync(rebootCmdTime);
            return JsonConvert.SerializeObject(new { response = "accepted" });
        }

        private async Task<string> AddStartupAppHandlerAsync(string jsonParam)
        {
            try
            {
                var startupAppInfo = JsonConvert.DeserializeObject<Message.StartupAppInfo>(jsonParam);
                await this.AddStartupAppAsync(startupAppInfo);
                return JsonConvert.SerializeObject(new { response = "accepted" });
            }
            catch (Exception e)
            {
                // response with failure
                return JsonConvert.SerializeObject(new { response = "rejected", reason = e.Message });
            }

        }

        public async Task ImmediateRebootAsync()
        {
            await ImmediateRebootAsync(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        public async Task ImmediateRebootAsync(string rebootCmdTime)
        {
            RebootRequestStatus rebootRequestStatus = await IsRebootAllowedByApp();

            // Report to the device twin
            ReportImmediateRebootStatus(rebootRequestStatus, rebootCmdTime);

            if (rebootRequestStatus != RebootRequestStatus.Allowed)
            {
                return;
            }

            var request = new Message.ImmediateRebootRequest();
            request.lastRebootCmdTime = rebootCmdTime;
            await this._systemConfiguratorProxy.SendCommandAsync(request);
        }

        private void ReportSelfUpdateStatus(string lastCheckValue, string statusValue)
        {
            Dictionary<string, object> collection = new Dictionary<string, object>();
            collection["microsoft"] = new
            {
                management = new
                {
                    appUpdate = new
                    {
                        lastCheck = lastCheckValue,
                        status = statusValue,
                    }
                }
            };
            _deviceTwin.ReportProperties(collection);
        }

        private async Task StartAppSelfUpdate()
        {
            Debug.WriteLine("Check for updates...");
            StoreContext context = StoreContext.GetDefault();

            // Check for updates...
            string lastCheck = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");

            ReportSelfUpdateStatus(lastCheck, "checkStarting");

            IReadOnlyList<StorePackageUpdate> updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates.Count == 0)
            {
                ReportSelfUpdateStatus(lastCheck, "noUpdates");
                return;
            }

            // Download and install the updates...
            IAsyncOperationWithProgress<StorePackageUpdateResult, StorePackageUpdateStatus> downloadOperation =
                context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);

            ReportSelfUpdateStatus(lastCheck, "updatesDownloadingAndInstalling");

            // Wait for completion...
            StorePackageUpdateResult result = await downloadOperation.AsTask();

            ReportSelfUpdateStatus(lastCheck, result.OverallState == StorePackageUpdateState.Completed ? "installed" : "failed");

            return;
        }

        private Task<string> StartAppSelfUpdateMethodHandlerAsync(string jsonParam)
        {
            Debug.WriteLine("StartAppSelfUpdateMethodHandlerAsync");

            StartAppSelfUpdate();

            return Task.FromResult(JsonConvert.SerializeObject(new { response = "succeeded" }));
        }

        private async Task GetCertificateDetailsAsync(string jsonParam)
        {
            GetCertificateDetailsParams parameters = JsonConvert.DeserializeObject<GetCertificateDetailsParams>(jsonParam);

            var request = new Message.GetCertificateDetailsRequest();
            request.path = parameters.path;
            request.hash = parameters.hash;

            Message.GetCertificateDetailsResponse response = await _systemConfiguratorProxy.SendCommandAsync(request) as Message.GetCertificateDetailsResponse;

            string jsonString = JsonConvert.SerializeObject(response);
            Debug.WriteLine("response = " + jsonString);

            var info = new Message.AzureFileTransferInfo()
            {
                ConnectionString = parameters.connectionString,
                ContainerName = parameters.containerName,
                BlobName = parameters.blobName,
                Upload = true,
                LocalPath = ""
            };

            var appLocalDataFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(parameters.blobName, CreationCollisionOption.ReplaceExisting);
            using (StreamWriter writer = new StreamWriter(await appLocalDataFile.OpenStreamForWriteAsync()))
            {
                await writer.WriteAsync(jsonString);
            }
            await IoTDMClient.AzureBlobFileTransfer.UploadFile(info, appLocalDataFile);

            await appLocalDataFile.DeleteAsync();
        }

        private Task<string> GetCertificateDetailsHandlerAsync(string jsonParam)
        {
            Debug.WriteLine("GetCertificateDetailsHandlerAsync");

            var response = new { response = "succeeded", reason = "" };
            try
            {
                // Submit the work and return immediately.
                GetCertificateDetailsAsync(jsonParam);
            }
            catch (Exception e)
            {
                response = new { response = "rejected:", reason = e.Message };
            }

            return Task.FromResult(JsonConvert.SerializeObject(response));
        }

        private async Task FactoryResetAsync(Message.FactoryResetRequest request)
        {
            await _systemConfiguratorProxy.SendCommandAsync(request);
        }

        private async Task FactoryResetAsync(string jsonParam)
        {
            await FactoryResetAsync(JsonConvert.DeserializeObject<Message.FactoryResetRequest>(jsonParam));
        }

        public async Task FactoryResetAsync(bool clearTPM, string recoveryPartitionGUID)
        {
            var request = new Message.FactoryResetRequest();
            request.clearTPM = clearTPM;
            request.recoveryPartitionGUID = recoveryPartitionGUID;
            await FactoryResetAsync(request);
        }

        private Task<string> FactoryResetHandlerAsync(string jsonParam)
        {
            Debug.WriteLine("FactoryResetHandlerAsync");

            var response = new { response = "succeeded", reason = "" };
            try
            {
                // Submit the work and return immediately.
                FactoryResetAsync(jsonParam);
            }
            catch (Exception e)
            {
                response = new { response = "rejected:", reason = e.Message };
            }

            return Task.FromResult(JsonConvert.SerializeObject(response));
        }

        private static async void ProcessDesiredCertificateConfiguration(
            DeviceManagementClient client,
            string connectionString,
            string containerName,
            Message.CertificateConfiguration certificateConfiguration)
        {

            await IoTDMClient.CertificateManagement.DownloadCertificates(client, connectionString, containerName, certificateConfiguration);
            var request = new Message.SetCertificateConfigurationRequest(certificateConfiguration);
            client._systemConfiguratorProxy.SendCommandAsync(request);
        }

        public async Task AllowReboots(bool allowReboots)
        {
            var configuration = new WindowsUpdateRebootPolicyConfiguration();
            configuration.allow = allowReboots;
            IResponse response = await this._systemConfiguratorProxy.SendCommandAsync(new SetWindowsUpdateRebootPolicyRequest(configuration));
            if (response.Status != ResponseStatus.Success)
            {
                throw new Exception("Error: failed to set update reboot policy.");
            }
        }

        public void ProcessDeviceManagementProperties(TwinCollection desiredProperties)
        {
            // ToDo: We should not throw here. All problems need to be logged.
            Message.CertificateConfiguration certificateConfiguration = null;

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
                                case "scheduledReboot":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        Debug.WriteLine("scheduledReboot = " + managementProperty.Value.ToString());

                                        JObject subProperties = (JObject)managementProperty.Value;

                                        var request = new Message.SetRebootInfoRequest();

                                        DateTime singleRebootTime = DateTime.Parse(subProperties.Property("singleRebootTime").Value.ToString());
                                        request.singleRebootTime = singleRebootTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

                                        DateTime dailyRebootTime = DateTime.Parse(subProperties.Property("dailyRebootTime").Value.ToString());
                                        request.dailyRebootTime = dailyRebootTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

                                        this._systemConfiguratorProxy.SendCommandAsync(request);
                                    }
                                    break;
                                case "externalStorage":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        Debug.WriteLine("externalStorage = " + managementProperty.Value.ToString());

                                        JObject subProperties = (JObject)managementProperty.Value;

                                        _externalStorage.connectionString = (string)subProperties.Property("connectionString").Value;
                                        _externalStorage.containerName = (string)subProperties.Property("container").Value;
                                    }
                                    break;
                                case "certificates":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        // Capture the configuration here.
                                        // To apply the configuration we need to wait until externalStorage has been configured too.
                                        Debug.WriteLine("CertificateConfiguration = " + managementProperty.Value.ToString());
                                        certificateConfiguration = JsonConvert.DeserializeObject<CertificateConfiguration>(managementProperty.Value.ToString());
                                    }
                                    break;
                                case "timeInfo":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        Debug.WriteLine("timeInfo = " + managementProperty.Value.ToString());

                                        // Default JsonConvert Deserializing changes ISO8601 date fields to "mm/dd/yyyy hh:mm:ss".
                                        // We need to preserve the ISO8601 since that's the format SystemConfigurator understands.
                                        // Because of that, we are not using:
                                        // Message.SetTimeInfo requestInfo = JsonConvert.DeserializeObject<Message.SetTimeInfo>(fieldsJson);

                                        Message.SetTimeInfoRequest request = new Message.SetTimeInfoRequest();
                                        JObject subProperties = (JObject)managementProperty.Value;
                                        request.ntpServer = (string)subProperties.Property("ntpServer").Value;
                                        request.timeZoneBias = (int)subProperties.Property("timeZoneBias").Value;
                                        request.timeZoneDaylightBias = (int)subProperties.Property("timeZoneDaylightBias").Value;
                                        DateTime daylightDate = DateTime.Parse(subProperties.Property("timeZoneDaylightDate").Value.ToString());
                                        request.timeZoneDaylightDate = daylightDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
                                        request.timeZoneDaylightName = (string)subProperties.Property("timeZoneDaylightName").Value;
                                        request.timeZoneStandardBias = (int)subProperties.Property("timeZoneStandardBias").Value;
                                        DateTime standardDate = DateTime.Parse(subProperties.Property("timeZoneStandardDate").Value.ToString());
                                        request.timeZoneStandardDate = standardDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
                                        request.timeZoneStandardName = (string)subProperties.Property("timeZoneStandardName").Value;

                                        this._systemConfiguratorProxy.SendCommandAsync(request);
                                    }
                                    break;
                                case "windowsUpdatePolicy":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        Debug.WriteLine("windowsUpdatePolicy = " + managementProperty.Value.ToString());
                                        var configuration = JsonConvert.DeserializeObject<WindowsUpdatePolicyConfiguration>(managementProperty.Value.ToString());
                                        this._systemConfiguratorProxy.SendCommandAsync(new SetWindowsUpdatePolicyRequest(configuration));
                                    }
                                    break;
                                case "windowsUpdates":
                                    if (managementProperty.Value.Type == JTokenType.Object)
                                    {
                                        Debug.WriteLine("windowsUpdates = " + managementProperty.Value.ToString());
                                        var configuration = JsonConvert.DeserializeObject<SetWindowsUpdatesConfiguration>(managementProperty.Value.ToString());
                                        this._systemConfiguratorProxy.SendCommandAsync(new SetWindowsUpdatesRequest(configuration));
                                    }
                                    break;
                                default:
                                    // Not supported
                                    break;
                            }
                        }
                    }
                }
            }

            // Need to keep this until externalStorage is processed.
            // ToDo: The client does not get a full copy of the device twin when it first connects! (regression?)
            //       This means that the externalStorage might not get set when the machine connects.
            if (!String.IsNullOrEmpty(_externalStorage.connectionString) &&
                !String.IsNullOrEmpty(_externalStorage.containerName) &&
                certificateConfiguration != null)
            {
                ProcessDesiredCertificateConfiguration(this, _externalStorage.connectionString, _externalStorage.containerName, certificateConfiguration);
            }
        }

        private async Task<Message.GetTimeInfoResponse> GetTimeInfoAsync()
        {
            var request = new Message.GetTimeInfoRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetTimeInfoResponse);
        }

        private async Task<Message.GetCertificateConfigurationResponse> GetCertificateConfigurationAsync()
        {
            var request = new Message.GetCertificateConfigurationRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetCertificateConfigurationResponse);
        }

        private async Task<Message.GetRebootInfoResponse> GetRebootInfoAsync()
        {
            var request = new Message.GetRebootInfoRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetRebootInfoResponse);
        }

        private async Task<Message.GetDeviceInfoResponse> GetDeviceInfoAsync()
        {
            var request = new Message.GetDeviceInfoRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetDeviceInfoResponse);
        }

        private async Task<Message.GetWindowsUpdatePolicyResponse> GetWindowsUpdatePolicyAsync()
        {
            var request = new Message.GetWindowsUpdatePolicyRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetWindowsUpdatePolicyResponse);
        }

        private async Task<Message.GetWindowsUpdatesResponse> GetWindowsUpdatesAsync()
        {
            var request = new Message.GetWindowsUpdatesRequest();
            return (await this._systemConfiguratorProxy.SendCommandAsync(request) as Message.GetWindowsUpdatesResponse);
        }

        private async Task ReportAllDeviceProperties()
        {
            Debug.WriteLine("ReportAllDeviceProperties");
            Debug.WriteLine("Querying start: " + DateTime.Now.ToString());

            Message.GetTimeInfoResponse timeInfoResponse = await GetTimeInfoAsync();
            Message.GetCertificateConfigurationResponse certificateConfigurationResponse = await GetCertificateConfigurationAsync();
            Message.GetRebootInfoResponse rebootInfoResponse = await GetRebootInfoAsync();
            Message.GetDeviceInfoResponse deviceInfoResponse = await GetDeviceInfoAsync();
            Message.GetWindowsUpdatePolicyResponse windowsUpdatePolicyResponse = await GetWindowsUpdatePolicyAsync();
            Message.GetWindowsUpdatesResponse windowsUpdatesResponse = await GetWindowsUpdatesAsync();

            Debug.WriteLine("Querying end: " + DateTime.Now.ToString());

            Dictionary<string, object> collection = new Dictionary<string, object>();
            collection["microsoft"] = new
            {
                management = new
                {
                    timeInfo = timeInfoResponse,
                    certificates = certificateConfigurationResponse,
                    rebootInfo = rebootInfoResponse,
                    deviceInfo = deviceInfoResponse,
                    windowsUpdatePolicy = windowsUpdatePolicyResponse.configuration,
                    windowsUpdates = windowsUpdatesResponse.configuration
                }
            };

            _deviceTwin.ReportProperties(collection);
        }

        private async Task<string> ReportAllDevicePropertiesMethodHandler(string jsonParam)
        {
            Debug.WriteLine("ReportAllDevicePropertiesMethodHandler");

            await ReportAllDeviceProperties();

            return JsonConvert.SerializeObject(new { response = "success" });
        }

        //
        // Private utilities
        //

        private async Task SetPropertyAsync(Message.DMMessageKind command, string valueString)
        {
            throw new NotImplementedException();
        }

        private async Task<string> GetPropertyAsync(Message.DMMessageKind command)
        {
            throw new NotImplementedException();
        }

        // Data members
        ISystemConfiguratorProxy _systemConfiguratorProxy;
        IIotStartupProxy _iotStartupProxy;
        IDevicePortalCoreApiProxy _devicePortalCoreApiProxy;
        IDeviceManagementRequestHandler _requestHandler;
        IDeviceTwin _deviceTwin;
        ExternalStorage _externalStorage;
    }

}
