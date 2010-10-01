/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Statistics;
using OpenSim.Region.ClientStack;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Physics.Manager;
using OpenSim.Server.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Timers;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Statistics;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Physics.Manager;

using Timer = System.Timers.Timer;

namespace OpenSim
{
    /// <summary>
    /// Common OpenSimulator simulator code
    /// </summary>
    public class OpenSimBase
    {
        /// <summary>The file used to load and save prim backup xml if no filename has been specified</summary>
        protected const string DEFAULT_PRIM_BACKUP_FILENAME = "prim-backup.xml";

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// This will control a periodic log printout of the current 'show stats' (if they are active) for this
        /// server.
        /// </summary>
        private Timer m_periodicDiagnosticsTimer = new Timer(60 * 60 * 1000);

        protected BaseHttpServer m_httpServer;
        protected CommandConsole m_console;
        protected OpenSimAppender m_consoleAppender;
        protected IAppender m_logFileAppender = null;
        /// <summary>Time at which this server was started</summary>
        protected DateTime m_startuptime;
        /// <summary>Record the initial startup directory for info purposes</summary>
        protected string m_startupDirectory = Environment.CurrentDirectory;
        /// <summary>Server version information.  Usually VersionInfo + information about git commit, operating system, etc.</summary>
        protected string m_version;
        protected string m_pidFile = String.Empty;
        /// <summary>Random uuid for private data</summary>
        protected string m_osSecret = String.Empty;
        /// <summary>Holds the non-viewer statistics collection object for this service/server</summary>
        protected IStatsCollector m_stats;

        protected Dictionary<EndPoint, uint> m_clientCircuits = new Dictionary<EndPoint, uint>();
        protected uint m_httpServerPort;
        protected ISimulationDataService m_simulationDataService;
        protected IEstateDataService m_estateDataService;
        protected ClientStackManager m_clientStackManager;
        protected SceneManager m_sceneManager = new SceneManager();

        protected string userStatsURI = String.Empty;
        protected bool m_autoCreateClientStack = true;
        protected string proxyUrl;
        protected int proxyOffset;
        protected ConfigSettings m_configSettings;
        protected ConfigurationLoader m_configLoader;
        protected IApplicationPlugin[] m_appPlugins = new IApplicationPlugin[0];
        protected OpenSimConfigSource m_config;
        protected List<IClientNetworkServer> m_clientServers = new List<IClientNetworkServer>();
        protected ModuleLoader m_moduleLoader;
        protected IRegistryCore m_applicationRegistry = new RegistryCore();
        protected CompositionContainer m_moduleContainer;

        public SceneManager SceneManager { get { return m_sceneManager; } }
        public ISimulationDataService SimulationDataService { get { return m_simulationDataService; } }
        public IEstateDataService EstateDataService { get { return m_estateDataService; } }

        public ConfigSettings ConfigurationSettings
        {
            get { return m_configSettings; }
            set { m_configSettings = value; }
        }

        /// <summary>The config information passed into the OpenSimulator region server</summary>
        public OpenSimConfigSource ConfigSource
        {
            get { return m_config; }
            set { m_config = value; }
        }

        public List<IClientNetworkServer> ClientServers
        {
            get { return m_clientServers; }
        }

        public uint HttpServerPort
        {
            get { return m_httpServerPort; }
        }

        public ModuleLoader ModuleLoader
        {
            get { return m_moduleLoader; }
            set { m_moduleLoader = value; }
        }

        public CompositionContainer ModuleContainer
        {
            get { return m_moduleContainer; }
        }

        public IRegistryCore ApplicationRegistry
        {
            get { return m_applicationRegistry; }
        }

        /// <summary>Secret identifier for the simulator</summary>
        public string osSecret
        {
            get { return m_osSecret; }
        }

