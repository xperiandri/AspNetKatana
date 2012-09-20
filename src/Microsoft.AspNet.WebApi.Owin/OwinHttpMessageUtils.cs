﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

namespace Microsoft.AspNet.WebApi.Owin
{
    public static class OwinHttpMessageUtils
    {
        static T Get<T>(IDictionary<string, object> env, string key)
        {
            object value;
            if (env.TryGetValue(key, out value))
            {
                return (T)value;
            }
            return default(T);
        }

        public static CancellationToken GetCancellationToken(IDictionary<string, object> env)
        {
            return Get<CancellationToken>(env, Constants.CallCancelledKey);
        }

        public static HttpRequestMessage GetRequestMessage(IDictionary<string, object> env)
        {
            var requestMethod = Get<string>(env, Constants.RequestMethodKey);
            var requestHeaders = Get<IDictionary<string, String[]>>(env, Constants.RequestHeadersKey);
            var requestBody = Get<Stream>(env, Constants.RequestBodyKey) ?? Stream.Null;
            var requestUri = CreateRequestUri(env, requestHeaders);

            var requestMessage = new HttpRequestMessage(new HttpMethod(requestMethod), requestUri)
            {
                Content = new StreamContent(requestBody)
            };

            MapRequestProperties(requestMessage, env);

            foreach (var kv in requestHeaders)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                {
                    requestMessage.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            return requestMessage;
        }

        public static Task SendResponseMessage(IDictionary<string, object> env, HttpResponseMessage responseMessage, CancellationToken cancellationToken)
        {
            env[Constants.ResponseStatusCodeKey] = responseMessage.StatusCode;
            env[Constants.ResponseReasonPhraseKey] = responseMessage.ReasonPhrase;
            var responseHeaders = Get<IDictionary<string, string[]>>(env, Constants.ResponseHeadersKey);
            var responseBody = Get<Stream>(env, Constants.ResponseBodyKey);
            foreach (var kv in responseMessage.Headers)
            {
                responseHeaders[kv.Key] = kv.Value.ToArray();
            }
            if (responseMessage.Content != null)
            {
                foreach (var kv in responseMessage.Content.Headers)
                {
                    responseHeaders[kv.Key] = kv.Value.ToArray();
                }
                return responseMessage.Content.CopyToAsync(responseBody);
            }
            return TaskHelpers.Completed();
        }

        public static Uri CreateRequestUri(IDictionary<string, object> env, IDictionary<string, string[]> requestHeaders)
        {
            var requestScheme = OwinHttpMessageUtils.Get<string>(env, Constants.RequestSchemeKey);
            var requestPathBase = OwinHttpMessageUtils.Get<string>(env, Constants.RequestPathBaseKey);
            var requestPath = OwinHttpMessageUtils.Get<string>(env, Constants.RequestPathKey);
            var requestQueryString = OwinHttpMessageUtils.Get<string>(env, Constants.RequestQueryStringKey);

            // default values, in absence of a host header
            var host = "127.0.0.1";
            var port = String.Equals(requestScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

            // if a single host header is available
            string[] hostAndPort;
            if (requestHeaders.TryGetValue("Host", out hostAndPort) &&
                hostAndPort != null &&
                hostAndPort.Length == 1 &&
                !String.IsNullOrWhiteSpace(hostAndPort[0]))
            {
                // try to parse as "host:port" format
                var delimiterIndex = hostAndPort[0].LastIndexOf(':');
                int portValue;
                if (delimiterIndex != -1 &&
                    Int32.TryParse(hostAndPort[0].Substring(delimiterIndex + 1), out portValue))
                {
                    // use those two values
                    host = hostAndPort[0].Substring(0, delimiterIndex);
                    port = portValue;
                }
                else
                {
                    // otherwise treat as host name
                    host = hostAndPort[0];
                }
            }

            var uriBuilder = new UriBuilder(requestScheme, host, port, requestPathBase + requestPath);
            if (!String.IsNullOrEmpty(requestQueryString))
            {
                uriBuilder.Query = requestQueryString;
            }
            return uriBuilder.Uri;
        }

        // Map the OWIN environment keys to the request properties keys that WebApi expects.
        // TODO: In WebApi vNext it is probably more efficient to change WebApi to consume the environment keys directly.
        private static void MapRequestProperties(HttpRequestMessage requestMessage, IDictionary<string, object> environment)
        {
            requestMessage.Properties[Constants.RequestEnvironmentKey] 
                = environment;

            // Client cert
            requestMessage.Properties[Constants.MSClientCertificateKey]
                = Get<X509Certificate2>(environment, Constants.ClientCertifiateKey);

            // IsLocal, Lazy<bool> expected.
            requestMessage.Properties[Constants.MSIsLocalKey]
                = new Lazy<bool>(() => Get<bool>(environment, Constants.IsLocalKey));

            // Remote End Point was only used by IsLocal to check for IPAddress.IsLoopback.
        }
    }
}