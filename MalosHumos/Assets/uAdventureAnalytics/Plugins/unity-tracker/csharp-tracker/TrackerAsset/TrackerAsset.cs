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

//#define ASYNC

#undef ASYNC

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using AssetPackage.Exceptions;
using AssetPackage.Utils;
using SimpleJSON;

namespace AssetPackage
{
#if ASYNC
    using System.Threading;
#endif

    [Obsolete("Use TrackerAsset instead")]
    public class Tracker
    {
        public static TrackerAsset Instance
        {
            get { return TrackerAsset.Instance; }
        }
    }

    /// <summary>
    ///     A tracker asset.
    ///     <list type="number">
    ///         <item>
    ///             <term>TODO</term><desciption> - Add method to return the mime-type/content-type.</desciption>
    ///         </item>
    ///         <item>
    ///             <term>TODO</term><desciption> - Add method to return the accept-type.</desciption>
    ///         </item>
    ///         <item>
    ///             <term>TODO</term><desciption> - Check disk based/off-line storage (local).</desciption>
    ///         </item>
    ///         <item>
    ///             <term>TODO</term><desciption> - Serialize Queue for later submission (using queue.ToList()).</desciption>
    ///         </item>
    ///         <item>
    ///             <term>TODO</term>
    ///             <desciption> - Prevent csv/xml/json from net storage and xapi from local storage.</desciption>
    ///         </item>
    ///     </list>
    /// </summary>
    public class TrackerAsset : BaseAsset
    {
        #region Fields

        public static DateTime START_DATE = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

#if ASYNC
        readonly object flushLockObject = new object();
        private Thread flushThread;
#endif
        /// <summary>
        ///     True when the thread must exit.
        /// </summary>
        private bool exit;

        /// <summary>
        ///     The RegEx to extract a JSON Object. Used to extract 'actor'.
        /// </summary>
        /// <remarks>
        ///     NOTE: This regex handles matching brackets by using balancing groups. This should be tested in Mono if it works
        ///     there too.<br />
        ///     NOTE: {} brackets must be escaped as {{ and }} for String.Format statements.<br />
        ///     NOTE: \ must be escaped as \\ in strings.<br />
        /// </remarks>
        private const string ObjectRegEx =
            "\"{0}\":(" + // {0} is replaced by the proprty name, capture only its value in {} brackets.
            "\\{{" + // Start with a opening brackets.
            "(?>" +
            "    [^{{}}]+" + // Capture each non bracket chracter.
            "    |    \\{{ (?<number>)" + // +1 for opening bracket.
            "    |    \\}} (?<-number>)" + // -1 for closing bracket.
            ")*" +
            "(?(number)(?!))" + // Handle unaccounted left brackets with a fail.
            "\\}})"; // Stop at matching bracket.

        //private const string ObjectRegEx = "\"{0}\":(\\{{(?:.+?)\\}},)";
        /// <summary>
        ///     Filename of the settings file.
        /// </summary>
        private const string SettingsFileName = "TrackerAssetSettings.xml";

        /// <summary>
        ///     The TimeStamp Format.
        /// </summary>
        private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>
        ///     The RegEx to extract a plain quoted JSON Value. Used to extract 'token'.
        /// </summary>
        private const string TokenRegEx = "\"{0}\":\"(.+?)\"";

        /// <summary>
        ///     The instance.
        /// </summary>
        private static TrackerAsset _instance;

        /// <summary>
        ///     The instance.
        /// </summary>
        public TrackerAssetUtils Utils { get; set; }

        /// <summary>
        ///     Identifier for the object.
        ///     Extracted from JSON inside Success().
        /// </summary>
        private string ObjectId = string.Empty;

        /// <summary>
        ///     Tracker StrictMode
        /// </summary>
        private bool strictMode = true;

        /// <summary>
        ///     Tracker StrictMode
        /// </summary>
        public bool StrictMode
        {
            get { return strictMode; }
            set { strictMode = value; }
        }

        /// <summary>
        ///     A Regex to extact the actor object from JSON.
        /// </summary>
        private readonly Regex jsonActor = new Regex(string.Format(ObjectRegEx, "actor"),
            RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);

        /// <summary>
        ///     A Regex to extact the authentication token value from JSON.
        /// </summary>
        private readonly Regex jsonAuthToken =
            new Regex(string.Format(TokenRegEx, "authToken"), RegexOptions.Singleline);

        /// <summary>
        ///     A Regex to extact the playerid token value from JSON.
        /// </summary>
        private readonly Regex jsonPlayerId = new Regex(string.Format(TokenRegEx, "playerId"), RegexOptions.Singleline);

        /// <summary>
        ///     A Regex to extact the session token value from JSON.
        /// </summary>
        private readonly Regex jsonSession = new Regex(string.Format(TokenRegEx, "session"), RegexOptions.Singleline);

        /// <summary>
        ///     A Regex to extact the objectId value from JSON.
        /// </summary>
        private readonly Regex jsonObjectId = new Regex(string.Format(TokenRegEx, "objectId"), RegexOptions.Singleline);

        /// <summary>
        ///     A Regex to extact the token value from JSON.
        /// </summary>
        private readonly Regex jsonToken = new Regex(string.Format(TokenRegEx, "token"), RegexOptions.Singleline);

        /// <summary>
        ///     A Regex to extact the status value from JSON.
        /// </summary>
        private readonly Regex jsonHealth = new Regex(string.Format(TokenRegEx, "status"), RegexOptions.Singleline);

        /// <summary>
        ///     The queue of TrackerEvents to Send.
        /// </summary>
        private ConcurrentQueue<TrackerEvent> queue = new ConcurrentQueue<TrackerEvent>();

        /// <summary>
        ///     The list of traces flushed while the connection was offline
        /// </summary>
        private List<string> tracesPending = new List<string>();

        /// <summary>
        ///     The list of traces sent when net storage unable to start
        /// </summary>
        private readonly List<TrackerEvent> tracesUnlogged = new List<TrackerEvent>();

        /// <summary>
        ///     Options for controlling the operation.
        /// </summary>
        private TrackerAssetSettings settings;

        /// <summary>
        ///     List of Extensions that have to ve added to the next trace
        /// </summary>
        private readonly Dictionary<string, object> extensions = new Dictionary<string, object>();

        #region SubTracker Fields

        /// <summary>
        ///     Instance of AccessibleTracker
        /// </summary>
        private AccessibleTracker accessibletracker;

        /// <summary>
        ///     Instance of AlternativeTracker
        /// </summary>
        private AlternativeTracker alternativetracker;

        /// <summary>
        ///     Instance of CompletableTracker
        /// </summary>
        private CompletableTracker completabletracker;

        /// <summary>
        ///     Instance of GameObjectTracker
        /// </summary>
        private GameObjectTracker gameobjecttracer;

        #endregion SubTracker Fields

        #endregion Fields

        #region Constructors

        /// <summary>
        ///     Prevents a default instance of the TrackerAsset class from being created.
        /// </summary>
        public TrackerAsset()
        {
            Utils = new TrackerAssetUtils(this);
            settings = new TrackerAssetSettings();

            if (LoadSettings(SettingsFileName))
            {
                // ok
            }
            else
            {
                settings.Secure = true;
                settings.Host = "rage.e-ucm.es";
                settings.Port = 443;
                settings.BasePath = "/api/";

                settings.UserToken = "";
                settings.TrackingCode = "";
                settings.StorageType = StorageTypes.local;
                settings.TraceFormat = TraceFormats.csv;
                settings.BatchSize = 10;

                SaveSettings(SettingsFileName);
            }
        }

        #endregion Constructors

        #region Enumerations

