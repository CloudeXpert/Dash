﻿//     Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Dash.Common.Diagnostics;
using Microsoft.Dash.Common.Handlers;
using Microsoft.Dash.Common.Utils;
using Microsoft.Dash.Server.Diagnostics;
using Microsoft.Dash.Server.Utils;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace Microsoft.Dash.Server.Handlers
{
    public static class BlobHandler
    {
        /// <summary>
        /// Generic function to redirect a put request for properties of a blob
        /// </summary>
        public static async Task<HandlerResult> BasicBlobAsync(string container, string blob)
        {
            return await OperationRunner.DoHandlerAsync("BlobHandler.BasicBlobAsync", async () =>
                {
                    var namespaceBlob = await NamespaceHandler.FetchNamespaceBlobAsync(container, blob);
                    if (!await namespaceBlob.ExistsAsync())
                    {
                        return new HandlerResult
                        {
                            StatusCode = HttpStatusCode.NotFound,
                        };
                    }
                    return HandlerResult.Redirect(ControllerOperations.GetRedirectUri(HttpContextFactory.Current.Request,
                        DashConfiguration.GetDataAccountByAccountName(namespaceBlob.AccountName),
                        namespaceBlob.Container,
                        namespaceBlob.BlobName));
                });
        }

        public static async Task<HandlerResult> PutBlobAsync(string container, string blob)
        {
            return await OperationRunner.DoHandlerAsync("BlobHandler.PutBlobAsync", async () =>
                {
                    var namespaceBlob = await NamespaceHandler.CreateNamespaceBlobAsync(container, blob);
                    //redirection code
                    Uri redirect = ControllerOperations.GetRedirectUri(HttpContextFactory.Current.Request,
                        DashConfiguration.GetDataAccountByAccountName(namespaceBlob.AccountName),
                        container,
                        blob);
                    return HandlerResult.Redirect(redirect);
                });
        }

        public static async Task<HandlerResult> CopyBlobAsync(IHttpRequestWrapper requestWrapper, string destContainer, string destBlob, string source)
        {
            return await OperationRunner.DoHandlerAsync("BlobHandler.CopyBlobAsync", async () =>
                {
                    // source is a naked URI supplied by client
                    Uri sourceUri;
                    if (Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out sourceUri))
                    {
                        string sourceContainer = String.Empty;
                        string sourceBlobName = String.Empty;
                        string sourceQuery = String.Empty;
                        var requestVersion = new DateTimeOffset(requestWrapper.Headers.Value("x-ms-version", StorageServiceVersions.Version_2009_09_19.UtcDateTime), TimeSpan.FromHours(0));
                        bool processRelativeSource = false;
                        if (!sourceUri.IsAbsoluteUri)
                        {
                            if (requestVersion >= StorageServiceVersions.Version_2012_02_12)
                            {
                                // 2012-02-12 onwards doesn't accept relative URIs
                                return new HandlerResult
                                {
                                    StatusCode = HttpStatusCode.BadRequest,
                                };
                            }
                            // Make sourceUri absolute here because a bunch of Uri functionality fails for relative URIs
                            sourceUri = new Uri(new Uri("http://dummyhost"), sourceUri);
                            processRelativeSource = true;
                        }
                        if (processRelativeSource ||
                            (String.Equals(sourceUri.Host, requestWrapper.Url.Host, StringComparison.OrdinalIgnoreCase) &&
                            ((sourceUri.IsDefaultPort && requestWrapper.Url.IsDefaultPort) || (sourceUri.Port == requestWrapper.Url.Port))))
                        {
                            var segments = sourceUri.Segments
                                .Select(segment => segment.Trim('/'))
                                .Where(segment => !String.IsNullOrWhiteSpace(segment))
                                .ToList();
                            if (processRelativeSource)
                            {
                                // Blob in named container: /accountName/containerName/blobName
                                // Snapshot in named container: /accountName/containerName/blobName?snapshot=<DateTime>
                                // Blob in root container: /accountName/blobName
                                // Snapshot in root container: /accountName/blobName?snapshot=<DateTime>
                                if (!String.Equals(segments.FirstOrDefault(), DashConfiguration.AccountName))
                                {
                                    return new HandlerResult
                                    {
                                        StatusCode = HttpStatusCode.BadRequest,
                                        ErrorInformation = new DashErrorInformation
                                        {
                                            ErrorCode = "CopyAcrossAccountsNotSupported",
                                            ErrorMessage = "The copy source account and destination account must be the same.",
                                        },
                                    };
                                }
                                if (segments.Count() == 2)
                                {
                                    sourceContainer = "root";
                                    sourceBlobName = segments[1];
                                }
                                else if (segments.Count() > 2)
                                {
                                    sourceContainer = segments[1];
                                    sourceBlobName = String.Join("/", segments.Skip(2));
                                }
                            }
                            else
                            {
                                sourceContainer = segments.FirstOrDefault();
                                sourceBlobName = String.Join("/", segments.Skip(1));
                            }
                        }
                        var destNamespaceBlob = await NamespaceHandler.FetchNamespaceBlobAsync(destContainer, destBlob);
                        string destAccount = String.Empty;
                        if (!String.IsNullOrEmpty(sourceContainer) && !String.IsNullOrEmpty(sourceBlobName))
                        {
                            var sourceQueryParams = HttpUtility.ParseQueryString(sourceUri.Query);
                            var sourceNamespaceBlob = await NamespaceHandler.FetchNamespaceBlobAsync(sourceContainer, sourceBlobName, sourceQueryParams["snapshot"]);
                            if (!await sourceNamespaceBlob.ExistsAsync())
                            {
                                // This isn't actually documented (what happens when the source doesn't exist), but by obervation the service emits 404
                                return new HandlerResult
                                {
                                    StatusCode = HttpStatusCode.NotFound,
                                };
                            }
                            // This is effectively an intra-account copy which is expected to be atomic. Therefore, even if the destination already
                            // exists, we need to place the destination in the same data account as the source.
                            // If the destination blob already exists, we delete it below to prevent an orphaned data blob
                            destAccount = sourceNamespaceBlob.AccountName;
                            var sourceUriBuilder = ControllerOperations.GetRedirectUriBuilder("GET",
                                requestWrapper.Url.Scheme,
                                DashConfiguration.GetDataAccountByAccountName(sourceNamespaceBlob.AccountName),
                                sourceContainer,
                                sourceBlobName,
                                false);
                            sourceUri = sourceUriBuilder.Uri;
                        }
                        else if (await destNamespaceBlob.ExistsAsync())
                        {
                            destAccount = destNamespaceBlob.AccountName;
                        }
                        else
                        {
                            destAccount = NamespaceHandler.GetDataStorageAccountForBlob(destBlob).Credentials.AccountName;
                        }
                        if (await destNamespaceBlob.ExistsAsync() && destNamespaceBlob.AccountName != destAccount)
                        {
                            // Delete the existing blob to prevent orphaning it
                            var dataBlob = NamespaceHandler.GetBlobByName(DashConfiguration.GetDataAccountByAccountName(destNamespaceBlob.AccountName), destContainer, destBlob);
                            await dataBlob.DeleteIfExistsAsync();
                        }
                        destNamespaceBlob.AccountName = destAccount;
                        destNamespaceBlob.Container = destContainer;
                        destNamespaceBlob.BlobName = destBlob;
                        destNamespaceBlob.IsMarkedForDeletion = false;
                        await destNamespaceBlob.SaveAsync();
                        // Now that we've got the metadata tucked away - do the actual copy
                        var destCloudContainer = NamespaceHandler.GetContainerByName(DashConfiguration.GetDataAccountByAccountName(destAccount), destContainer);
                        var destCloudBlob = destCloudContainer.GetBlockBlobReference(destBlob);
                        // Storage client will retry failed copy. Let our clients decide that.
                        var copyId = await destCloudBlob.StartCopyFromBlobAsync(sourceUri,
                            AccessCondition.GenerateEmptyCondition(),
                            AccessCondition.GenerateEmptyCondition(),
                            new BlobRequestOptions
                            {
                                RetryPolicy = new NoRetry(),
                            },
                            new OperationContext());
                        return new HandlerResult
                        {
                            StatusCode = requestVersion >= StorageServiceVersions.Version_2012_02_12 ? HttpStatusCode.Accepted : HttpStatusCode.Created,
                            Headers = new ResponseHeaders(new[] 
                            {
                                new KeyValuePair<string, string>("x-ms-copy-id", copyId),
                                new KeyValuePair<string, string>("x-ms-copy-status", destCloudBlob.CopyState.Status == CopyStatus.Success ? "success" : "pending"),
                            })
                        };
                    }
                    return new HandlerResult
                    {
                        StatusCode = HttpStatusCode.BadRequest,
                    };
                });
        }

        public static async Task<HandlerResult> AbortCopyBlobAsync(IHttpRequestWrapper requestWrapper, string destContainer, string destBlob, string copyId)
        {
            return await OperationRunner.DoHandlerAsync("BlobHandler.AbortCopyBlobAsync", async () =>
                {
                    var destNamespaceBlob = await NamespaceHandler.FetchNamespaceBlobAsync(destContainer, destBlob);

                    var destCloudBlob = NamespaceHandler.GetBlobByName(DashConfiguration.GetDataAccountByAccountName(destNamespaceBlob.AccountName), destContainer, destBlob);
                    await destCloudBlob.AbortCopyAsync(copyId);
                    return new HandlerResult
                    {
                        StatusCode = HttpStatusCode.NoContent,
                    };
                });
        }
    }
}