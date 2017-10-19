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
using Microsoft.Devices.Management;
using Microsoft.Devices.Management.Message;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using System.Linq;

namespace IoTDMClient
{
    internal class BlobInfo
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }

        public async Task<string> DownloadToTempAsync(DeviceManagementClient client)
        {   
            var path = DMGarbageCollector.TempFolder + BlobName.Split('/', '\\').LastOrDefault(); ;

            var info = new AzureFileTransferInfo()
            {
                ConnectionString = ConnectionString,
                ContainerName = ContainerName,
                BlobName = BlobName,
                Upload = false,

                LocalPath = path,                
            };

            await AzureBlobFileTransfer.TransferFileAsync(info, client);
            return path;
        }
    }

    internal static class AzureBlobFileTransfer
    {
        private static async Task<CloudBlockBlob> GetBlob(AzureFileTransferInfo transferInfo, bool ensureContainerExists)
        {
            // Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(transferInfo.ConnectionString);

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve a reference to a container.
            CloudBlobContainer container = blobClient.GetContainerReference(transferInfo.ContainerName);

            if (ensureContainerExists)
            {
                // Create the container if it doesn't already exist.
                await container.CreateIfNotExistsAsync();
            }

            // Retrieve reference to a named blob.
            return container.GetBlockBlobReference(transferInfo.BlobName);
        }

        public static async Task<string> DownloadFile(AzureFileTransferInfo transferInfo, StorageFile appLocalDataFile)
        {
            var appLocalDataPath = appLocalDataFile.Path;

            var blockBlob = await GetBlob(transferInfo, false);

            // Save blob contents to a file.
            await blockBlob.DownloadToFileAsync(appLocalDataFile);

            return appLocalDataPath;
        }

        public static async Task UploadFile(AzureFileTransferInfo transferInfo, StorageFile appLocalDataFile)
        {
            var blockBlob = await GetBlob(transferInfo, true);

            // Save blob contents to a file.
            await blockBlob.UploadFromFileAsync(appLocalDataFile);
        }

        public static async Task TransferFileAsync(AzureFileTransferInfo transferInfo, DeviceManagementClient client)
        {
            //
            // C++ Azure Blob SDK not supported for ARM, so use Service to copy file to/from
            // App's LocalData and then use C# Azure Blob SDK to transfer
            //
            var appLocalDataFilename = transferInfo.LocalPath.Split('\\').LastOrDefault();            
            var appLocalDataFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(appLocalDataFilename, CreationCollisionOption.ReplaceExisting);
            transferInfo.AppLocalDataPath = appLocalDataFile.Path;

            if (!transferInfo.Upload)
            {
                transferInfo.AppLocalDataPath = await DownloadFile(transferInfo, appLocalDataFile);
            }

            await client.TransferFileAsync(transferInfo);

            if (transferInfo.Upload)
            {
                await UploadFile(transferInfo, appLocalDataFile);
            }

            await appLocalDataFile.DeleteAsync();

        }

    }
}