        /// <summary>
        ///     Values that represent events.
        /// </summary>
        public enum Events
        {
            /// <summary>
            ///     An enum constant representing the choice option.
            /// </summary>
            choice,

            /// <summary>
            ///     An enum constant representing the click option.
            /// </summary>
            click,

            /// <summary>
            ///     An enum constant representing the screen option.
            /// </summary>
            screen,

            /// <summary>
            ///     An enum constant representing the variable option.
            /// </summary>
            var,

            /// <summary>
            ///     An enum constant representing the zone option.
            /// </summary>
            zone
        }

        /// <summary>
        ///     Values that represent storage types.
        /// </summary>
        public enum StorageTypes
        {
            /// <summary>
            ///     An enum constant representing the network option.
            /// </summary>
            net,

            /// <summary>
            ///     An enum constant representing the local option.
            /// </summary>
            local
        }

        /// <summary>
        ///     Values that represent trace formats.
        /// </summary>
        public enum TraceFormats
        {
            /// <summary>
            ///     An enum constant representing the JSON option.
            /// </summary>
            json,

            /// <summary>
            ///     An enum constant representing the XML option.
            /// </summary>
            xml,

            /// <summary>
            ///     An enum constant representing the xAPI option.
            /// </summary>
            xapi,

            /// <summary>
            ///     An enum constant representing the CSV option.
            /// </summary>
            csv
        }

        /// <summary>
        ///     Values that represent the available verbs for traces.
        /// </summary>
        public enum Verb
        {
            Initialized,
            Progressed,
            Completed,
            Accessed,
            Skipped,
            Selected,
            Unlocked,
            Interacted,
            Used
        }

        /// <summary>
        ///     Values that represent the different extensions for traces.
        /// </summary>
        public enum Extension
        {
            /* Special extensions, 
               those extensions are stored reparatedly in xAPI, e.g.:
               result: {
                    score: {
                        raw: <score_value: float>
                    },
                    success: <success_value: bool>,
                    completion: <completion_value: bool>,
                    response: <response_value: string>
                    ...
               }


            */
            Score,
            Success,
            Response,
            Completion,

            /* Common extensions, these extensions are stored 
               in the result.extensions object (in the xAPI format), e.g.:

               result: {
                    ...
                    extensions: {
                        .../health: <value>,
                        .../position: <value>,
                        .../progress: <value>
                    }
               }
            */
            Health,
            Position,
            Progress
        }

        #endregion Enumerations

        #region Properties

        /// <summary>
        ///     Visible when reflecting.
        /// </summary>
        /// <value>
        ///     The instance.
        /// </value>
        public static TrackerAsset Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TrackerAsset();
                }

