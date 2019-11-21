/*
 * Copyright 2016 Open University of the Netherlands
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * This project has received funding from the European Union’s Horizon
 * 2020 research and innovation programme under grant agreement No 644187.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;

namespace AssetPackage
{
    /// <summary>
    ///     Request Settings.
    /// </summary>
    public class RequestSetttings
    {
        /// <summary>
        ///     The allowed responses.
        /// </summary>
        public List<int> allowedResponsCodes;

        /// <summary>
        ///     The body.
        /// </summary>
        public string body;

        /// <summary>
        ///     True to binary response.
        /// </summary>
        public bool hasBinaryResponse;

        /// <summary>
        ///     The method.
        /// </summary>
        public string method;

        /// <summary>
        ///     The request headers.
        /// </summary>
        public Dictionary<string, string> requestHeaders;

        /// <summary>
        ///     URI of the document.
        /// </summary>
        public Uri uri;

        /// <summary>
        ///     Initializes a new instance of the AssetPackage.requestParameters
        ///     class.
        /// </summary>
        public RequestSetttings()
        {
            method = "GET";
            requestHeaders = new Dictionary<string, string>();
            body = string.Empty;
            allowedResponsCodes = new List<int>();
            allowedResponsCodes.Add(200);
            hasBinaryResponse = false;
        }
    }

    /// <summary>
    ///     Response results.
    /// </summary>
    public class RequestResponse : RequestSetttings
    {
        public byte[] binaryResponse;

        /// <summary>
        ///     The response code.
        /// </summary>
        public int responseCode;

        /// <summary>
        ///     The response headers.
        /// </summary>
        public Dictionary<string, string> responseHeaders;

        /// <summary>
        ///     Message describing the respons.
        /// </summary>
        public string responsMessage;

        /// <summary>
        ///     Initializes a new instance of the AssetPackage.RequestResponse class.
        /// </summary>
        public RequestResponse()
        {
            hasBinaryResponse = false;

            binaryResponse = new byte[0];

            responseCode = 0;
            responsMessage = string.Empty;

            responseHeaders = new Dictionary<string, string>();
        }

        /// <summary>
        ///     Initializes a new instance of the AssetPackage.RequestResponse class.
        /// </summary>
        /// <remarks>
        ///     The body is not copied as it will contain thee response body instead.
        /// </remarks>
        /// <param name="settings"> Options for controlling the operation. </param>
        public RequestResponse(RequestSetttings settings) : this()
        {
            method = settings.method;
            requestHeaders = settings.requestHeaders;
            uri = settings.uri;
            body = string.Empty;

            allowedResponsCodes = settings.allowedResponsCodes;

            hasBinaryResponse = settings.hasBinaryResponse;
        }

        /// <summary>
        ///     Gets a value indicating whether result is allowed.
        /// </summary>
        /// <value>
        ///     true if result allowed, false if not.
        /// </value>
        public bool ResultAllowed
        {
            get { return allowedResponsCodes.Contains(responseCode); }
        }
    }

    /// <summary>
    ///     Interface for web service request.
    /// </summary>
    /// <remarks>
    ///     Implemented on a Bridge.
    /// </remarks>
    public interface IWebServiceRequest
    {
        /// <summary>
        ///     Web service request.
        /// </summary>
        /// <returns>
        ///     A RequestResponse.
        /// </returns>
        /// <param name="requestSettings">  Options for controlling the operation. </param>
        /// <param name="requestResponse"> The request response. </param>
        void WebServiceRequest(RequestSetttings requestSettings, out RequestResponse requestResponse);
    }
}