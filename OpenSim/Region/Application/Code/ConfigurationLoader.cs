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
using System.IO;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenSim.Framework;

namespace OpenSim
{
    /// <summary>
    /// Loads the Configuration files into nIni
    /// </summary>
    public class ConfigurationLoader
    {
        private const string DEFAULT_INI_MASTER_FILE = "OpenSimDefaults.ini";
        private const string DEFAULT_INI_OVERRIDES_FILE = "OpenSim.ini";

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Various Config settings the region needs to start
        /// Physics Engine, Mesh Engine, GridMode, PhysicsPrim allowed, Neighbor, 
        /// StorageDLL, Storage Connection String, Estate connection String, Client Stack
        /// Standalone settings.
        /// </summary>
        protected ConfigSettings m_configSettings;

        /// <summary>
        /// A source of Configuration data
        /// </summary>
        protected OpenSimConfigSource m_config;

        public OpenSimConfigSource LoadConfigSettings(
                IConfigSource argvSource, out ConfigSettings configSettings)
        {
            IConfig startupConfig = argvSource.Configs["Startup"];
            string masterFilename = startupConfig.GetString("inimaster", DEFAULT_INI_MASTER_FILE);
            string iniFilename = startupConfig.GetString("inifile", DEFAULT_INI_OVERRIDES_FILE);

            // Load the master ini file
            IniConfigSource masterConfig = LoadConfig(masterFilename);

            // Load the overrides ini file
            IniConfigSource overridesConfig = LoadConfig(iniFilename);

            // Merge
            masterConfig.Merge(overridesConfig);

            // Create m_config and assign masterConfig to it
            m_config = new OpenSimConfigSource();
            m_config.Source = masterConfig;

            // Create m_configSettings and set it up
            configSettings = new ConfigSettings();
            m_configSettings = configSettings;
            ReadConfigSettings();

            return m_config;
        }

        protected void ReadConfigSettings()
        {
            IConfig startupConfig = m_config.Source.Configs["Startup"];
            if (startupConfig != null)
            {
                m_configSettings.PhysicsEngine = startupConfig.GetString("physics");
                m_configSettings.MeshEngineName = startupConfig.GetString("meshing");
                m_configSettings.PhysicalPrim = startupConfig.GetBoolean("physical_prim", true);

                m_configSettings.See_into_region_from_neighbor = startupConfig.GetBoolean("see_into_this_sim_from_neighbor", true);

                m_configSettings.StorageDll = startupConfig.GetString("storage_plugin");

                m_configSettings.ClientstackDll
                    = startupConfig.GetString("clientstack_plugin", "OpenSim.Region.ClientStack.LindenUDP.dll");
            }

            IConfig standaloneConfig = m_config.Source.Configs["StandAlone"];
            if (standaloneConfig != null)
            {
                m_configSettings.StandaloneAuthenticate = standaloneConfig.GetBoolean("accounts_authenticate", true);
                m_configSettings.StandaloneWelcomeMessage = standaloneConfig.GetString("welcome_message");

                m_configSettings.StandaloneInventoryPlugin = standaloneConfig.GetString("inventory_plugin");
                m_configSettings.StandaloneInventorySource = standaloneConfig.GetString("inventory_source");
                m_configSettings.StandaloneUserPlugin = standaloneConfig.GetString("userDatabase_plugin");
                m_configSettings.StandaloneUserSource = standaloneConfig.GetString("user_source");

                m_configSettings.LibrariesXMLFile = standaloneConfig.GetString("LibrariesXMLFile");
            }

            IConfigSource config = m_config.Source;
            IConfig networkConfig = config.Configs["Network"];
            if (networkConfig != null)
            {
                m_configSettings.HttpListenerPort = (uint)networkConfig.GetInt("http_listener_port", (int)ConfigSettings.DefaultRegionHttpPort);
                m_configSettings.httpSSLPort = (uint)networkConfig.GetInt("http_listener_sslport", ((int)ConfigSettings.DefaultRegionHttpPort + 1));
                m_configSettings.HttpUsesSSL = networkConfig.GetBoolean("http_listener_ssl", false);
                m_configSettings.HttpSSLCN = networkConfig.GetString("http_listener_cn", "localhost");
            }
        }