                return _instance;
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the tracker has been started
        /// </summary>
        /// <value>
        ///     true if started, false if not.
        /// </value>
        public bool Started { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the connection active (ie the ActorObject
        ///     and ObjectId have been extracted).
        /// </summary>
        /// <value>
        ///     true if active, false if not.
        /// </value>
        public bool Active { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the connected (ie a UserToken is present and no Fail() has occurred).
        /// </summary>
        /// <value>
        ///     true if connected, false if not.
        /// </value>
        public bool Connected { get; private set; }

        /// <summary>
        ///     Gets the health.
        /// </summary>
        /// <value>
        ///     The health.
        /// </value>
        public string Health { get; private set; }

        /// <summary>
        ///     Gets or sets options for controlling the operation.
        /// </summary>
        /// <remarks>
        ///     Besides the toXml() and fromXml() methods, we never use this property but use
        ///     it's correctly typed backing field 'settings' instead.
        /// </remarks>
        /// <remarks> This property should go into each asset having Settings of its own. </remarks>
        /// <remarks>
        ///     The actual class used should be derived from BaseAsset (and not directly from
        ///     ISetting).
        /// </remarks>
        /// <value>
        ///     The settings.
        /// </value>
        public override ISettings Settings
        {
            get { return settings; }
            set { settings = value as TrackerAssetSettings; }
        }

        /// <summary>
        ///     The actor object.
        ///     Extracted from JSON inside Success().
        /// </summary>
        private JSONNode ActorObject { get; set; }

        #region SubTracker Properties

        /// <summary>
        ///     Access point for Accessible Traces generation
        /// </summary>
        public AccessibleTracker Accessible
        {
            get
            {
                if (accessibletracker == null)
                {
                    accessibletracker = new AccessibleTracker();
                    accessibletracker.setTracker(this);
                }

                return accessibletracker;
            }
        }

        /// <summary>
        ///     Access point for Alternative Traces generation
        /// </summary>
        public AlternativeTracker Alternative
        {
            get
            {
                if (alternativetracker == null)
                {
                    alternativetracker = new AlternativeTracker();
                    alternativetracker.setTracker(this);
                }

                return alternativetracker;
            }
        }

        /// <summary>
        ///     Access point for Completable Traces generation
        /// </summary>
        public CompletableTracker Completable
        {
            get
            {
                if (completabletracker == null)
                {
                    completabletracker = new CompletableTracker();
                    completabletracker.setTracker(this);
                }

                return completabletracker;
            }
        }

        /// <summary>
        ///     Access point for Completable Traces generation
        /// </summary>
        public GameObjectTracker GameObject
        {
            get
            {
                if (gameobjecttracer == null)
                {
                    gameobjecttracer = new GameObjectTracker();
                    gameobjecttracer.setTracker(this);
                }

                return gameobjecttracer;
            }
        }

        /// <summary>
        ///     Access point for Accessible Traces generation
        /// </summary>
        [Obsolete("Use TrackerAsset.Accessible")]
        public AccessibleTracker accessible
        {
            get { return Accessible; }
        }

        /// <summary>
        ///     Access point for Alternative Traces generation
        /// </summary>
        [Obsolete("Use TrackerAsset.Alternative")]
        public AlternativeTracker alternative
        {
            get { return Alternative; }
        }

        /// <summary>
        ///     Access point for Completable Traces generation
        /// </summary>
        [Obsolete("Use TrackerAsset.Completable")]
        public CompletableTracker completable
        {
            get { return Completable; }
        }

        /// <summary>
        ///     Access point for Completable Traces generation
        /// </summary>
        [Obsolete("Use TrackerAsset.GameObject")]
        public GameObjectTracker trackedGameObject
        {
            get { return GameObject; }
        }

        #endregion SubTracker Properties

        #endregion Properties

        #region Methods

        /// <summary>
        ///     Checks the health of the UCM Tracker.
        /// </summary>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public bool CheckHealth()
        {
            var response = IssueRequest("health", "GET");

            if (response.ResultAllowed)
            {
                if (jsonHealth.IsMatch(response.body))
                {
                    Health = jsonHealth.Match(response.body).Groups[1].Value;

                    Log(Severity.Information, "Health Status={0}", Health);
                }
            }
            else
            {
                Log(Severity.Error, "Request Error: {0}-{1}", response.responseCode, response.responsMessage);
            }

            return response.ResultAllowed;
        }

        /// <summary>
        ///     Flushes the queue.
        /// </summary>
        public void Flush()
        {
#if ASYNC
            // If its waiting for flush, lets wake it up
            if (flushThread.IsAlive)
            {
                if (!flushing)
                    lock (flushLockObject)
                    {
                        Monitor.Pulse(flushLockObject);
                    }
                else
                    this.redo_flush = true;
            }
#else
            ProcessQueue();
#endif
        }

#if ASYNC
        bool redo_flush = false;
        bool flushing = false;
        /// <summary>
        /// Real flushes (to be called from the thread)
        /// </summary>
        private void DoFlush()
        {
            lock (flushLockObject)
            {
                while (!exit)
                {
                    Monitor.Wait(flushLockObject);
                    {
                        flushing = true;
                        ProcessQueue();
                        if (redo_flush)
                        {
                            redo_flush = false;
                            ProcessQueue();
                        }
                        flushing = false;
                    }
                }
            }
        }
#endif

        /// <summary>
        ///     Flushes the queue.
        /// </summary>
        [Obsolete("Use Flush instead.")]
        public void RequestFlush()
        {
            Flush();
        }


        /// <summary>
        ///     Login with a Username and Password.
        ///     After this call, the Success method will extract the token from the returned .
        /// </summary>
        /// <param name="username"> The username. </param>
        /// <param name="password"> The password. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public bool Login(string username, string password)
        {
            var logged = false;
            var headers = new Dictionary<string, string>();

            headers.Add("Content-Type", "application/json");
            headers.Add("Accept", "application/json");

            var response = IssueRequest("login", "POST", headers,
                string.Format("{{\r\n \"username\": \"{0}\",\r\n \"password\": \"{1}\"\r\n}}",
                    username, password));

            if (response.ResultAllowed)
            {
                if (jsonToken.IsMatch(response.body))
                {
                    settings.UserToken = jsonToken.Match(response.body).Groups[1].Value;
                    if (settings.UserToken.StartsWith("Bearer "))
                    {
                        settings.UserToken.Remove(0, "Bearer ".Length);
                    }

                    Log(Severity.Information, "Token= {0}", settings.UserToken);

                    logged = true;
                }
            }
            else
            {
                logged = false;
                Log(Severity.Error, "Request Error: {0}-{1}", response.responseCode, response.responsMessage);
            }

            return logged;
        }

        /// <summary>
        ///     Login with an Anonymous PlayerId
        /// </summary>
        /// <param name="anonymousId"> The playerId of the anonymous player </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        public bool Login(string anonymousId)
        {
            settings.PlayerId = anonymousId;
            return true;
        }

        /// <summary>
        ///     Starts with a userToken and trackingCode.
        /// </summary>
        /// <param name="userToken">    The user token. </param>
        /// <param name="trackingCode"> The tracking code. </param>
        public void Start(string userToken, string trackingCode)
        {
            settings.UserToken = userToken;
            settings.TrackingCode = trackingCode;

            Start();
        }

        /// <summary>
        ///     Starts with a trackingCode (and with the already extracted UserToken).
        /// </summary>
        /// <param name="trackingCode"> The tracking code. </param>
        public void Start(string trackingCode)
        {
            settings.TrackingCode = trackingCode;

            Start();
        }

        /// <summary>
        ///     Starts Tracking with: 1) An already extracted UserToken (from Login) and
        ///     2) TrackingCode (Shown at Game on a2 server).
        /// </summary>
        public void Start()
        {
            Started = true;

            switch (settings.StorageType)
            {
                case StorageTypes.net:
                    Connect();
                    break;

                case StorageTypes.local:
                {
                    // Allow LocalStorage if a Bridge is implementing IDataStorage.
                    // 
                    var tmp = getInterface<IDataStorage>();

                    Connected = tmp != null;
                    Active = tmp != null;
                }
                    break;
            }
#if ASYNC
            if (flushThread == null || !flushThread.IsAlive)
            {
                exit = false;
                flushThread = new Thread(new ThreadStart(DoFlush));
                flushThread.Name = System.DateTime.Now.ToString();
                flushThread.Start();
            }
#endif
        }

        private void Connect()
        {
            var headers = new Dictionary<string, string>();

            var body = string.Empty;

            //! The UserToken might get swapped for a better one during response
            //! processing. 
            if (!string.IsNullOrEmpty(settings.UserToken))
            {
                headers["Authorization"] = string.Format("Bearer {0}", settings.UserToken);
            }
            else if (!string.IsNullOrEmpty(settings.PlayerId))
            {
                headers.Add("Content-Type", "application/json");
                headers.Add("Accept", "application/json");

                body = "{\"anonymous\" : \"" + settings.PlayerId + "\"}";
            }

            var response = IssueRequest(string.Format("proxy/gleaner/collector/start/{0}", settings.TrackingCode),
                "POST", headers, body);

            if (response.ResultAllowed)
            {
                Log(Severity.Information, "");

                // Extract AuthToken.
                //
                if (jsonAuthToken.IsMatch(response.body))
                {
                    settings.UserToken = jsonAuthToken.Match(response.body).Groups[1].Value;
                    Log(Severity.Information, "AuthToken= {0}", settings.UserToken);

                    Connected = true;
                }

                // Extract PlayerId.
                //
                if (jsonPlayerId.IsMatch(response.body))
                {
                    settings.PlayerId = jsonPlayerId.Match(response.body).Groups[1].Value;
                    Log(Severity.Information, "PlayerId= {0}", settings.PlayerId);
                }

                // Extract Session number.
                //
                if (jsonSession.IsMatch(response.body))
                {
                    Log(Severity.Information, "Session= {0}", jsonSession.Match(response.body).Groups[1].Value);
                }

                // Extract ObjectID.
                //
                if (jsonObjectId.IsMatch(response.body))
                {
                    ObjectId = jsonObjectId.Match(response.body).Groups[1].Value;

                    if (!ObjectId.EndsWith("/"))
                    {
                        ObjectId += "/";
                    }

                    Log(Severity.Information, "ObjectId= {0}", ObjectId);
                }

                // Extract Actor Json Object.
                //
                if (jsonActor.IsMatch(response.body))
                {
                    ActorObject = JSONNode.Parse(jsonActor.Match(response.body).Groups[1].Value);

                    Log(Severity.Information, "Actor= {0}", ActorObject);

                    Active = true;
                }
            }
            else
            {
                Log(Severity.Error, "Request Error: {0}-{1}", response.responseCode, response.responsMessage);

                Active = false;
                Connected = false;
            }
        }

        /// <summary>
        ///     Starts with a trackingCode (and with the already extracted UserToken).
        /// </summary>
        /// <param name="trackingCode"> The tracking code. </param>
        public void Stop()
        {
            Active = false;
            Connected = false;
            Started = false;
            ActorObject = null;
#if ASYNC
            if (flushThread != null || flushThread.IsAlive)
            {
                exit = true;
                flushThread.Abort();
            }
#endif
            queue = new ConcurrentQueue<TrackerEvent>();
            tracesPending = new List<string>();
        }

        /// <summary>
        ///     Exit the tracker before closing to guarantee the thread closing.
        /// </summary>
        public void Exit()
        {
            exit = true;
            Flush();
#if ASYNC
            flushThread.Join();
            flushThread = null;
#endif
        }

        /// <summary>
        ///     Clears the unflushed Trace queue and the unappended extensions queue
        /// </summary>
        public void Clear()
        {
            queue.Clear();
            extensions.Clear();
        }

        /// <summary>
        ///     Adds a full trace to the queue, ignoring current extensions.
        /// </summary>
        /// <param name="trace">A comma separated string with the values of the trace</param>
        [Obsolete("Use ActionTrace instead. Never intended to be public. Has to receive a csv with specific format.")]
        public void Trace(string trace)
        {
            if (trace == null || trace == "")
            {
                throw new TraceException("Trace is be empty or null");
            }

            var parts = TrackerAssetUtils.parseCSV(trace);

            if (parts.Length != 3)
            {
                throw new TraceException("Trace length must be 3 (verb,target_type,target_id)");
            }

            ActionTrace(parts[0], parts[1], parts[2]);
        }

        /// <summary>
        ///     Adds a trace with the specified values
        /// </summary>
        /// <param name="values">Values of the trace.</param>
        [Obsolete("Use ActionTrace instead. Never intended to be public. Has to receive values in specific order.")]
        public void Trace(params string[] values)
        {
            /*if (strictMode) {
				Debug.LogWarning ("Tracker: Trace() method is Obsolete. Ignoring");
				return;
			} else {*/
            if (values.Length != 3)
            {
                throw new TraceException(
                    "Tracker: Trace must have at least 3 arguments: a verb, a target type and a target ID");
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!Utils.check<TraceException>(values[i],
                    "Tracker: Trace param " + i + " is null or empty, ignoring trace.",
                    "Tracker: Trace param " + i + " is null or empty"))
                {
                    return;
                }
            }
            //}

            ActionTrace(values[0], values[1], values[2]);
        }

        /// <summary>
        ///     Adds the given value to the Queue.
        /// </summary>
        /// <param name="value"> New value for the variable. </param>
        public void Trace(TrackerEvent trace)
        {
            if (!Started)
            {
                throw new TrackerException("Tracker Has not been started");
            }

            if (extensions.Count > 0)
            {
                trace.Result.Extensions = new Dictionary<string, object>(extensions);
                extensions.Clear();
            }

            queue.Enqueue(trace);
        }

        /// <summary>
        ///     Adds a trace with verb, target and targeit
        /// </summary>
        /// <param name="values">Values of the trace.</param>
        public void ActionTrace(string verb, string target_type, string target_id)
        {
            var trace = true;

            trace &= Utils.check<TraceException>(verb, "Tracker: Trace verb can't be null, ignoring. ",
                "Tracker: Trace verb can't be null.");
            trace &= Utils.check<TraceException>(target_type, "Tracker: Trace Target type can't be null, ignoring. ",
                "Tracker: Trace Target type can't be null.");
            trace &= Utils.check<TraceException>(target_id, "Tracker: Trace Target ID can't be null, ignoring. ",
                "Tracker: Trace Target ID can't be null.");

            if (trace)
            {
                Trace(new TrackerEvent(this)
                {
                    Event = new TrackerEvent.TraceVerb(verb),
                    Target = new TrackerEvent.TraceObject(target_type, target_id)
                });
            }
        }

        /// <summary>
        ///     Issue a HTTP Webrequest.
        /// </summary>
        /// <param name="path">   Full pathname of the file. </param>
        /// <param name="method"> The method. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        private RequestResponse IssueRequest(string path, string method)
        {
            return IssueRequest(path, method, new Dictionary<string, string>(), string.Empty);
        }

        /// <summary>
        ///     Issue a HTTP Webrequest.
        /// </summary>
        /// <param name="path">    Full pathname of the file. </param>
        /// <param name="method">  The method. </param>
        /// <param name="headers"> The headers. </param>
        /// <param name="body">    The body. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        private RequestResponse IssueRequest(string path, string method, Dictionary<string, string> headers,
            string body = "")
        {
            return IssueRequest(path, method, headers, body, settings.Port);
        }

        /// <summary>
        ///     Query if this object issue request 2.
        /// </summary>
        /// <param name="path">    Full pathname of the file. </param>
        /// <param name="method">  The method. </param>
        /// <param name="headers"> The headers. </param>
        /// <param name="body">    The body. </param>
        /// <param name="port">    The port. </param>
        /// <returns>
        ///     true if it succeeds, false if it fails.
        /// </returns>
        private RequestResponse IssueRequest(string path, string method, Dictionary<string, string> headers,
            string body, int port)
        {
            var ds = getInterface<IWebServiceRequest>();

            var response = new RequestResponse();

            if (ds != null)
            {
                ds.WebServiceRequest(
                    new RequestSetttings
                    {
                        method = method,
                        uri = new Uri(string.Format("http{0}://{1}{2}{3}/{4}",
                            settings.Secure ? "s" : string.Empty,
                            settings.Host,
                            port == 80 ? string.Empty : string.Format(":{0}", port),
                            string.IsNullOrEmpty(settings.BasePath.TrimEnd('/')) ? "" : settings.BasePath.TrimEnd('/'),
                            path.TrimStart('/')
                        )),
                        requestHeaders = headers,
                        //! allowedResponsCodes,     // TODO default is ok
                        body = body // or method.Equals("GET")?string.Empty:body
                    }, out response);
            }

            return response;
        }

        /// <summary>
        ///     Process the queue.
        /// </summary>
        private void ProcessQueue()
        {
            if (!Started)
            {
                Log(Severity.Warning,
                    "Refusing to send traces without starting tracker (Active is False, should be True)");
                return;
            }

            if (!Active)
            {
                Connect();
            }

            if (queue.Count > 0 || tracesPending.Count > 0 || tracesUnlogged.Count > 0)
            {
                //Extract the traces from the queue and remove from the queue
                var traces = CollectTraces();

                //Check if it's connected now
                if (Active)
                {
                    if (SendUnloggedTraces())
                    {
                        var data = ProcessTraces(traces, settings.TraceFormat);

                        if ((!SendPendingTraces() || !(queue.Count > 0 && SendTraces(data))) && queue.Count > 0)
                        {
                            tracesPending.Add(data);
                        }
                    }
                }
                else
                {
                    tracesUnlogged.AddRange(traces);
                }

                // if backup requested, save a copy
                if (settings.BackupStorage)
                {
                    var storage = getInterface<IDataStorage>();
                    var append_storage = getInterface<IAppend>();

                    if (queue.Count > 0)
                    {
                        var rawData = ProcessTraces(traces, TraceFormats.csv);

                        if (append_storage != null)
                        {
                            append_storage.Append(settings.BackupFile, rawData);
                        }
                        else if (storage != null)
                        {
                            var previous = storage.Exists(settings.BackupFile)
                                ? storage.Load(settings.BackupFile)
                                : string.Empty;

                            if (storage.Exists(settings.BackupFile))
                            {
                                storage.Save(settings.BackupFile, previous + rawData);
                            }
                            else
                            {
                                storage.Save(settings.BackupFile, rawData);
                            }
                        }
                    }
                }

                queue.Dequeue(traces.Length);
            }
            else
            {
                Log(Severity.Information, "Nothing to flush");
            }
        }

        private TrackerEvent[] CollectTraces()
        {
            var cnt = settings.BatchSize == 0 ? uint.MaxValue : settings.BatchSize;
            cnt = Math.Min((uint) queue.Count, cnt);
            var traces = queue.Peek(cnt);

            return traces;
        }

        private string ProcessTraces(TrackerEvent[] traces, TraceFormats format)
        {
            var data = string.Empty;
            TrackerEvent item;
            var sb = new List<string>();

            for (var i = 0; i < traces.Length; i++)
            {
                item = traces[i];

                switch (format)
                {
                    case TraceFormats.json:
                        sb.Add(item.ToJson());
                        break;
                    case TraceFormats.xml:
                        sb.Add(item.ToXml());
                        break;
                    case TraceFormats.xapi:
                        sb.Add(item.ToXapi());
                        break;
                    default:
                        sb.Add(item.ToCsv());
                        break;
                }
            }

            switch (format)
            {
                case TraceFormats.csv:
                    data = string.Join("\r\n", sb.ToArray()) + "\r\n";
                    break;
                case TraceFormats.json:
                    data = "[\r\n" + string.Join(",\r\n", sb.ToArray()) + "\r\n]";
                    break;
                case TraceFormats.xml:
                    data = "<TrackEvents>\r\n" + string.Join("\r\n", sb.ToArray()) + "\r\n</TrackEvent>";
                    break;
                case TraceFormats.xapi:
                    data = "[\r\n" + string.Join(",\r\n", sb.ToArray()) + "\r\n]";
                    break;
                default:
                    data = string.Join("\r\n", sb.ToArray());
                    break;
            }

            sb.Clear();

            return data;
        }

        private bool SendPendingTraces()
        {
            // Try to send old traces
            while (tracesPending.Count > 0)
            {
                Log(Severity.Information, "Enqueued trace-blocks detected: {0}. Processing...", tracesPending.Count);
                var data = tracesPending[0];
                if (!SendTraces(data))
                {
                    Log(Severity.Information, "Error sending enqueued traces");
                    // does not keep sending old traces, but continues processing new traces so that get added to tracesPending
                    break;
                }

                tracesPending.RemoveAt(0);
                Log(Severity.Information, "Sent enqueued traces OK");
            }

            return tracesPending.Count == 0;
        }

        private bool SendUnloggedTraces()
        {
            if (tracesUnlogged.Count > 0 && ActorObject != null)
            {
                var data = ProcessTraces(tracesUnlogged.ToArray(), settings.TraceFormat);
                var sent = SendTraces(data);
                tracesUnlogged.Clear();

                if (!sent)
                {
                    tracesPending.Add(data);
                }
            }

            return tracesUnlogged.Count == 0;
        }

        private bool SendTraces(string data)
        {
            switch (settings.StorageType)
            {
                case StorageTypes.local:
                    var storage = getInterface<IDataStorage>();
                    var append_storage = getInterface<IAppend>();

                    if (storage != null)
                    {
                        var previous = storage.Exists(settings.LogFile) ? storage.Load(settings.LogFile) : string.Empty;

                        if (previous.Length > 0)
                        {
                            previous = previous.Replace("\r\n]", ",\r\n");
                            data = data.Replace("[\r\n", "");
                        }

                        storage.Save(settings.LogFile, previous + data);
                    }

                    break;
                case StorageTypes.net:
                    var headers = new Dictionary<string, string>();

                    headers.Add("Content-Type", "application/json");
                    headers.Add("Authorization", string.Format("{0}", settings.UserToken));

                    Log(Severity.Information, "\r\n" + data);

                    var response = IssueRequest("proxy/gleaner/collector/track", "POST", headers, data);

                    if (response.ResultAllowed)
                    {
                        Log(Severity.Information, "Track= {0}", response.body);
                        Connected = true;
                    }
                    else
                    {
                        Log(Severity.Error, "Request Error: {0}-{1}", response.responseCode, response.responsMessage);

                        Log(Severity.Warning, "Error flushing, connection disabled temporaly");

                        Connected = false;
                        return false;
                    }

                    break;
            }

            return true;
        }

        #region Extension Methods

        /// <summary>
        ///     Sets if the following trace has been a success, including this value to the extensions.
        /// </summary>
        /// <param name="success">If set to <c>true</c> means it has been a success.</param>
        public void setSuccess(bool success)
        {
            setVar(Extension.Success.ToString().ToLower(), success);
        }

        /// <summary>
        ///     Sets the score of the following trace, including it to the extensions.
        /// </summary>
        /// <param name="score">Score, (Recomended between 0 and 1)</param>
        public void setScore(float score)
        {
            if (score < 0 || score > 1)
            {
                Log(Severity.Warning, "Tracker: Score recommended between 0 and 1 (Current: " + score + ")");
            }

            setVar(Extension.Score.ToString().ToLower(), score);
        }

        /// <summary>
        ///     Sets the response. If the player chooses between alternatives, the response should be the selected alternative.
        /// </summary>
        /// <param name="response">Response.</param>
        public void setResponse(string response)
        {
            addExtension(Extension.Response.ToString().ToLower(), response);
        }

        /// <summary>
        ///     Sets the completion of the following trace extensions. Completion specifies if something has been completed.
        /// </summary>
        /// <param name="completion">If set to <c>true</c> the trace action has been completed.</param>
        public void setCompletion(bool completion)
        {
            setVar(Extension.Completion.ToString().ToLower(), completion);
        }

        /// <summary>
        ///     Sets the progress of the action.
        /// </summary>
        /// <param name="progress">Progress. (Recomended between 0 and 1)</param>
        public void setProgress(float progress)
        {
            if (progress < 0 || progress > 1)
            {
                Log(Severity.Warning, "Tracker: Progress recommended between 0 and 1 (Current: " + progress + ")");
            }

            setVar(Extension.Progress.ToString().ToLower(), progress);
        }

        /// <summary>
        ///     Sets the coords where the trace takes place.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        /// <param name="z">The z coordinate.</param>
        public void setPosition(float x, float y, float z)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z))
            {
                if (StrictMode)
                {
                    throw new ValueExtensionException("Tracker: x, y or z cant be null.");
                }

                Log(Severity.Information, "Tracker: x, y or z cant be null, ignoring.");
                return;
            }

            addExtension(Extension.Position.ToString().ToLower(),
                "{\"x\":" + x + ", \"y\": " + y + ", \"z\": " + z + "}");
        }

        /// <summary>
        ///     Sets the health of the player's character when the trace occurs.
        /// </summary>
        /// <param name="health">Health.</param>
        public void setHealth(float health)
        {
            if (Utils.check<ValueExtensionException>(health, "Tracker: Health cant be null, ignoring.",
                "Tracker: Health cant be null."))
            {
                addExtension(Extension.Health.ToString().ToLower(), health);
            }
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="value">Value.</param>
        public void setVar(string id, Dictionary<string, bool> value)
        {
            addExtension(id, value);
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="value">Value.</param>
        public void setVar(string id, string value)
        {
            addExtension(id, value);
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        public void setVar(string key, int value)
        {
            addExtension(key, value);
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        public void setVar(string key, float value)
        {
            addExtension(key, value);
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        public void setVar(string key, double value)
        {
            addExtension(key, value);
        }

        /// <summary>
        ///     Adds a variable to the extensions.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        public void setVar(string key, bool value)
        {
            addExtension(key, value);
        }


        /// <summary>
        ///     Adds a extension to the extension list.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        [Obsolete("Use setVar instead. Never intended to be public.")]
        public void setExtension(string key, float value)
        {
            addExtension(key, value);
        }

        /// <summary>
        ///     Adds a extension to the extension list.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        [Obsolete("Use setVar instead. Never intended to be public.")]
        public void setExtension(string key, double value)
        {
            addExtension(key, value);
        }

        /// <summary>
        ///     Adds a extension to the extension list.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="value">Value.</param>
        [Obsolete("Use setVar instead. Never intended to be public.")]
        public void setExtension(string key, object value)
        {
            addExtension(key, value);
        }

        private void addExtension(string key, object value)
        {
            if (Utils.checkExtension(key, value))
            {
                if (extensions.ContainsKey(key))
                {
                    extensions[key] = value;
                }
                else
                {
                    extensions.Add(key, value);
                }
            }
        }

        #endregion Extension Methods

        #endregion Methods

        #region Nested Types

        /// <summary>
        ///     Interface that subtrackers must implement.
        /// </summary>
        public interface IGameObjectTracker
        {
            void setTracker(TrackerAsset tracker);
        }

        /// <summary>
        ///     Interface that trace formatters must implement.
        /// </summary>
        public interface ITraceFormatter
        {
            string Serialize(List<string> traces);

            void StartData(JSONClass data);
        }

        /// <summary>
        ///     A tracker event.
        /// </summary>
        public class TrackerEvent
        {
            #region Constructors

            public TrackerEvent(TrackerAsset tracker)
            {
                Tracker = tracker;
                TimeStamp = Math.Round(DateTime.Now.ToUniversalTime().Subtract(START_DATE).TotalMilliseconds);
                Result = new TraceResult();
            }

            #endregion Constructors

            #region Fields

            private static Dictionary<string, string> verbIds;

            private static Dictionary<string, string> objectIds;

            private static Dictionary<string, string> extensionIds;

            private TraceVerb verb;

            private TraceObject target;

            private TraceResult result;

            #endregion Fields

            #region Properties

            private static Dictionary<string, string> VerbIDs
            {
                get
                {
                    if (verbIds == null)
                    {
                        verbIds = new Dictionary<string, string>
                        {
                            {Verb.Initialized.ToString().ToLower(), "http://adlnet.gov/expapi/verbs/initialized"},
                            {Verb.Progressed.ToString().ToLower(), "http://adlnet.gov/expapi/verbs/progressed"},
                            {Verb.Completed.ToString().ToLower(), "http://adlnet.gov/expapi/verbs/completed"},
                            {Verb.Accessed.ToString().ToLower(), "https://w3id.org/xapi/seriousgames/verbs/accessed"},
                            {Verb.Skipped.ToString().ToLower(), "http://id.tincanapi.com/verb/skipped"},
                            {Verb.Selected.ToString().ToLower(), "https://w3id.org/xapi/adb/verbs/selected"},
                            {Verb.Unlocked.ToString().ToLower(), "https://w3id.org/xapi/seriousgames/verbs/unlocked"},
                            {Verb.Interacted.ToString().ToLower(), "http://adlnet.gov/expapi/verbs/interacted"},
                            {Verb.Used.ToString().ToLower(), "https://w3id.org/xapi/seriousgames/verbs/used"}
                        };
                    }

                    return verbIds;
                }
            }

            private static Dictionary<string, string> ObjectIDs
            {
                get
                {
                    if (objectIds == null)
                    {
                        objectIds = new Dictionary<string, string>
                        {
                            // Completable
                            {
                                CompletableTracker.Completable.Game.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/serious-game"
                            },
                            {
                                CompletableTracker.Completable.Session.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/session"
                            },
                            {
                                CompletableTracker.Completable.Level.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/level"
                            },
                            {
                                CompletableTracker.Completable.Quest.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/quest"
                            },
                            {
                                CompletableTracker.Completable.Stage.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/stage"
                            },
                            {
                                CompletableTracker.Completable.Combat.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/combat"
                            },
                            {
                                CompletableTracker.Completable.StoryNode.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/story-node"
                            },
                            {
                                CompletableTracker.Completable.Race.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/race"
                            },
                            {
                                CompletableTracker.Completable.Completable.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/completable"
                            },

                            // Acceesible
                            {
                                AccessibleTracker.Accessible.Screen.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/screen"
                            },
                            {
                                AccessibleTracker.Accessible.Area.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/area"
                            },
                            {
                                AccessibleTracker.Accessible.Zone.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/zone"
                            },
                            {
                                AccessibleTracker.Accessible.Cutscene.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/cutscene"
                            },
                            {
                                AccessibleTracker.Accessible.Accessible.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/accessible"
                            },

                            // Alternative
                            {
                                AlternativeTracker.Alternative.Question.ToString().ToLower(),
                                "http://adlnet.gov/expapi/activities/question"
                            },
                            {
                                AlternativeTracker.Alternative.Menu.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/menu"
                            },
                            {
                                AlternativeTracker.Alternative.Dialog.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/dialog-tree"
                            },
                            {
                                AlternativeTracker.Alternative.Path.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/path"
                            },
                            {
                                AlternativeTracker.Alternative.Arena.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/arena"
                            },
                            {
                                AlternativeTracker.Alternative.Alternative.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/alternative"
                            },

                            // GameObject
                            {
                                GameObjectTracker.TrackedGameObject.Enemy.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/enemy"
                            },
                            {
                                GameObjectTracker.TrackedGameObject.Npc.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/non-player-character"
                            },
                            {
                                GameObjectTracker.TrackedGameObject.Item.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/item"
                            },
                            {
                                GameObjectTracker.TrackedGameObject.GameObject.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/activity-types/game-object"
                            }
                        };
                    }

                    return objectIds;
                }
            }

            private static Dictionary<string, string> ExtensionIDs
            {
                get
                {
                    if (extensionIds == null)
                    {
                        extensionIds = new Dictionary<string, string>
                        {
                            {
                                Extension.Health.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/extensions/health"
                            },
                            {
                                Extension.Position.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/extensions/position"
                            },
                            {
                                Extension.Progress.ToString().ToLower(),
                                "https://w3id.org/xapi/seriousgames/extensions/progress"
                            }
                        };
                    }

                    return extensionIds;
                }
            }

            /// <summary>
            ///     Gets or sets the Tracker
            /// </summary>
            /// <value>
            ///     The Tracker.
            /// </value>
            public TrackerAsset Tracker { get; set; }

            /// <summary>
            ///     Gets or sets the event.
            /// </summary>
            /// <value>
            ///     The event.
            /// </value>
            [DefaultValue("")]
            public TraceVerb Event
            {
                get { return verb; }
                set
                {
                    verb = value;
                    verb.Parent = this;
                    verb.isValid();
                }
            }

            /// <summary>
            ///     Gets or sets the Target for the.
            /// </summary>
            /// <value>
            ///     The target.
            /// </value>
            [DefaultValue("")]
            public TraceObject Target
            {
                get { return target; }
                set
                {
                    target = value;
                    target.Parent = this;
                }
            }

            /// <summary>
            ///     Gets or sets the Result for the.
            /// </summary>
            /// <value>
            ///     The Result.
            /// </value>
            [DefaultValue("")]
            public TraceResult Result
            {
                get { return result; }
                set
                {
                    result = value;
                    result.Parent = this;
                }
            }

            /// <summary>
            ///     Gets the Date/Time of the time stamp.
            /// </summary>
            /// <value>
            ///     The time stamp.
            /// </value>
            public double TimeStamp { get; private set; }

            #endregion Properties

            #region Methods

            /// <summary>
            ///     Converts this object to a CSV Item.
            /// </summary>
            /// <returns>
            ///     This object as a string.
            /// </returns>
            public string ToCsv()
            {
                return TimeStamp
                       + "," + Event.ToCsv()
                       + "," + Target.ToCsv()
                       + (Result == null || string.IsNullOrEmpty(Result.ToCsv()) ? string.Empty : Result.ToCsv());
            }

            /// <summary>
            ///     Converts this object to a JSON Item.
            /// </summary>
            /// <returns>
            ///     This object as a string.
            /// </returns>
            public string ToJson()
            {
                var json = new JSONClass();

                json.Add("actor", Tracker.ActorObject == null ? JSONNode.Parse("{}") : Tracker.ActorObject);
                json.Add("event", Event.ToJson());
                json.Add("target", Target.ToJson());

                var result = Result.ToJson();
                if (result.Count > 0)
                {
                    json.Add("result", result);
                }

                json.Add("timestamp",
                    new JSONData(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(TimeStamp)
                        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ")));

                return json.ToString();
            }

            /// <summary>
            ///     Converts this object to an XML Item.
            /// </summary>
            /// <returns>
            ///     This object as a string.
            /// </returns>
            public string ToXml()
            {
#warning Use XMLSerializer else use proper XML Encoding.
                return "<TrackEvent \"timestamp\"=\"" + TimeStamp.ToString(TimeFormat) + "\"" +
                       " \"event\"=\"" + verbIds[Event.ToString().ToLower()] + "\"" +
                       " \"target\"=\"" + Target + "\"" +
                       (Result == null || string.IsNullOrEmpty(Result.ToXml())
                           ? " />"
                           : "><![CDATA[" + Result.ToXml() + "]]></TrackEvent>");
            }

            /// <summary>
            ///     Converts this object to an xapi.
            /// </summary>
            /// <returns>
            ///     This object as a string.
            /// </returns>
            public string ToXapi()
            {
                var json = new JSONClass();

                json.Add("actor", Tracker.ActorObject == null ? JSONNode.Parse("{}") : Tracker.ActorObject);
                json.Add("verb", Event.ToXapi());
                json.Add("object", Target.ToXapi());

                if (Result != null)
                {
                    var result = Result.ToXapi();
                    if (result.Count > 0)
                    {
                        json.Add("result", result);
                    }
                }

                json.Add("timestamp",
                    new JSONData(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(TimeStamp)
                        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ")));

                return json.ToJSON(1);
            }

            /// <summary>
            ///     Enquotes.
            /// </summary>
            /// <remarks>
            ///     Both checks could be combined.
            /// </remarks>
            /// <param name="value"> The value. </param>
            /// <returns>
            ///     A string.
            /// </returns>
            private string Enquote(string value)
            {
                if (value.Contains("\""))
                {
                    return string.Format("\"{0}\"", value.Replace("\"", "\"\""));
                }

                if (value.Contains("\r\n") || value.Contains(","))
                {
                    return string.Format("\"{0}\"", value);
                }

                return value;
            }

            private bool isValid()
            {
                var check = true;

                check &= Event.isValid();
                check &= Target.isValid();
                check &= Result.isValid();

                return true;
            }

            #endregion Methods

            #region Nested Types

            /// <summary>
            ///     Class for Target storage.
            /// </summary>
            public class TraceObject
            {
                private string _id;
                private string _type;

                public TraceObject(string type, string id)
                {
                    Type = type;
                    ID = id;
                }

                public TrackerEvent Parent { get; internal set; }

                public string Type
                {
                    get { return _type; }
                    set
                    {
                        if (Parent == null || Parent.Tracker.Utils.check<TargetXApiException>(value,
                                "xAPI Exception: Target Type is null or empty. Ignoring.",
                                "xAPI Exception: Target Type can't be null or empty."))
                        {
                            _type = value;
                        }
                    }
                }

                public string ID
                {
                    get { return _id; }
                    set
                    {
                        if (Parent == null || Parent.Tracker.Utils.check<TargetXApiException>(value,
                                "xAPI Exception: Target ID is null or empty. Ignoring.",
                                "xAPI Exception: Target ID can't be null or empty."))
                        {
                            _id = value;
                        }
                    }
                }

                public JSONClass Definition { get; set; }

                public string ToCsv()
                {
                    return Type.Replace(",", "\\,") + "," + ID.Replace(",", "\\,");
                }

                public JSONClass ToJson()
                {
                    var typeKey = Type;

                    if (!ObjectIDs.TryGetValue(Type, out typeKey))
                    {
                        typeKey = Type;
                        if (Parent.Tracker.StrictMode)
                        {
                            throw new TargetXApiException("Tracker-xAPI: Unknown definition for target type: " + Type);
                        }

                        Parent.Tracker.Log(Severity.Warning,
                            "Tracker-xAPI: Unknown definition for target type: " + Type);
                    }

                    JSONClass obj = new JSONClass(), definition = new JSONClass();

                    obj["id"] = (Parent.Tracker.ActorObject != null ? Parent.Tracker.ObjectId : "") + ID;
                    definition["type"] = typeKey;

                    obj.Add("definition", definition);

                    return obj;
                }

                public string ToXml()
                {
                    // TODO;
                    return Type + "," + ID;
                }

                public JSONClass ToXapi()
                {
                    var typeKey = Type;

                    if (!ObjectIDs.TryGetValue(Type, out typeKey))
                    {
                        typeKey = Type;
                        if (Parent.Tracker.StrictMode)
                        {
                            throw new TargetXApiException("Tracker-xAPI: Unknown definition for target type: " + Type);
                        }

                        Parent.Tracker.Log(Severity.Warning,
                            "Tracker-xAPI: Unknown definition for target type: " + Type);
                    }

                    JSONClass obj = new JSONClass(), definition = new JSONClass();

                    obj["id"] = (Parent.Tracker.ActorObject != null ? Parent.Tracker.ObjectId : "") + ID;
                    definition["type"] = typeKey;

                    obj.Add("definition", definition);

                    return obj;
                }

                public bool isValid()
                {
                    return TrackerAssetUtils.quickCheck(Type) && TrackerAssetUtils.quickCheck(ID);
                }
            }

            /// <summary>
            ///     Class for Verb storage.
            /// </summary>
            public class TraceVerb
            {
                private string sverb = "";
                private Verb vverb;

                public TraceVerb(Verb verb)
                {
                    Verb = verb;
                }

                public TraceVerb(string verb)
                {
                    sVerb = verb;
                }

                public TrackerEvent Parent { get; internal set; }

                public string sVerb
                {
                    get { return sverb; }
                    set
                    {
                        sverb = value;
                        Verb v;
                        if (TrackerAssetUtils.TryParseEnum(value, out v))
                        {
                            sverb = value.ToLower();
                            vverb = v;
                        }
                        else if (Parent != null)
                        {
                            if (Parent.Tracker.StrictMode)
                            {
                                throw new VerbXApiException("Tracker-xAPI: Unknown definition for verb: " + value);
                            }

                            Parent.Tracker.Log(Severity.Warning, "Tracker-xAPI: Unknown definition for verb: " + value);
                        }
                    }
                }

                public Verb Verb
                {
                    get { return vverb; }
                    set
                    {
                        sverb = value.ToString().ToLower();
                        vverb = value;
                    }
                }

                public string ToCsv()
                {
                    return sVerb.Replace(",", "\\,");
                }

                public JSONClass ToJson()
                {
                    var id = sVerb;

                    var verb = new JSONClass();
                    if (VerbIDs.TryGetValue(id, out id))
                    {
                        verb["id"] = id;
                    }
                    else
                    {
                        verb["id"] = sverb;
                    }

                    return verb;
                }

                public string ToXml()
                {
                    // TODO;
                    return "";
                }

                public JSONClass ToXapi()
                {
                    var id = sVerb;

                    var verb = new JSONClass();
                    if (VerbIDs.TryGetValue(id, out id))
                    {
                        verb["id"] = id;
                    }
                    else
                    {
                        verb["id"] = sverb;
                    }

                    return verb;
                }

                public bool isValid()
                {
                    var check = true;
                    if (Parent != null)
                    {
                        sVerb = sVerb;
                    }

                    return check && TrackerAssetUtils.quickCheck(sverb);
                }
            }

            /// <summary>
            ///     Class for Result storage.
            /// </summary>
            public class TraceResult
            {
                private int completion = -1;

                private Dictionary<string, object> extdir;

                private string res;
                private float score = float.NaN;

                private int success = -1;
                public TrackerEvent Parent { get; internal set; }

                public bool Success
                {
                    get { return success == 1 ? true : false; }
                    set { success = value ? 1 : 0; }
                }

                public bool Completion
                {
                    get { return completion == 1 ? true : false; }
                    set { completion = value ? 1 : 0; }
                }

                public string Response
                {
                    get { return res; }
                    set
                    {
                        if (Parent == null || Parent.Tracker.Utils.check<ValueExtensionException>(value,
                                "xAPI extension: response Empty or null. Ignoring",
                                "xAPI extension: response can't be empty or null"))
                        {
                            res = value;
                        }
                    }
                }

                public float Score
                {
                    get { return score; }
                    set
                    {
                        if (Parent == null || Parent.Tracker.Utils.check<ValueExtensionException>(value,
                                "xAPI extension: score null or NaN. Ignoring",
                                "xAPI extension: score can't be null or NaN."))
                        {
                            score = value;
                        }
                    }
                }

                public Dictionary<string, object> Extensions
                {
                    get { return extdir; }
                    set
                    {
                        extdir = new Dictionary<string, object>();
                        foreach (var extension in value)
                        {
                            switch (extension.Key.ToLower())
                            {
                                case "success":
                                    Success = (bool) extension.Value;
                                    break;
                                case "completion":
                                    Completion = (bool) extension.Value;
                                    break;
                                case "response":
                                    Response = (string) extension.Value;
                                    break;
                                case "score":
                                    Score = (float) extension.Value;
                                    break;
                                default:
                                    extdir.Add(extension.Key, extension.Value);
                                    break;
                            }
                        }
                    }
                }

                public string ToCsv()
                {
                    var result =
                        (success > -1 ? ",success" + intToBoolString(success) : "")
                        + (completion > -1 ? ",completion" + intToBoolString(completion) : "")
                        + (!string.IsNullOrEmpty(Response) ? ",response," + Response.Replace(",", "\\,") : "")
                        + (!float.IsNaN(score) ? ",score," + score.ToString("G", CultureInfo.InvariantCulture) : "");

                    if (Extensions != null && Extensions.Count > 0)
                    {
                        foreach (var extension in Extensions)
                        {
                            result += "," + extension.Key.Replace(",", "\\,") + ",";
                            if (extension.Value != null)
                            {
                                if (extension.Value.GetType() == typeof(string))
                                {
                                    result += extension.Value.ToString().Replace(",", "\\,");
                                }
                                else if (extension.Value.GetType() == typeof(float))
                                {
                                    result += ((float) extension.Value).ToString("G", CultureInfo.InvariantCulture);
                                }
                                else if (extension.Value.GetType() == typeof(double))
                                {
                                    result += ((double) extension.Value).ToString("G", CultureInfo.InvariantCulture);
                                }
                                else if (extension.Value.GetType() == typeof(Dictionary<string, bool>))
                                {
                                    var map = (Dictionary<string, bool>) extension.Value;
                                    var smap = "";
                                    foreach (var t in map)
                                    {
                                        smap += t.Key + "=" + t.Value.ToString().ToLower() + "-";
                                    }

                                    result += smap.TrimEnd('-');
                                }
                                else
                                {
                                    result += extension.Value.ToString();
                                }
                            }
                        }
                    }


                    return result;
                }

                public JSONClass ToJson()
                {
                    var result = new JSONClass();

                    if (success != -1)
                    {
                        result.Add("success", new JSONData(Convert.ToBoolean(success)));
                    }

                    if (completion != -1)
                    {
                        result.Add("completion", new JSONData(Convert.ToBoolean(completion)));
                    }

                    if (!string.IsNullOrEmpty(Response))
                    {
                        result.Add("response", new JSONData(Response));
                    }

                    if (!float.IsNaN(score))
                    {
                        var s = new JSONClass();
                        s.Add("raw", new JSONData(score));
                        result.Add("score", s);
                    }

                    if (Extensions != null && Extensions.Count > 0)
                    {
                        var extensions = new JSONClass();
                        foreach (var extension in Extensions)
                        {
                            if (extension.Value != null)
                            {
                                if (extension.Value.GetType() == typeof(int))
                                {
                                    extensions.Add(extension.Key, new JSONData((int) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(bool))
                                {
                                    extensions.Add(extension.Key, new JSONData((bool) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(float))
                                {
                                    extensions.Add(extension.Key, new JSONData((float) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(double))
                                {
                                    extensions.Add(extension.Key, new JSONData((double) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(Dictionary<string, bool>))
                                {
                                    var map = (Dictionary<string, bool>) extension.Value;
                                    var emap = new JSONClass();
                                    foreach (var t in map)
                                    {
                                        emap.Add(t.Key, new JSONData(t.Value));
                                    }

                                    extensions.Add(extension.Key, emap);
                                }
                                else
                                {
                                    extensions.Add(extension.Key, new JSONData(extension.Value.ToString()));
                                }
                            }
                        }

                        result.Add("extensions", extensions);
                    }

                    return result;
                }

                public string ToXml()
                {
                    // TODO;
                    return "";
                }

                public JSONClass ToXapi()
                {
                    var result = new JSONClass();

                    if (success != -1)
                    {
                        result.Add("success", new JSONData(Convert.ToBoolean(success)));
                    }

                    if (completion != -1)
                    {
                        result.Add("completion", new JSONData(Convert.ToBoolean(completion)));
                    }

                    if (!string.IsNullOrEmpty(Response))
                    {
                        result.Add("response", new JSONData(Response));
                    }

                    if (!float.IsNaN(score))
                    {
                        var s = new JSONClass();
                        s.Add("raw", new JSONData(score));
                        result.Add("score", s);
                    }

                    if (Extensions != null && Extensions.Count > 0)
                    {
                        var extensions = new JSONClass();
                        foreach (var extension in Extensions)
                        {
                            if (extension.Value != null)
                            {
                                var key = extension.Key;

                                var tmpkey = "";
                                if (ExtensionIDs.TryGetValue(key, out tmpkey))
                                {
                                    key = tmpkey;
                                }

                                if (extension.Value.GetType() == typeof(int))
                                {
                                    extensions.Add(key, new JSONData((int) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(bool))
                                {
                                    extensions.Add(key, new JSONData((bool) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(float))
                                {
                                    extensions.Add(key, new JSONData((float) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(double))
                                {
                                    extensions.Add(key, new JSONData((double) extension.Value));
                                }
                                else if (extension.Value.GetType() == typeof(Dictionary<string, bool>))
                                {
                                    var map = (Dictionary<string, bool>) extension.Value;
                                    var emap = new JSONClass();
                                    foreach (var t in map)
                                    {
                                        emap.Add(t.Key, new JSONData(t.Value));
                                    }

                                    extensions.Add(key, emap);
                                }
                                else
                                {
                                    extensions.Add(key, new JSONData(extension.Value.ToString()));
                                }
                            }
                        }

                        result.Add("extensions", extensions);
                    }

                    return result;
                }

                private static string intToBoolString(int property)
                {
                    var ret = "";
                    if (property >= 1)
                    {
                        ret = ",true";
                    }
                    else if (property == 0)
                    {
                        ret = ",false";
                    }

                    return ret;
                }

                public bool isValid()
                {
                    var valid = true;

                    if (!string.IsNullOrEmpty(Response))
                    {
                        Response = Response;
                    }

                    if (!float.IsNaN(Score))
                    {
                        Score = Score;
                    }

                    var result = new JSONClass();

                    if (success != -1)
                    {
                        valid &= TrackerAssetUtils.quickCheck(success);
                    }

                    if (completion != -1)
                    {
                        valid &= TrackerAssetUtils.quickCheck(completion);
                    }

                    if (!string.IsNullOrEmpty(Response))
                    {
                        valid &= TrackerAssetUtils.quickCheck(Response);
                    }

                    if (!float.IsNaN(score))
                    {
                        valid &= TrackerAssetUtils.quickCheck(score);
                    }

                    if (Extensions != null && Extensions.Count > 0)
                    {
                        foreach (var extension in Extensions)
                        {
                            valid &= TrackerAssetUtils.quickCheckExtension(extension.Key, extension.Value);
                        }
                    }

                    return valid;
                }
            }

            #endregion Nested Types
        }

        #endregion Nested Types
    }
}