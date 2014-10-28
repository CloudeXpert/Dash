﻿//     Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Dash.Server.Controllers
{
    [RoutePrefix("Blob")]
    public class BlobController : CommonController
    {
        /// Get Blob - http://msdn.microsoft.com/en-us/library/azure/dd179440.aspx
        [HttpGet]
        public async Task<IHttpActionResult> GetBlob(string container, string blob, string snapshot = null)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;

            CloudBlockBlob namespaceBlob = GetBlobByName(masterAccount, container, blob);

            //Get blob metadata
            namespaceBlob.FetchAttributes();

            blobUri = new Uri(namespaceBlob.Metadata["link"]);
            accountName = namespaceBlob.Metadata["accountname"];
            accountKey = namespaceBlob.Metadata["accountkey"];
            Uri redirect = GetRedirectUri(blobUri, accountName, accountKey, container, Request);

            return Redirect(redirect);
        }

        /// Put Blob - http://msdn.microsoft.com/en-us/library/azure/dd179451.aspx
        [HttpPut]
        public async Task<IHttpActionResult> PutBlob(string container, string blob)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            CreateNamespaceBlob(Request, masterAccount);

            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            //redirection code
            Uri redirect = GetRedirectUri(blobUri, accountName, accountKey, container, Request);
            return Redirect(redirect);
        }

        /// Delete Blob - http://msdn.microsoft.com/en-us/library/azure/dd179413.aspx
        [HttpDelete]
        public async Task<IHttpActionResult> DeleteBlob(string container, string blob, string snapshot = null)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            // Set Namespace Blob for deletion
            //create a namespace blob with hardcoded metadata
            CloudBlockBlob namespaceBlob = GetBlobByName(masterAccount, container, blob);

            if (!namespaceBlob.Exists())
            {
                throw new FileNotFoundException("Namespace blob not found");
            }

            namespaceBlob.FetchAttributes();
            namespaceBlob.Metadata["todelete"] = "true";
            namespaceBlob.SetMetadata();

            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            CloudBlockBlob blobObj = GetBlobByName(masterAccount, container, blob);
            blobObj.Delete();

            return Redirect(GetRedirectUri(blobUri, accountName, accountKey, containerName, Request));
        }

        /// Get Blob Properties - http://msdn.microsoft.com/en-us/library/azure/dd179394.aspx
        [HttpHead]
        public async Task<IHttpActionResult> GetBlobProperties(string container, string blob, string snapshot = null)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            //reading metadata from namespace blob
            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            return Redirect(GetRedirectUri(blobUri, accountName, accountKey, containerName, Request));
        }

        /// Get Blob operations with the 'comp' query parameter
        [AcceptVerbs("GET", "HEAD")]
        public async Task<IHttpActionResult> GetBlobComp(string container, string blob, string comp, string snapshot = null)
        {
            switch (comp.ToLower())
            {
                case "metadata":
                    return await GetBlobMetadata(container, blob, snapshot);

                case "blocklist":
                    return await GetBlobBlockList(container, blob, snapshot);

                default:
                    return BadRequest();
            }
        }

        /// PUT Blob operations with the 'comp' query parameter
        [HttpPut]
        public async Task<IHttpActionResult> PutBlobComp(string container, string blob, string comp)
        {
            switch (comp.ToLower())
            {
                case "properties":
                    return await SetBlobProperties(container, blob);

                case "metadata":
                    return await SetBlobMetadata(container, blob);

                case "lease":
                    return await LeaseBlob(container, blob);

                case "snapshot":
                    return await SnapshotBlob(container, blob);

                case "block":
                    return await PutBlobBlock(container, blob);

                case "blocklist":
                    return await PutBlobBlockList(container, blob);

                default:
                    return BadRequest();
            }
        }

        /// <summary>
        /// Generic function to redirect a put request for properties of a blob
        /// </summary>
        /// <param name="container"></param>
        /// <param name="blob"></param>
        /// <returns></returns>
        private async Task<IHttpActionResult> SetBlobItem(string container, string blob)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            return Redirect(GetRedirectUri(blobUri, accountName, accountKey, container, Request));
        }

        /// Set Blob Properties - http://msdn.microsoft.com/en-us/library/azure/ee691966.aspx
        private async Task<IHttpActionResult> SetBlobProperties(string container, string blob)
        {
            return await SetBlobItem(container, blob);
        }

        /// Get Blob Metadata - http://msdn.microsoft.com/en-us/library/azure/dd179350.aspx
        private async Task<IHttpActionResult> GetBlobMetadata(string container, string blob, string snapshot)
        {
            return await GetBlob(container, blob, snapshot);
        }

        /// Set Blob Metadata - http://msdn.microsoft.com/en-us/library/azure/dd179414.aspx
        private async Task<IHttpActionResult> SetBlobMetadata(string container, string blob)
        {
            return await SetBlobItem(container, blob);
        }

        /// Lease Blob - http://msdn.microsoft.com/en-us/library/azure/ee691972.aspx
        private async Task<IHttpActionResult> LeaseBlob(string container, string blob)
        {
            return await SetBlobItem(container, blob);
        }

        /// Snapshot Blob - http://msdn.microsoft.com/en-us/library/azure/ee691971.aspx
        private async Task<IHttpActionResult> SnapshotBlob(string container, string blob)
        {
            // This will need to be some variation of copy. May need to replicate snapshotting logic in case
            // original server is out of space.
            await Task.Delay(10);
            return Ok();
        }

        /// Get Block List - http://msdn.microsoft.com/en-us/library/azure/dd179400.aspx
        private async Task<IHttpActionResult> GetBlobBlockList(string container, string blob, string snapshot)
        {
            await Task.Delay(10);
            return Ok();
        }

        /// Put Block - http://msdn.microsoft.com/en-us/library/azure/dd135726.aspx
        private async Task<IHttpActionResult> PutBlobBlock(string container, string blob)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            CreateNamespaceBlob(Request, masterAccount);

            //reading metadata from namespace blob
            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            return Redirect(GetRedirectUri(blobUri, accountName, accountKey, container, Request));
        }

        /// Put Block List - http://msdn.microsoft.com/en-us/library/azure/dd179467.aspx
        private async Task<IHttpActionResult> PutBlobBlockList(string container, string blob)
        {
            CloudStorageAccount masterAccount = GetMasterAccount();

            String accountName = "";
            String accountKey = "";
            Uri blobUri;
            String containerName = "";

            //reading metadata from namespace blob
            ReadMetaData(masterAccount, container, blob, out blobUri, out accountName, out accountKey, out containerName);

            //Need to figure out what to do with this one. Commented out for now.
            //forming forward request
            //base.FormForwardingRequest(blobUri, accountName, accountKey, ref Request);

            //HttpClient client = new HttpClient();
            //HttpResponseMessage response = new HttpResponseMessage();
            //response = await client.SendAsync(Request, HttpCompletionOption.ResponseContentRead);
            return Ok();
        }
    }
}