        #region Configuration Helpers

        private bool IsUrl(string file)
        {
            Uri configUri;
            return Uri.TryCreate(file, UriKind.Absolute, out configUri) &&
                (configUri.Scheme == Uri.UriSchemeHttp || configUri.Scheme == Uri.UriSchemeHttps);
        }

        private IniConfigSource LoadConfig(string location)
        {
            IniConfigSource currentConfig = new IniConfigSource();
            List<string> currentConfigLines = new List<string>();
            string[] configLines = null;

            if (IsUrl(location))
            {
                // Web-based loading
                string responseStr;
                if (WebUtil.TryGetUrl(location, out responseStr))
                {
                    configLines = responseStr.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    m_log.Error("Failed to load web config file " + location + ": " + responseStr);
                }
            }
            else
            {
                // Local file loading
                try
                {
                    configLines = new List<string>(File.ReadAllLines(location)).ToArray();
                }
                catch (Exception ex)
                {
                    // Don't print out an error message if there is no OpenSim.ini, this is the 
                    // default setup
                    if (location != DEFAULT_INI_OVERRIDES_FILE)
                        m_log.Error("Failed to load config file " + location + ": " + ex.Message);
                }
            }

            if (configLines != null)
            {
                for (int i = 0; i < configLines.Length; i++)
                {
                    string line = configLines[i].Trim();

                    if (line.StartsWith("Include "))
                    {
                        // Compile the current config lines, compile the included config file, and combine them
                        currentConfig.Merge(CompileConfig(currentConfigLines));
                        currentConfigLines.Clear();

                        string includeLocation = line.Substring(8).Trim().Trim(new char[] { '"' });

                        if (IsUrl(includeLocation))
                        {
                            IniConfigSource includeConfig = LoadConfig(includeLocation);
                            currentConfig.Merge(includeConfig);
                        }
                        else
                        {
                            string basepath = Path.GetFullPath(Util.configDir());

                            // Resolve relative paths with wildcards
                            string chunkWithoutWildcards = includeLocation;
                            string chunkWithWildcards = string.Empty;
                            int wildcardIndex = includeLocation.IndexOfAny(new char[] { '*', '?' });
                            if (wildcardIndex != -1)
                            {
                                chunkWithoutWildcards = includeLocation.Substring(0, wildcardIndex);
                                chunkWithWildcards = includeLocation.Substring(wildcardIndex);
                            }
                            string path = Path.Combine(basepath, chunkWithoutWildcards);
                            path = Path.GetFullPath(path) + chunkWithWildcards;
                            
                            string[] paths = Util.Glob(path);
                            foreach (string p in paths)
                            {
                                IniConfigSource includeConfig = LoadConfig(p);
                                currentConfig.Merge(includeConfig);
                            }
                        }
                    }
                    else if (!String.IsNullOrEmpty(line) && !line.StartsWith(";"))
                    {
                        currentConfigLines.Add(line);
                    }
                }

                currentConfig.Merge(CompileConfig(currentConfigLines));
            }

            return currentConfig;
        }

        private IniConfigSource CompileConfig(List<string> lines)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    byte[] line = Encoding.UTF8.GetBytes(lines[i]);
                    stream.Write(line, 0, line.Length);
                    stream.WriteByte(0x0A); // Linefeed
                }

                stream.Seek(0, SeekOrigin.Begin);
                return new IniConfigSource(stream);
            }
        }

        private string GetConfigPath(string filename)
        {
            if (Path.IsPathRooted(filename) || IsUrl(filename))
            {
                return filename;
            }
            else
            {
                string currentDir = Util.ExecutingDirectory();
                return Path.Combine(currentDir, filename);
            }
        }

        #endregion Configuration Helpers
    }
}
