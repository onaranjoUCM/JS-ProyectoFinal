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

using System.Collections.Generic;
using System.Linq;

namespace AssetManagerPackage
{
    /// <summary>
    ///     A Broadcast Messages class.
    /// </summary>
    public static class Messages
    {
        #region Delegates

        /// <summary>
        ///     Interface for broadcast message callback.
        /// </summary>
        /// <param name="message"> The message id. </param>
        /// <param name="parameters">  A variable-length parameters list containing arguments. </param>
        public delegate void MessagesEventCallback(string message, params object[] parameters);

        #endregion Delegates

        #region Fields

        /// <summary>
        ///     The subscription ID generator.
        /// </summary>
        private static int idGenerator;

        /// <summary>
        ///     The messages is a dictionary of messages and their subscribers.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, MessagesEventCallback>> messages =
            new Dictionary<string, Dictionary<string, MessagesEventCallback>>();

        #endregion Fields

        #region Methods

        /// <summary>
        ///     Define a broadcast message.
        /// </summary>
        /// <param name="message"> The message. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public static bool define(string message)
        {
            if (!messages.Keys.Contains(message))
            {
                messages.Add(message, new Dictionary<string, MessagesEventCallback>());

                return true;
            }

            return false;
        }

        /// <summary>
        ///     Broadcast a message.
        /// </summary>
        /// <param name="message"> The message to broadcast. </param>
        /// <param name="parameters">
        ///     A variable-length parameters list containing
        ///     arguments.
        /// </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public static bool broadcast(string message, params object[] parameters)
        {
            if (!messages.Keys.Contains(message))
            {
                return false;
            }

            foreach (var func in messages[message])
            {
                func.Value(message, parameters);
            }

            return true;
        }

        /// <summary>
        ///     Subscribe to a broadcast message.
        /// </summary>
        /// <remarks>
        ///     if the message does not exist yet it's created on-the-fly.
        /// </remarks>
        /// <param name="message">  The message. </param>
        /// <param name="callback"> The callback function. </param>
        /// <returns>
        ///     The broadcast subscription id.
        /// </returns>
        public static string subscribe(string message, MessagesEventCallback callback)
        {
            if (!messages.Keys.Contains(message))
            {
                messages.Add(message, new Dictionary<string, MessagesEventCallback>());
            }

            var subscriptionId = (++idGenerator).ToString();

            messages[message].Add(subscriptionId, callback);

            return subscriptionId;
        }

        /// <summary>
        ///     Unsubscribes the given broadcast subscription id.
        /// </summary>
        /// <param name="subscriptionId"> The broadcast subscription id. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public static bool unsubscribe(string subscriptionId)
        {
            foreach (var message in messages.Keys)
            {
                var subscribers = messages[message];

                foreach (var subscriber in subscribers.Keys)
                {
                    if (subscriptionId.Equals(subscriber))
                    {
                        subscribers.Remove(subscriber);

                        return true;
                    }
                }
            }

            return false;
        }

        #endregion Methods
    }
}