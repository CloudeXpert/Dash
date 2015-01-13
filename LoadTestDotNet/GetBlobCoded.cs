﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.34014
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace LoadTestDotNet
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.WebTesting;
    using System.Security.Cryptography;


    public class GetBlobCoded : WebTest
    {
        static PropertyInfo _refererRequest;

        static GetBlobCoded()
        {
            _refererRequest = typeof(WebTestRequest).GetProperty("RefererRequest", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public GetBlobCoded()
        {
            bool dash = true;
            if (dash)
            {
                this.Context.Add("StorageEndPoint", "http://dashjbtest.cloudapp.net");
                this.Context.Add("StorageAccount", "dashlocal");
                this.Context.Add("AccountKey", "wCNvIdXcltACBiDUMyO0BflZpKmjseplqOlzE62tx87qnkwpUMBV/GQhrscW9lmdZVT0x8DilYqUoHMNBlVIGg==");
                this.Context.Add("SendChunked", true);
            }
            else
            {
                this.Context.Add("StorageEndPoint", "http://dashstorage3.blob.core.windows.net");
                this.Context.Add("StorageAccount", "dashstorage3");
                this.Context.Add("AccountKey", "TP+G/9FTZRP1he1EpKilMercxSbMyqtaI9xTbc/3HqT2/FkxyIk1wVlBdemDFuYKStmlkFqHc7049l8McTd8NQ==");
                this.Context.Add("SendChunked", false);
            }
        }

        public override IEnumerator<WebTestRequest> GetRequestEnumerator()
        {
            var sp = ServicePointManager.FindServicePoint(new Uri((string)this.Context["StorageEndPoint"]));
            string blobname = String.Format("/anonymouscontainertest/{0}", Guid.NewGuid());
            var request = CreateRequest("Put Blob", blobname, "PUT", 201);
            request.Body = new BinaryHttpBody
            {
                ContentType = "application/octet-stream",
                Data = File.ReadAllBytes("testblob"),
            };
            string requestDate = DateTime.UtcNow.ToString("R");
            request.Headers.Add("x-ms-blob-type", "BlockBlob");
            request.Headers.Add("x-ms-date", requestDate);
            request.Headers.Add("Authorization", String.Format("SharedKeyLite {0}:{1}",
                this.Context["StorageAccount"],
                GetAuthSignature("PUT", "application/octet-stream", "", blobname, request.Headers)));
            if (Convert.ToBoolean(this.Context["SendChunked"]))
            {
                request.SendChunked = true;
            }
            request.PreRequest += request_PreRequest;
            yield return request;

            yield return CreateRequest("Get Blob", blobname, "GET", 200);

            request = CreateRequest("Delete Blob", blobname, "DELETE", 202);
            request.Headers.Add("x-ms-date", requestDate);
            request.Headers.Add("Authorization", String.Format("SharedKeyLite {0}:{1}",
                this.Context["StorageAccount"],
                GetAuthSignature("DELETE", "", "", blobname, request.Headers)));
            yield return request;
        }

        void request_PreRequest(object sender, PreRequestEventArgs e)
        {
            if (e.Request.IsRedirectFollow)
            {
                var refererRequest = (WebTestRequest)_refererRequest.GetValue(e.Request);
                e.Request.Method = refererRequest.Method;
                e.Request.Body = refererRequest.Body;
                e.Request.Headers.Remove("Authorization");
            }
        }

        WebTestRequest CreateRequest(string reportingName, string path, string method, int expectedResponse)
        {
            var request = new WebTestRequest((this.Context["StorageEndPoint"].ToString() + path))
            {
                ReportingName = reportingName,
                Encoding = System.Text.Encoding.GetEncoding("utf-8"),
                ExpectedHttpStatusCode = expectedResponse,
                Method = method,
            };
            request.Headers.Add(new WebTestRequestHeader("x-ms-version", "2014-02-14"));
            request.Headers.Add(new WebTestRequestHeader("x-ms-client-request-id", "adfb540e-1050-4c9b-a53a-be6cb71688d3"));

            return request;
        }

        string GetAuthSignature(string verb, string contentType, string requestDate, string uriPath, WebTestRequestHeaderCollection headers)
        {
            string stringToSign = String.Format("{0}\n\n{1}\n{2}\n{3}{4}",
                verb,
                contentType,
                requestDate,
                CanonicalHeaders(headers),
                CanonicalResource(uriPath));
            var bytesToSign = Encoding.UTF8.GetBytes(stringToSign);
            using (var hmac = new HMACSHA256(Convert.FromBase64String((string)this.Context["AccountKey"])))
            {
                return Convert.ToBase64String(hmac.ComputeHash(bytesToSign));
            }
        }

        string CanonicalHeaders(WebTestRequestHeaderCollection headers)
        {
            return String.Join("", headers
                .Where(header => header.Name.StartsWith("x-ms"))
                .Select(header => Tuple.Create(header.Name.ToLower(), header.Value))
                .OrderBy(header => header.Item1)
                .Select(header => String.Format("{0}:{1}\n", header.Item1, header.Item2)));
        }

        string CanonicalResource(string uriPath)
        {
            return "/" + 
                this.Context["StorageAccount"] + 
                uriPath;
        }
    }
}
