﻿using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Data;
using System.Text.RegularExpressions;

namespace Microsoft.WindowsAzure.Storage.TreeCopyProxy.ProxyServer
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Configuration;

    enum Operation { PutBlob, GetBlob, ListBlobs, HeadBlob, PutBlock, PutBlockList, DeleteBlob, PutContainer, CopyBlob, ListContainers, DeleteContainer, HeadContainer, OptionsMethod, GetBlobPropertiesHandler, Unknown };


    abstract class Handler
    {
        public abstract Task<HttpResponseMessage> ProcessRequest(HttpRequestMessage request);

        protected void ReadMetaData(HttpRequestMessage request, CloudStorageAccount masterAccount, out Uri blobUri, out String accountName, out String accountKey, out String containerName, out String blobName )
        {
            containerName = request.RequestUri.AbsolutePath.Substring(1, request.RequestUri.AbsolutePath.IndexOf('/', 2) - 1);
            blobName = request.RequestUri.LocalPath.Substring(containerName.Length + 2);
            

            var blobClient = masterAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            CloudBlockBlob namespaceBlob = container.GetBlockBlobReference(blobName);

            //Get blob metadata
            namespaceBlob.FetchAttributes();

            blobUri = new Uri(namespaceBlob.Metadata["link"]);
            accountName = namespaceBlob.Metadata["accountname"];
            accountKey = namespaceBlob.Metadata["accountkey"];
            containerName = namespaceBlob.Metadata["container"];
            blobName = namespaceBlob.Metadata["blobname"];
            
        }

        protected void FormForwardingRequest(Uri blobUri, String accountName, String accountKey, ref HttpRequestMessage request)
        {
            StorageCredentials credentials = new StorageCredentials(accountName, accountKey);

            CloudStorageAccount account = new CloudStorageAccount(credentials, false);

            var blobClient = account.CreateCloudBlobClient();

            string containerString = blobUri.AbsolutePath.Substring(1, blobUri.AbsolutePath.IndexOf('/', 2) - 1);

            CloudBlobContainer container = blobClient.GetContainerReference(containerString);

            string blobString = blobUri.AbsolutePath.Substring(blobUri.AbsolutePath.IndexOf('/', 2) + 1).Replace("%20"," ");

            var blob = container.GetBlockBlobReference(blobString);

            string sas = calculateSASStringForContainer(container);


            request.Headers.Host = accountName + ".blob.core.windows.net";

            //creating redirection Uri
            UriBuilder forwardUri = new UriBuilder(blob.Uri.ToString() + sas + "&" + request.RequestUri.Query.Substring(1));

            //strip off the proxy port and replace with an Http port
            forwardUri.Port = 80;

            request.RequestUri = forwardUri.Uri;

            //sets the Authorization to null, so getting the blob doesnt't use this string but sas
            request.Headers.Authorization = null;

            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            {
                request.Content = null;
            }
        }

        protected void FormRedirectResponse(Uri blobUri, String accountName, String accountKey, string containerName, string blobName, HttpRequestMessage request, ref HttpResponseMessage response)
        {
            StorageCredentials credentials = new StorageCredentials(accountName, accountKey);
            CloudStorageAccount account = new CloudStorageAccount(credentials, false);

            var blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

            string sas = calculateSASStringForContainer(container);

            ////creating redirection Uri
            //UriBuilder forwardUri = new UriBuilder(blobUri.ToString() + sas + "&" + request.RequestUri.Query.Substring(1));

            UriBuilder forwardUri;

            //creating redirection Uri
            if (request.RequestUri.Query != "")
                forwardUri = new UriBuilder(blobUri.ToString() + sas + "&" + request.RequestUri.Query.Substring(1).Replace("timeout=90", "timeout=90000"));
            else
                forwardUri = new UriBuilder(blobUri.ToString() + sas);

            //strip off the proxy port and replace with an Http port
            forwardUri.Port = 80;
            
            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
                response.Content = null;
            else
                response.Content = request.Content;

            response.StatusCode = HttpStatusCode.Moved;
            response.Headers.Location = forwardUri.Uri;
            System.Net.ServicePointManager.Expect100Continue = false;
        }

        //calculates Shared Access Signature (SAS) string based on type of request (GET, HEAD, DELETE, PUT)
        protected string calculateSASString(HttpRequestMessage request, ICloudBlob blob)
        {
            string sas = "";
            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            {
                //creating sas string (Shared Access Signature) to get permissions to access to hardcoded blob
                sas = blob.GetSharedAccessSignature(
                    new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Read,
                        SharedAccessStartTime = DateTime.Now.AddMinutes(-5),
                        SharedAccessExpiryTime = DateTime.Now.AddMinutes(54)
                    });
            }
            else if (request.Method == HttpMethod.Delete)
            {
                //creating sas string (Shared Access Signature) to get permissions to access to hardcoded blob
                sas = blob.GetSharedAccessSignature(
                    new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Delete,
                        SharedAccessStartTime = DateTime.Now.AddMinutes(-5),
                        SharedAccessExpiryTime = DateTime.Now.AddMinutes(54)
                    });
            }
            else if (request.Method == HttpMethod.Put)
            {
                sas = blob.GetSharedAccessSignature(
                    new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Write,
                        SharedAccessStartTime = DateTime.Now.AddMinutes(-5),
                        SharedAccessExpiryTime = DateTime.Now.AddMinutes(54)
                    });
            }

            return sas;
        }

        protected void CreateNamespaceBlob(HttpRequestMessage request, CloudStorageAccount masterAccount)
        {
            String accountName = "";
            String accountKey = "";

            //create an namespace blob with hardcoded metadata
            var namespaceBlobClient = masterAccount.CreateCloudBlobClient();

            string masterContainerString = request.RequestUri.AbsolutePath.Substring(1,
                                                                                     request.RequestUri.AbsolutePath
                                                                                            .IndexOf('/', 2) - 1);

            CloudBlobContainer masterContainer = namespaceBlobClient.GetContainerReference(masterContainerString);

            string masterBlobString = request.RequestUri.LocalPath.Substring(masterContainerString.Length + 2);

            CloudBlockBlob blobMaster = masterContainer.GetBlockBlobReference(masterBlobString);


            //getting storage account name and account key from file account, by using simple hashing algorithm to choose account storage
            getStorageAccount(masterAccount, masterBlobString, out accountName, out accountKey);


            if (blobMaster.Exists())
                blobMaster.FetchAttributes();
            else
                blobMaster.UploadText("");


            ////calculating client blob name as a hash value of it's real name
            //var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(masterBlobString.GetHashCode().ToString());
            //String clientBlobName = System.Convert.ToBase64String(plainTextBytes);

            blobMaster.Metadata["link"] = request.RequestUri.Scheme + "://" + accountName + ".blob.core.windows.net/" + masterContainerString + "/" + masterBlobString;
            blobMaster.Metadata["accountname"] = accountName;
            blobMaster.Metadata["accountkey"] = accountKey;
            blobMaster.Metadata["container"] = masterContainerString;
            blobMaster.Metadata["blobname"] = masterBlobString;
            blobMaster.SetMetadata();
        }

        //getting storage account name and account key from file account, by using simple hashing algorithm to choose account storage
        protected void getStorageAccount(CloudStorageAccount masterAccount, string masterBlobString, out string accountName, out string accountKey)
        {
            //var blobClientMaster = masterAccount.CreateCloudBlobClient();
            //accountName = "";
            //accountKey = "";

            //CloudBlobContainer masterContainer = blobClientMaster.GetContainerReference("accounts");

            //CloudBlockBlob blobMaster = masterContainer.GetBlockBlobReference("accounts.txt");

            //string content = blobMaster.DownloadText();

            //using (StringReader sr = new StringReader(content))
            //{
            //    //reading number of accounts
            //    Int32 numAcc = Convert.ToInt32(sr.ReadLine());

            //    //chosing number of storage account to put blob into
            //    Int64 chosenAccount = GetInt64HashCode(masterBlobString, numAcc);

            //    //reading last account used for storing, we use hashing algorithm for now so we don't actually use this number
            //    Int32 curAcc = Convert.ToInt32(sr.ReadLine());

            //    for (int i = 0; i <= chosenAccount; i++)
            //    {
            //        accountName = sr.ReadLine();
            //        accountKey = sr.ReadLine();
            //    }
            //    sr.Close();
            //}
            string ScaleoutNumberOfAccountsString = ConfigurationManager.AppSettings["ScaleoutNumberOfAccounts"];
            Int32 numAcc = Convert.ToInt32(ScaleoutNumberOfAccountsString);
            Int64 chosenAccount = GetInt64HashCode(masterBlobString, numAcc);

            string ScaleoutAccountInfo = ConfigurationManager.AppSettings["ScaleoutStorage" + chosenAccount.ToString()];

            //getting account name
            Match match1 = Regex.Match(ScaleoutAccountInfo, @"AccountName=([A-Za-z0-9\-]+);",RegexOptions.IgnoreCase);
            accountName = "";
            if (match1.Success)
                accountName = match1.Groups[1].Value;

            //getting account key
            accountKey = ScaleoutAccountInfo.Substring(ScaleoutAccountInfo.IndexOf("AccountKey=")+11);
        }



        /// <summary>
        /// Return unique Int64 value for input string
        /// </summary>
        /// <param name="strText"></param>
        /// <returns></returns>
        static Int64 GetInt64HashCode(string strText, Int32 numAcc)
        {
            Int64 hashCode = 0;
            if (!string.IsNullOrEmpty(strText))
            {
                byte[] byteContents = Encoding.UTF8.GetBytes(strText);
                System.Security.Cryptography.SHA256 hash =
                new System.Security.Cryptography.SHA256CryptoServiceProvider();
                byte[] hashText = hash.ComputeHash(byteContents);
                Int64 hashCodeStart = BitConverter.ToInt64(hashText, 0);
                Int64 hashCodeMedium = BitConverter.ToInt64(hashText, 8);
                Int64 hashCodeEnd = BitConverter.ToInt64(hashText, 24);
                hashCode = hashCodeStart ^ hashCodeMedium ^ hashCodeEnd;
            }
            return (hashCode > 0) ? hashCode % numAcc : (-hashCode) % numAcc;
        }  



        //getting storage account name and account key from file account, by using simple round-robin algorithm to choose account storage, NOT USED AT THE MOMENT
        private void getStorageAccountRoundRobin(CloudStorageAccount masterAccount, out string accountName, out string accountKey)
        {
            var blobClientMaster = masterAccount.CreateCloudBlobClient();
            accountName = "";
            accountKey = "";

            CloudBlobContainer masterContainer = blobClientMaster.GetContainerReference("accounts");

            CloudBlockBlob blobMaster = masterContainer.GetBlockBlobReference("accounts.txt");

            string content = blobMaster.DownloadText();
            StringBuilder newContent = new StringBuilder();

            using (StringReader sr = new StringReader(content))
            using (StringWriter sw = new StringWriter(newContent))
            {
                //reading number of accounts
                Int32 numAcc = Convert.ToInt32(sr.ReadLine());

                //reading last account used for storing, we use round-robin algorithm so next account is curAcc+1 (mod numAcc)
                Int32 curAcc = Convert.ToInt32(sr.ReadLine());

                //calculating next storage account for storing
                curAcc = curAcc + 1;
                if (curAcc > numAcc)
                    curAcc = 1;

                sw.WriteLine(numAcc.ToString() + "\r\n" + curAcc.ToString());
                string temp;
                for (int i = 1; i <= numAcc; i++)
                {
                    temp = sr.ReadLine();
                    sw.WriteLine(temp);

                    if (i == curAcc)
                        accountName = temp;

                    temp = sr.ReadLine();
                    sw.WriteLine(temp);

                    if (i == curAcc)
                        accountKey = temp;
                }

                sw.Close();
                sr.Close();

            }
            blobMaster.UploadText(newContent.ToString());

        }

        //reads account data and returns accountName and accountKey for currAccount (account index)
        protected void readAccountData(CloudStorageAccount masterAccount, int currAccount, out string accountName, out string accountKey)
        {
            string ScaleoutAccountInfo = ConfigurationManager.AppSettings["ScaleoutStorage" + currAccount.ToString()];

            //getting account name
            Match match1 = Regex.Match(ScaleoutAccountInfo, @"AccountName=([A-Za-z0-9\-]+);", RegexOptions.IgnoreCase);
            accountName = "";
            if (match1.Success)
                accountName = match1.Groups[1].Value;

            //getting account key
            accountKey = ScaleoutAccountInfo.Substring(ScaleoutAccountInfo.IndexOf("AccountKey=") + 11);
        }

        public static Operation GetOperationType(HttpRequestMessage request)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri.ToString().Contains("comp=list") && !request.RequestUri.ToString().Contains("/?"))
                return Operation.ListBlobs;
            if (request.Method == HttpMethod.Get && request.RequestUri.ToString().Contains("comp=list") && request.RequestUri.ToString().Contains("/?"))
                return Operation.ListContainers;
            if (request.Method == HttpMethod.Get && request.RequestUri.ToString().Contains("comp=acl"))
                return Operation.ListContainers;
            else if (request.Method == HttpMethod.Put && request.Headers.Contains("x-ms-copy-source"))
                return Operation.CopyBlob;
            else if (request.Method == HttpMethod.Put && request.RequestUri.ToString().Contains("restype=container"))
                return Operation.PutContainer;
            else if (request.RequestUri.ToString().Contains("comp=blocklist") && request.Method == HttpMethod.Put)
                return Operation.PutBlockList;
            else if (request.RequestUri.ToString().Contains("comp=block") && request.Method == HttpMethod.Put)
                return Operation.PutBlock;
            else if (request.Method == HttpMethod.Head && request.RequestUri.ToString().Contains("restype=container"))
                return Operation.HeadContainer;
            else if (request.Method == HttpMethod.Head)
                return Operation.HeadBlob;
            else if (request.Method == HttpMethod.Delete && request.RequestUri.ToString().Contains("restype=container"))
                return Operation.DeleteContainer;
            else if (request.Method == HttpMethod.Delete)
                return Operation.DeleteBlob;
            else if (request.Method == HttpMethod.Put)
                return Operation.PutBlob;
            else if (request.Method == HttpMethod.Get)
                return Operation.GetBlob;
            else if (request.Method == HttpMethod.Options)
                return Operation.OptionsMethod;
            else if (request.Method == HttpMethod.Head)
                return Operation.GetBlobPropertiesHandler;
            else
                return Operation.Unknown;
        }

        //calculates SAS string to have access to a container
        protected string calculateSASStringForContainer(CloudBlobContainer container)
        {
            string sas = "";

            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessExpiryTime = DateTime.UtcNow.AddHours(4);

            //we do not need all of those permisions
            sasConstraints.Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Delete;

            //Generate the shared access signature on the container, setting the constraints directly on the signature.
            sas = container.GetSharedAccessSignature(sasConstraints);

            return sas;
        }

        protected void GetNamesFromUri(Uri blobUri, out string containerName, out string blobName)
        {
            containerName = blobUri.AbsolutePath.Substring(1, blobUri.AbsolutePath.IndexOf('/', 2) - 1);
            blobName = System.IO.Path.GetFileName(blobUri.LocalPath);
        }

        protected CloudBlobContainer GetContainerByName(CloudStorageAccount account, string containerName)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            return client.GetContainerReference(containerName);
        }

        protected CloudBlockBlob GetBlobByName(CloudStorageAccount account, string containerName, string blobName)
        {
            CloudBlobContainer container = GetContainerByName(account, containerName);
            return container.GetBlockBlobReference(blobName);
        }

        protected CloudBlockBlob GetBlobByUri(CloudStorageAccount masterAccount, Uri blobUri)
        {
            string namespaceContainerString = "";
            string namespaceBlobString = "";
            GetNamesFromUri(blobUri, out namespaceContainerString, out namespaceBlobString);
            return GetBlobByName(masterAccount, namespaceContainerString, namespaceBlobString);
        }

        protected CloudBlobContainer ContainerFromRequest(CloudStorageAccount account, HttpRequestMessage request)
        {
            string containerString = request.RequestUri.AbsolutePath.Substring(1);

            return GetContainerByName(account, containerString);
        }

        protected string ContainerSASFromRequest(CloudStorageAccount account, HttpRequestMessage request)
        {
            CloudBlobContainer container = ContainerFromRequest(account, request);

            return calculateSASStringForContainer(container);
        }
    }


}