        public BaseHttpServer HttpServer
        {
            get { return m_httpServer; }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public OpenSimBase(IConfigSource configSource)
        {
            m_startuptime = DateTime.Now;
            m_version = VersionInfo.Version;

            // Random uuid for private data
            m_osSecret = UUID.Random().ToString();

            m_periodicDiagnosticsTimer.Elapsed += new ElapsedEventHandler(LogDiagnostics);
            m_periodicDiagnosticsTimer.Enabled = true;

            // This thread will go on to become the console listening thread
            Thread.CurrentThread.Name = "ConsoleThread";

            ILoggerRepository repository = LogManager.GetRepository();
            IAppender[] appenders = repository.GetAppenders();

            foreach (IAppender appender in appenders)
            {
                if (appender.Name == "LogFileAppender")
                {
                    m_logFileAppender = appender;
                }
            }

            LoadConfigSettings(configSource);

            m_clientStackManager = CreateClientStackManager();

            Initialize();

            m_httpServer = new BaseHttpServer(
                m_httpServerPort, m_configSettings.HttpUsesSSL, m_configSettings.httpSSLPort,
                m_configSettings.HttpSSLCN);

            if (m_configSettings.HttpUsesSSL && (m_configSettings.HttpListenerPort == m_configSettings.httpSSLPort))
            {
                m_log.Error("[REGION SERVER]: HTTP Server config failed.   HTTP Server and HTTPS server must be on different ports");
            }

            m_log.InfoFormat("[REGION SERVER]: Starting HTTP server on port {0}", m_httpServerPort);
            m_httpServer.Start();

            MainServer.Instance = m_httpServer;
        }

        /// <summary>
        /// Performs initialisation of the scene, such as loading configuration from disk.
        /// </summary>
        public virtual void Startup()
        {
            m_log.Info("[STARTUP]: Beginning startup processing");

            EnhanceVersionInformation();

            m_log.Info("[STARTUP]: OpenSimulator version: " + m_version + Environment.NewLine);
            // clr version potentially is more confusing than helpful, since it doesn't tell us if we're running under Mono/MS .NET and
            // the clr version number doesn't match the project version number under Mono.
            //m_log.Info("[STARTUP]: Virtual machine runtime version: " + Environment.Version + Environment.NewLine);
            m_log.Info("[STARTUP]: Operating system version: " + Environment.OSVersion + Environment.NewLine);

            StartupSpecific();

            TimeSpan timeTaken = DateTime.Now - m_startuptime;

            m_log.InfoFormat("[STARTUP]: Startup took {0}m {1}s", timeTaken.Minutes, timeTaken.Seconds);
        }

        public virtual void Shutdown()
        {
            ShutdownSpecific();

            m_log.Info("[SHUTDOWN]: Shutdown processing on main thread complete.  Exiting...");
            RemovePIDFile();

            Environment.Exit(0);
        }

        public string StatReport(OSHttpRequest httpRequest)
        {
            // If we catch a request for "callback", wrap the response in the value for jsonp
            if (httpRequest.Query.ContainsKey("callback"))
            {
                return httpRequest.Query["callback"].ToString() + "(" + m_stats.XReport((DateTime.Now - m_startuptime).ToString(), m_version) + ");";
            }
            else
            {
                return m_stats.XReport((DateTime.Now - m_startuptime).ToString(), m_version);
            }
        }

        protected virtual void LoadConfigSettings(IConfigSource configSource)
        {
            m_configLoader = new ConfigurationLoader();
            m_config = m_configLoader.LoadConfigSettings(configSource, out m_configSettings);
            ReadExtraConfigSettings();
        }

        protected virtual void ReadExtraConfigSettings()
        {
            IConfig networkConfig = m_config.Source.Configs["Network"];
            if (networkConfig != null)
            {
                proxyUrl = networkConfig.GetString("proxy_url", "");
                proxyOffset = Int32.Parse(networkConfig.GetString("proxy_offset", "0"));
            }
        }

        protected virtual void LoadApplicationPlugins()
        {
            #region Container Loading

            AggregateCatalog catalog = new AggregateCatalog();

            AssemblyCatalog assemblyCatalog = new AssemblyCatalog(System.Reflection.Assembly.GetExecutingAssembly());
            DirectoryCatalog directoryCatalog = new DirectoryCatalog(".", "OpenSim.*.dll");

            catalog.Catalogs.Add(assemblyCatalog);
            catalog.Catalogs.Add(directoryCatalog);

            m_moduleContainer = new CompositionContainer(catalog, true);

            try
            {
                m_log.InfoFormat("[MODULES]: Found {0} modules in the current assembly and {1} modules in external assemblies",
                    assemblyCatalog.Parts.Count(), directoryCatalog.Parts.Count());
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                StringBuilder error = new StringBuilder("[MODULES]: Error(s) encountered loading plugin modules. You may have an incompatible or out of date plugin .dll in the current folder.");
                foreach (Exception loaderEx in ex.LoaderExceptions)
                    error.Append("\n " + loaderEx.Message);
                m_log.Error(error.ToString());

                Environment.Exit(-1);
            }

            #endregion Container Loading

            #region Plugin Loading

            IEnumerable<Lazy<object, object>> exportEnumerable = m_moduleContainer.GetExports(typeof(IApplicationPlugin), null, null);
            Dictionary<string, Lazy<object, object>> exports = new Dictionary<string, Lazy<object, object>>();
            List<IApplicationPlugin> imports = new List<IApplicationPlugin>();
            List<string> notLoaded = new List<string>();

            // Reshuffle exportEnumerable into a dictionary mapping module names to their lazy instantiations
            foreach (Lazy<object, object> lazyExport in exportEnumerable)
            {
                IDictionary<string, object> metadata = (IDictionary<string, object>)lazyExport.Metadata;
                object nameObj;
                if (metadata.TryGetValue("Name", out nameObj))
                {
                    string name = (string)nameObj;

                    if (!exports.ContainsKey(name))
                        exports.Add(name, lazyExport);
                    else
                        m_log.Warn("[MODULES]: Found an IApplicationPlugin with a duplicate name: " + name);
                }
            }

            // TODO: Load modules in the order they appear in the whitelist
            foreach (Lazy<object, object> lazyExport in exports.Values)
            {
                imports.Add((IApplicationPlugin)lazyExport.Value);
            }
            exports.Clear();

            // Populate m_appPlugins
            m_appPlugins = imports.ToArray();

            m_log.Debug("[MODULES]: Loaded " + m_appPlugins.Length + " application plugins");

            #endregion Plugin Loading
        }

        /// <summary>
        /// Provides a list of help topics that are available.  Overriding classes should append their topics to the
        /// information returned when the base method is called.
        /// </summary>
        /// 
        /// <returns>
        /// A list of strings that represent different help topics on which more information is available
        /// </returns>
        protected List<string> GetHelpTopics()
        {
            List<string> topics = new List<string>();
            Scene s = SceneManager.CurrentOrFirstScene;
            if (s != null && s.GetCommanders() != null)
                topics.AddRange(s.GetCommanders().Keys);

            return topics;
        }

        /// <summary>
        /// Get a new physics scene.
        /// </summary>
        /// <param name="engine">The name of the physics engine to use</param>
        /// <param name="meshEngine">The name of the mesh engine to use</param>
        /// <param name="config">The configuration data to pass to the physics and mesh engines</param>
        /// <param name="osSceneIdentifier">
        /// The name of the OpenSim scene this physics scene is serving.  This will be used in log messages.
        /// </param>
        /// <returns></returns>
        protected PhysicsScene GetPhysicsScene(
            string engine, string meshEngine, IConfigSource config, string osSceneIdentifier)
        {
            PhysicsPluginManager physicsPluginManager;
            physicsPluginManager = new PhysicsPluginManager();
            physicsPluginManager.LoadPluginsFromAssemblies(Util.dataDir());

            return physicsPluginManager.GetPhysicsScene(engine, meshEngine, config, osSceneIdentifier);
        }

        /// <summary>
        /// Print statistics to the logfile, if they are active
        /// </summary>
        protected void LogDiagnostics(object source, ElapsedEventArgs e)
        {
            StringBuilder sb = new StringBuilder("DIAGNOSTICS\n\n");
            sb.Append(GetUptimeReport());

            if (m_stats != null)
            {
                sb.Append(m_stats.Report());
            }

            sb.Append(Environment.NewLine);
            sb.Append(GetThreadsReport());

            m_log.Debug(sb);
        }

        /// <summary>
        /// Get a report about the registered threads in this server.
        /// </summary>
        protected string GetThreadsReport()
        {
            StringBuilder sb = new StringBuilder();
            Watchdog.ThreadWatchdogInfo[] threads = Watchdog.GetThreads();

            sb.Append(threads.Length + " threads are being tracked:" + Environment.NewLine);
            foreach (Watchdog.ThreadWatchdogInfo twi in threads)
            {
                Thread t = twi.Thread;

                sb.Append(
                    "ID: " + t.ManagedThreadId + ", Name: " + t.Name + ", TimeRunning: "
                    + "Pri: " + t.Priority + ", State: " + t.ThreadState);
                sb.Append(Environment.NewLine);
            }

            int workers = 0, ports = 0, maxWorkers = 0, maxPorts = 0;
            ThreadPool.GetAvailableThreads(out workers, out ports);
            ThreadPool.GetMaxThreads(out maxWorkers, out maxPorts);

            sb.Append(Environment.NewLine + "*** ThreadPool threads ***" + Environment.NewLine);
            sb.Append("workers: " + (maxWorkers - workers) + " (" + maxWorkers + "); ports: " + (maxPorts - ports) + " (" + maxPorts + ")" + Environment.NewLine);

            return sb.ToString();
        }

        /// <summary>
        /// Return a report about the uptime of this server
        /// </summary>
        /// <returns></returns>
        protected string GetUptimeReport()
        {
            StringBuilder sb = new StringBuilder(String.Format("Time now is {0}\n", DateTime.Now));
            sb.Append(String.Format("Server has been running since {0}, {1}\n", m_startuptime.DayOfWeek, m_startuptime));
            sb.Append(String.Format("That is an elapsed time of {0}\n", DateTime.Now - m_startuptime));

            return sb.ToString();
        }

        protected virtual void HandleShow(string module, string[] cmd)
        {
            List<string> args = new List<string>(cmd);

            args.RemoveAt(0);

            string[] showParams = args.ToArray();

            switch (showParams[0])
            {
                case "info":
                    Notice("Version: " + m_version);
                    Notice("Startup directory: " + m_startupDirectory);
                    break;

                case "stats":
                    if (m_stats != null)
                        Notice(m_stats.Report());
                    break;

                case "threads":
                    Notice(GetThreadsReport());
                    break;

                case "uptime":
                    Notice(GetUptimeReport());
                    break;

                case "version":
                    Notice(
                        String.Format(
                            "Version: {0} (interface version {1})", m_version, VersionInfo.MajorInterfaceVersion));
                    break;
            }
        }

        private void HandleQuit(string module, string[] args)
        {
            Shutdown();
        }

        private void HandleLogLevel(string module, string[] cmd)
        {
            if (null == m_consoleAppender)
            {
                Notice("No appender named Console found (see the log4net config file for this executable)!");
                return;
            }

            string rawLevel = cmd[3];

            ILoggerRepository repository = LogManager.GetRepository();
            Level consoleLevel = repository.LevelMap[rawLevel];

            if (consoleLevel != null)
                m_consoleAppender.Threshold = consoleLevel;
            else
                Notice(
                    String.Format(
                        "{0} is not a valid logging level.  Valid logging levels are ALL, DEBUG, INFO, WARN, ERROR, FATAL, OFF",
                        rawLevel));

            Notice(String.Format("Console log level is {0}", m_consoleAppender.Threshold));
        }

        /// <summary>
        /// Console output is only possible if a console has been established.
        /// That is something that cannot be determined within this class. So
        /// all attempts to use the console MUST be verified.
        /// </summary>
        protected void Notice(string msg)
        {
            if (m_console != null)
            {
                m_console.Output(msg);
            }
        }

        /// <summary>
        /// Enhance the version string with extra information if it's available.
        /// </summary>
        protected void EnhanceVersionInformation()
        {
            string buildVersion = string.Empty;

            // Add commit hash and date information if available
            // The commit hash and date are stored in a file bin/.version
            // This file can automatically created by a post
            // commit script in the opensim git master repository or
            // by issuing the follwoing command from the top level
            // directory of the opensim repository
            // git log -n 1 --pretty="format:%h: %ci" >bin/.version
            // For the full git commit hash use %H instead of %h
            //
            // The subversion information is deprecated and will be removed at a later date
            // Add subversion revision information if available
            // Try file "svn_revision" in the current directory first, then the .svn info.
            // This allows to make the revision available in simulators not running from the source tree.
            // FIXME: Making an assumption about the directory we're currently in - we do this all over the place
            // elsewhere as well
            string svnRevisionFileName = "svn_revision";
            string svnFileName = ".svn/entries";
            string gitCommitFileName = ".version";
            string inputLine;
            int strcmp;

            if (File.Exists(gitCommitFileName))
            {
                StreamReader CommitFile = File.OpenText(gitCommitFileName);
                buildVersion = CommitFile.ReadLine();
                CommitFile.Close();
                m_version += buildVersion ?? "";
            }

            // Remove the else logic when subversion mirror is no longer used
            else
            {
                if (File.Exists(svnRevisionFileName))
                {
                    StreamReader RevisionFile = File.OpenText(svnRevisionFileName);
                    buildVersion = RevisionFile.ReadLine();
                    buildVersion.Trim();
                    RevisionFile.Close();

                }

                if (string.IsNullOrEmpty(buildVersion) && File.Exists(svnFileName))
                {
                    StreamReader EntriesFile = File.OpenText(svnFileName);
                    inputLine = EntriesFile.ReadLine();
                    while (inputLine != null)
                    {
                        // using the dir svn revision at the top of entries file
                        strcmp = String.Compare(inputLine, "dir");
                        if (strcmp == 0)
                        {
                            buildVersion = EntriesFile.ReadLine();
                            break;
                        }
                        else
                        {
                            inputLine = EntriesFile.ReadLine();
                        }
                    }
                    EntriesFile.Close();
                }

                m_version += string.IsNullOrEmpty(buildVersion) ? "      " : ("." + buildVersion + "     ").Substring(0, 6);
            }
        }

        protected void CreatePIDFile(string path)
        {
            try
            {
                string pidstring = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
                FileStream fs = File.Create(path);
                System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
                Byte[] buf = enc.GetBytes(pidstring);
                fs.Write(buf, 0, buf.Length);
                fs.Close();
                m_pidFile = path;
            }
            catch (Exception)
            {
            }
        }

        protected void RemovePIDFile()
        {
            if (m_pidFile != String.Empty)
            {
                try
                {
                    File.Delete(m_pidFile);
                    m_pidFile = String.Empty;
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// Performs startup specific to the region server, including initialization of the scene 
        /// such as loading configuration from disk.
        /// </summary>
        protected virtual void StartupSpecific()
        {
            #region Console Setup

            if (m_console != null)
            {
                ILoggerRepository repository = LogManager.GetRepository();
                IAppender[] appenders = repository.GetAppenders();

                foreach (IAppender appender in appenders)
                {
                    if (appender.Name == "Console")
                    {
                        m_consoleAppender = (OpenSimAppender)appender;
                        break;
                    }
                }

                if (null == m_consoleAppender)
                {
                    Notice("No appender named Console found (see the log4net config file for this executable)!");
                }
                else
                {
                    m_consoleAppender.Console = m_console;

                    // If there is no threshold set then the threshold is effectively everything.
                    if (null == m_consoleAppender.Threshold)
                        m_consoleAppender.Threshold = Level.All;

                    Notice(String.Format("Console log level is {0}", m_consoleAppender.Threshold));
                }

                m_console.Commands.AddCommand("base", false, "quit",
                        "quit",
                        "Quit the application", HandleQuit);

                m_console.Commands.AddCommand("base", false, "shutdown",
                        "shutdown",
                        "Quit the application", HandleQuit);

                m_console.Commands.AddCommand("base", false, "set log level",
                        "set log level <level>",
                        "Set the console logging level", HandleLogLevel);

                m_console.Commands.AddCommand("base", false, "show info",
                        "show info",
                        "Show general information", HandleShow);

                m_console.Commands.AddCommand("base", false, "show stats",
                        "show stats",
                        "Show statistics", HandleShow);

                m_console.Commands.AddCommand("base", false, "show threads",
                        "show threads",
                        "Show thread status", HandleShow);

                m_console.Commands.AddCommand("base", false, "show uptime",
                        "show uptime",
                        "Show server uptime", HandleShow);

                m_console.Commands.AddCommand("base", false, "show version",
                        "show version",
                        "Show server version", HandleShow);
            }

            #endregion Console Setup

            IConfig startupConfig = m_config.Source.Configs["Startup"];
            if (startupConfig != null)
            {
                string pidFile = startupConfig.GetString("PIDFile", String.Empty);
                if (pidFile != String.Empty)
                    CreatePIDFile(pidFile);
                
                userStatsURI = startupConfig.GetString("Stats_URI", String.Empty);
            }

            // Load the simulation data service
            IConfig simDataConfig = m_config.Source.Configs["SimulationDataStore"];
            if (simDataConfig == null)
                throw new Exception("Configuration file is missing the [SimulationDataStore] section");
            string module = simDataConfig.GetString("LocalServiceModule", String.Empty);
            if (String.IsNullOrEmpty(module))
                throw new Exception("Configuration file is missing the LocalServiceModule parameter in the [SimulationDataStore] section");
            m_simulationDataService = ServerUtils.LoadPlugin<ISimulationDataService>(module, new object[] { m_config.Source });

            // Load the estate data service
            IConfig estateDataConfig = m_config.Source.Configs["EstateDataStore"];
            if (estateDataConfig == null)
                throw new Exception("Configuration file is missing the [EstateDataStore] section");
            module = estateDataConfig.GetString("LocalServiceModule", String.Empty);
            if (String.IsNullOrEmpty(module))
                throw new Exception("Configuration file is missing the LocalServiceModule parameter in the [EstateDataStore] section");
            m_estateDataService = ServerUtils.LoadPlugin<IEstateDataService>(module, new object[] { m_config.Source });

            m_stats = StatsManager.StartCollectingSimExtraStats();

            // Create a ModuleLoader instance
            m_moduleLoader = new ModuleLoader(m_config.Source);

            LoadApplicationPlugins();
            foreach (IApplicationPlugin plugin in m_appPlugins)
                plugin.Initialise(this);
            foreach (IApplicationPlugin plugin in m_appPlugins)
                plugin.PostInitialise();

            AddPluginCommands();
        }

        protected virtual void AddPluginCommands()
        {
            // If console exists add plugin commands.
            if (m_console != null)
            {
                List<string> topics = GetHelpTopics();

                foreach (string topic in topics)
                {
                    m_console.Commands.AddCommand("plugin", false, "help " + topic,
                                                  "help " + topic,
                                                  "Get help on plugin command '" + topic + "'",
                                                  HandleCommanderHelp);

                    m_console.Commands.AddCommand("plugin", false, topic,
                                                  topic,
                                                  "Execute subcommand for plugin '" + topic + "'",
                                                  null);

                    ICommander commander = null;

                    Scene s = SceneManager.CurrentOrFirstScene;

                    if (s != null && s.GetCommanders() != null)
                    {
                        if (s.GetCommanders().ContainsKey(topic))
                            commander = s.GetCommanders()[topic];
                    }

                    if (commander == null)
                        continue;

                    foreach (string command in commander.Commands.Keys)
                    {
                        m_console.Commands.AddCommand(topic, false,
                                                      topic + " " + command,
                                                      topic + " " + commander.Commands[command].ShortHelp(),
                                                      String.Empty, HandleCommanderCommand);
                    }
                }
            }
        }

        private void HandleCommanderCommand(string module, string[] cmd)
        {
            m_sceneManager.SendCommandToPluginModules(cmd);
        }

        private void HandleCommanderHelp(string module, string[] cmd)
        {
            // Only safe for the interactive console, since it won't
            // let us come here unless both scene and commander exist
            //
            ICommander moduleCommander = SceneManager.CurrentOrFirstScene.GetCommander(cmd[1]);
            if (moduleCommander != null)
                m_console.Output(moduleCommander.Help);
        }

        protected void Initialize()
        {
            m_httpServerPort = m_configSettings.HttpListenerPort;
            m_sceneManager.OnRestartSim += handleRestartRegion;
        }

        /// <summary>
        /// Execute the region creation process.  This includes setting up scene infrastructure.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="portadd_flag"></param>
        /// <returns></returns>
        public IClientNetworkServer CreateRegion(RegionInfo regionInfo, bool portadd_flag, out IScene scene)
        {
            return CreateRegion(regionInfo, portadd_flag, false, out scene);
        }

        /// <summary>
        /// Execute the region creation process.  This includes setting up scene infrastructure.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <returns></returns>
        public IClientNetworkServer CreateRegion(RegionInfo regionInfo, out IScene scene)
        {
            return CreateRegion(regionInfo, false, true, out scene);
        }

        /// <summary>
        /// Execute the region creation process.  This includes setting up scene infrastructure.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="portadd_flag"></param>
        /// <param name="do_post_init"></param>
        /// <returns></returns>
        public IClientNetworkServer CreateRegion(RegionInfo regionInfo, bool portadd_flag, bool do_post_init, out IScene mscene)
        {
            int port = regionInfo.InternalEndPoint.Port;

            // set initial RegionID to originRegionID in RegionInfo. (it needs for loding prims)
            // Commented this out because otherwise regions can't register with
            // the grid as there is already another region with the same UUID
            // at those coordinates. This is required for the load balancer to work.
            // --Mike, 2009.02.25
            //regionInfo.originRegionID = regionInfo.RegionID;

            // set initial ServerURI
            regionInfo.ServerURI = "http://" + regionInfo.ExternalHostName + ":" + regionInfo.InternalEndPoint.Port;
            regionInfo.HttpPort = m_httpServerPort;
            
            regionInfo.osSecret = m_osSecret;
            
            if ((proxyUrl.Length > 0) && (portadd_flag))
            {
                // set proxy url to RegionInfo
                regionInfo.proxyUrl = proxyUrl;
                regionInfo.ProxyOffset = proxyOffset;
                Util.XmlRpcCommand(proxyUrl, "AddPort", port, port + proxyOffset, regionInfo.ExternalHostName);
            }

            IClientNetworkServer clientServer;
            Scene scene = SetupScene(regionInfo, proxyOffset, m_config.Source, out clientServer);

            m_log.Info("[MODULES]: Loading Region's modules (old style)");

            List<IRegionModule> modules = m_moduleLoader.PickupModules(scene, ".");

            // This needs to be ahead of the script engine load, so the
            // script module can pick up events exposed by a module
            m_moduleLoader.InitialiseSharedModules(scene);

            // Use this in the future, the line above will be deprecated soon
            m_log.Info("[MODULES]: Loading Region's modules (new style)");
            IRegionModulesController controller;
            if (ApplicationRegistry.TryGet(out controller))
            {
                controller.AddRegionToModules(scene);
            }
            else m_log.Error("[MODULES]: The new RegionModulesController is missing...");

            scene.SetModuleInterfaces();

            // Prims have to be loaded after module configuration since some modules may be invoked during the load
            scene.LoadPrimsFromStorage(regionInfo.originRegionID);
            
            // TODO : Try setting resource for region xstats here on scene
            MainServer.Instance.AddStreamHandler(new Region.Framework.Scenes.RegionStatsHandler(regionInfo)); 
            
            try
            {
                scene.RegisterRegionWithGrid();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[STARTUP]: Registration of region with grid failed, aborting startup due to {0} {1}", 
                    e.Message, e.StackTrace);

                // Carrying on now causes a lot of confusion down the
                // line - we need to get the user's attention
                Environment.Exit(1);
            }

            scene.loadAllLandObjectsFromStorage(regionInfo.originRegionID);
            scene.EventManager.TriggerParcelPrimCountUpdate();

            // We need to do this after we've initialized the
            // scripting engines.
            scene.CreateScriptInstances();

            m_sceneManager.Add(scene);

            if (m_autoCreateClientStack)
            {
                m_clientServers.Add(clientServer);
                clientServer.Start();
            }

            if (do_post_init)
            {
                foreach (IRegionModule module in modules)
                {
                    module.PostInitialise();
                }
            }
            scene.EventManager.OnShutdown += delegate() { ShutdownRegion(scene); };

            mscene = scene;

            scene.StartTimer();

            return clientServer;
        }

        private void ShutdownRegion(Scene scene)
        {
            m_log.DebugFormat("[SHUTDOWN]: Shutting down region {0}", scene.RegionInfo.RegionName);
            IRegionModulesController controller;
            if (ApplicationRegistry.TryGet<IRegionModulesController>(out controller))
            {
                controller.RemoveRegionFromModules(scene);
            }
        }

        public void RemoveRegion(Scene scene, bool cleanup)
        {
            // only need to check this if we are not at the
            // root level
            if ((m_sceneManager.CurrentScene != null) &&
                (m_sceneManager.CurrentScene.RegionInfo.RegionID == scene.RegionInfo.RegionID))
            {
                m_sceneManager.TrySetCurrentScene("..");
            }

            scene.DeleteAllSceneObjects();
            m_sceneManager.CloseScene(scene);
            ShutdownClientServer(scene.RegionInfo);
            
            if (!cleanup)
                return;

            if (!String.IsNullOrEmpty(scene.RegionInfo.RegionFile))
            {
                if (scene.RegionInfo.RegionFile.ToLower().EndsWith(".xml"))
                {
                    File.Delete(scene.RegionInfo.RegionFile);
                    m_log.InfoFormat("[OPENSIM]: deleting region file \"{0}\"", scene.RegionInfo.RegionFile);
                }
                if (scene.RegionInfo.RegionFile.ToLower().EndsWith(".ini"))
                {
                    try
                    {
                        IniConfigSource source = new IniConfigSource(scene.RegionInfo.RegionFile);
                        if (source.Configs[scene.RegionInfo.RegionName] != null)
                        {
                            source.Configs.Remove(scene.RegionInfo.RegionName);

                            if (source.Configs.Count == 0)
                            {
                                File.Delete(scene.RegionInfo.RegionFile);
                            }
                            else
                            {
                                source.Save(scene.RegionInfo.RegionFile);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public void RemoveRegion(string name, bool cleanUp)
        {
            Scene target;
            if (m_sceneManager.TryGetScene(name, out target))
                RemoveRegion(target, cleanUp);
        }

        /// <summary>
        /// Remove a region from the simulator without deleting it permanently.
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public void CloseRegion(Scene scene)
        {
            // only need to check this if we are not at the
            // root level
            if ((m_sceneManager.CurrentScene != null) &&
                (m_sceneManager.CurrentScene.RegionInfo.RegionID == scene.RegionInfo.RegionID))
            {
                m_sceneManager.TrySetCurrentScene("..");
            }

            m_sceneManager.CloseScene(scene);
            ShutdownClientServer(scene.RegionInfo);
        }
        
        /// <summary>
        /// Remove a region from the simulator without deleting it permanently.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public void CloseRegion(string name)
        {
            Scene target;
            if (m_sceneManager.TryGetScene(name, out target))
                CloseRegion(target);
        }
        
        /// <summary>
        /// Create a scene and its initial base structures.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="clientServer"> </param>
        /// <returns></returns>
        protected Scene SetupScene(RegionInfo regionInfo, out IClientNetworkServer clientServer)
        {
            return SetupScene(regionInfo, 0, null, out clientServer);
        }

        /// <summary>
        /// Create a scene and its initial base structures.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="proxyOffset"></param>
        /// <param name="configSource"></param>
        /// <param name="clientServer"> </param>
        /// <returns></returns>
        protected Scene SetupScene(
            RegionInfo regionInfo, int proxyOffset, IConfigSource configSource, out IClientNetworkServer clientServer)
        {
            AgentCircuitManager circuitManager = new AgentCircuitManager();
            IPAddress listenIP = regionInfo.InternalEndPoint.Address;
            //if (!IPAddress.TryParse(regionInfo.InternalEndPoint, out listenIP))
            //    listenIP = IPAddress.Parse("0.0.0.0");

            uint port = (uint) regionInfo.InternalEndPoint.Port;

            if (m_autoCreateClientStack)
            {
                clientServer
                    = m_clientStackManager.CreateServer(
                        listenIP, ref port, proxyOffset, regionInfo.m_allow_alternate_ports, configSource,
                        circuitManager);
            }
            else
            {
                clientServer = null;
            }

            regionInfo.InternalEndPoint.Port = (int) port;

            Scene scene = CreateScene(regionInfo, m_simulationDataService, m_estateDataService, circuitManager);

            if (m_autoCreateClientStack)
            {
                clientServer.AddScene(scene);
            }

            scene.LoadWorldMap();

            scene.PhysicsScene = GetPhysicsScene(scene.RegionInfo.RegionName);
            scene.PhysicsScene.SetTerrain(scene.Heightmap.GetFloatsSerialised());
            scene.PhysicsScene.SetWaterLevel((float) regionInfo.RegionSettings.WaterHeight);

            return scene;
        }

        protected ClientStackManager CreateClientStackManager()
        {
            return new ClientStackManager(m_configSettings.ClientstackDll);
        }

        protected Scene CreateScene(RegionInfo regionInfo, ISimulationDataService simDataService,
            IEstateDataService estateDataService, AgentCircuitManager circuitManager)
        {
            SceneCommunicationService sceneGridService = new SceneCommunicationService();

            return new Scene(
                regionInfo, circuitManager, sceneGridService, m_moduleContainer,
                simDataService, estateDataService, m_moduleLoader, false, m_configSettings.PhysicalPrim,
                m_configSettings.See_into_region_from_neighbor, m_config.Source, m_version);
        }
        
        protected void ShutdownClientServer(RegionInfo whichRegion)
        {
            // Close and remove the clientserver for a region
            bool foundClientServer = false;
            int clientServerElement = 0;
            Location location = new Location(whichRegion.RegionHandle);

            for (int i = 0; i < m_clientServers.Count; i++)
            {
                if (m_clientServers[i].HandlesRegion(location))
                {
                    clientServerElement = i;
                    foundClientServer = true;
                    break;
                }
            }

            if (foundClientServer)
            {
                m_clientServers[clientServerElement].NetworkStop();
                m_clientServers.RemoveAt(clientServerElement);
            }
        }
        
        public void handleRestartRegion(RegionInfo whichRegion)
        {
            m_log.Info("[OPENSIM]: Got restart signal from SceneManager");

            ShutdownClientServer(whichRegion);
            IScene scene;
            CreateRegion(whichRegion, true, out scene);
        }

        # region Setup methods

        protected PhysicsScene GetPhysicsScene(string osSceneIdentifier)
        {
            return GetPhysicsScene(
                m_configSettings.PhysicsEngine, m_configSettings.MeshEngineName, m_config.Source, osSceneIdentifier);
        }

        /// <summary>
        /// Handler to supply the current status of this sim
        /// </summary>
        /// Currently this is always OK if the simulator is still listening for connections on its HTTP service
        public class SimStatusHandler : IStreamedRequestHandler
        {
            public byte[] Handle(string path, Stream request,
                                 OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                return Util.UTF8.GetBytes("OK");
            }

            public string ContentType
            {
                get { return "text/plain"; }
            }

            public string HttpMethod
            {
                get { return "GET"; }
            }

            public string Path
            {
                get { return "/simstatus/"; }
            }
        }

        /// <summary>
        /// Handler to supply the current extended status of this sim
        /// Sends the statistical data in a json serialization 
        /// </summary>
        public class XSimStatusHandler : IStreamedRequestHandler
        {
            OpenSimBase m_opensim;
            string osXStatsURI = String.Empty;
        
            public XSimStatusHandler(OpenSimBase sim)
            {
                m_opensim = sim;
                osXStatsURI = Util.SHA1Hash(sim.osSecret);
            }
            
            public byte[] Handle(string path, Stream request,
                                 OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                return Util.UTF8.GetBytes(m_opensim.StatReport(httpRequest));
            }

            public string ContentType
            {
                get { return "text/plain"; }
            }

            public string HttpMethod
            {
                get { return "GET"; }
            }

            public string Path
            {
                // This is for the OpenSimulator instance and is the osSecret hashed
                get { return "/" + osXStatsURI + "/"; }
            }
        }

        /// <summary>
        /// Handler to supply the current extended status of this sim to a user configured URI
        /// Sends the statistical data in a json serialization 
        /// If the request contains a key, "callback" the response will be wrappend in the 
        /// associated value for jsonp used with ajax/javascript
        /// </summary>
        public class UXSimStatusHandler : IStreamedRequestHandler
        {
            OpenSimBase m_opensim;
            string osUXStatsURI = String.Empty;
        
            public UXSimStatusHandler(OpenSimBase sim)
            {
                m_opensim = sim;
                osUXStatsURI = sim.userStatsURI;
                
            }
            
            public byte[] Handle(string path, Stream request,
                                 OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                return Util.UTF8.GetBytes(m_opensim.StatReport(httpRequest));
            }

            public string ContentType
            {
                get { return "text/plain"; }
            }

            public string HttpMethod
            {
                get { return "GET"; }
            }

            public string Path
            {
                // This is for the OpenSimulator instance and is the user provided URI 
                get { return "/" + osUXStatsURI + "/"; }
            }
        }

        #endregion

        /// <summary>
        /// Performs any last-minute sanity checking and shuts down the region server
        /// </summary>
        public virtual void ShutdownSpecific()
        {
            if (proxyUrl.Length > 0)
            {
                Util.XmlRpcCommand(proxyUrl, "Stop");
            }

            m_log.Info("[SHUTDOWN]: Closing all threads");
            m_log.Info("[SHUTDOWN]: Killing listener thread");
            m_log.Info("[SHUTDOWN]: Killing clients");
            // TODO: implement this
            m_log.Info("[SHUTDOWN]: Closing console and terminating");

            try
            {
                m_sceneManager.Close();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[SHUTDOWN]: Ignoring failure during shutdown - {0}", e);
            }
        }

        /// <summary>
        /// Get the start time and up time of Region server
        /// </summary>
        /// <param name="starttime">The first out parameter describing when the Region server started</param>
        /// <param name="uptime">The second out parameter describing how long the Region server has run</param>
        public void GetRunTime(out string starttime, out string uptime)
        {
            starttime = m_startuptime.ToString();
            uptime = (DateTime.Now - m_startuptime).ToString();
        }

        /// <summary>
        /// Get the number of the avatars in the Region server
        /// </summary>
        /// <param name="usernum">The first out parameter describing the number of all the avatars in the Region server</param>
        public void GetAvatarNumber(out int usernum)
        {
            usernum = m_sceneManager.GetCurrentSceneAvatars().Count;
        }

        /// <summary>
        /// Get the number of regions
        /// </summary>
        /// <param name="regionnum">The first out parameter describing the number of regions</param>
        public void GetRegionNumber(out int regionnum)
        {
            regionnum = m_sceneManager.Scenes.Count;
        }
        
        /// <summary>
        /// Load the estate information for the provided RegionInfo object.
        /// </summary>
        /// <param name="regInfo">
        /// A <see cref="RegionInfo"/>
        /// </param>
        public void PopulateRegionEstateInfo(RegionInfo regInfo)
        {
            IEstateDataService estateDataService = EstateDataService;

            if (estateDataService != null)
            {
                regInfo.EstateSettings = estateDataService.LoadEstateSettings(regInfo.RegionID, false);
            }

            if (regInfo.EstateSettings.EstateID == 0) // No record at all
            {
                MainConsole.Instance.Output("Your region is not part of an estate.");
                while (true)
                {
                    string response = MainConsole.Instance.CmdPrompt("Do you wish to join an existing estate?", "no", new List<string>() { "yes", "no" });
                    if (response == "no")
                    {
                        // Create a new estate
                        regInfo.EstateSettings = estateDataService.LoadEstateSettings(regInfo.RegionID, true);

                        regInfo.EstateSettings.EstateName = MainConsole.Instance.CmdPrompt("New estate name", regInfo.EstateSettings.EstateName);
                        //regInfo.EstateSettings.Save();
                        break;
                    }
                    else
                    {
                        response = MainConsole.Instance.CmdPrompt("Estate name to join", "None");
                        if (response == "None")
                            continue;

                        List<int> estateIDs = estateDataService.GetEstates(response);
                        if (estateIDs.Count < 1)
                        {
                            MainConsole.Instance.Output("The name you have entered matches no known estate. Please try again");
                            continue;
                        }

                        int estateID = estateIDs[0];

                        regInfo.EstateSettings = estateDataService.LoadEstateSettings(estateID);

                        if (estateDataService.LinkRegion(regInfo.RegionID, estateID))
                            break;

                        MainConsole.Instance.Output("Joining the estate failed. Please try again.");
                    }
                }
            }
        }
    }

    
    public class OpenSimConfigSource
    {
        public IConfigSource Source;

        public void Save(string path)
        {
            if (Source is IniConfigSource)
            {
                IniConfigSource iniCon = (IniConfigSource) Source;
                iniCon.Save(path);
            }
            else if (Source is XmlConfigSource)
            {
                XmlConfigSource xmlCon = (XmlConfigSource) Source;
                xmlCon.Save(path);
            }
        }
    }
}
