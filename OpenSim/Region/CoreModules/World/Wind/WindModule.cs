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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

using OpenSim.Region.CoreModules.World.Wind;

namespace OpenSim.Region.CoreModules
{
    [RegionModuleDeprecated("WindModule")]
    public class WindModule : IWindModule
    {
        private const string DEFAULT_WIND_PLUGIN = "SimpleRandomWind";

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private uint m_frame;
        private uint m_frameLastUpdateClientArray;
        private int m_frameUpdateRate = 150;
        private Scene m_scene;
        private bool m_ready;
        private bool m_enabled;
        private IWindModelPlugin m_activeWindPlugin;
        // Simplified windSpeeds based on the fact that the client protocal tracks at a resolution of 16m
        private Vector2[] windSpeeds = new Vector2[16 * 16];

        #region IRegion Methods

        public void Initialise(Scene scene, IConfigSource config)
        {
            string desiredWindPlugin = DEFAULT_WIND_PLUGIN;

            IConfig windConfig = config.Configs["Wind"];
            if (windConfig != null)
            {
                m_enabled = windConfig.GetBoolean("enabled", true);
                m_frameUpdateRate = windConfig.GetInt("wind_update_rate", 150);

                // Determine which wind model plugin is desired
                if (windConfig.Contains("wind_plugin"))
                    desiredWindPlugin = windConfig.GetString("wind_plugin");
            }

            if (m_enabled)
            {
                m_log.InfoFormat("[WIND] Enabled with an update rate of {0} frames.", m_frameUpdateRate);

                m_scene = scene;
                m_frame = 0;

                if (windConfig != null)
                {
                    CompositionContainer moduleContainer = scene.ModuleContainer;
                    IEnumerable<Lazy<object, object>> exportEnumerable = moduleContainer.GetExports(typeof(IPlugin), null, null);

                    foreach (Lazy<object, object> lazyExport in exportEnumerable)
                    {
                        IDictionary<string, object> metadata = (IDictionary<string, object>)lazyExport.Metadata;
                        object nameObj;
                        if (metadata.TryGetValue("Name", out nameObj))
                        {
                            string name = (string)nameObj;
                            if (name.Equals(desiredWindPlugin, StringComparison.InvariantCultureIgnoreCase) && lazyExport.Value is IWindModelPlugin)
                            {
                                m_log.InfoFormat("[WIND] {0} plugin found, initializing.", desiredWindPlugin);

                                m_activeWindPlugin = (IWindModelPlugin)lazyExport.Value;
                                m_activeWindPlugin.Initialise();
                                m_activeWindPlugin.WindConfig(m_scene, windConfig);
                                break;
                            }
                        }
                    }
                }

                // if the plug-in wasn't found, default to no wind.
                if (m_activeWindPlugin == null)
                {
                    m_log.ErrorFormat("[WIND] Could not find specified wind plug-in: {0}", desiredWindPlugin);
                    m_log.ErrorFormat("[WIND] Defaulting to no wind.");
                }

                // This one puts an entry in the main help screen
                m_scene.AddCommand(this, String.Empty, "wind", "Usage: wind <param> [value] - Get or Update Wind paramaters", null);
                
                // This one enables the ability to type just the base command without any parameters
                m_scene.AddCommand(this, "wind", "", "", HandleConsoleCommand);

                // Get a list of the parameters for the plugin
                if (m_activeWindPlugin != null)
                {
                    m_scene.AddCommand(this, String.Format("wind wind_update_rate"), "Change the wind update rate.", "", HandleConsoleBaseCommand);

                    foreach (KeyValuePair<string, string> kvp in m_activeWindPlugin.WindParams())
                    {
                        m_scene.AddCommand(this, String.Format("wind {0} {1}", m_activeWindPlugin.Name, kvp.Key),
                            String.Format("{0} : {1} - {2}", m_activeWindPlugin.Name, kvp.Key, kvp.Value), "", HandleConsoleParamCommand);
                    }
                }

                // Register event handlers for when Avatars enter the region, and frame ticks
                m_scene.EventManager.OnFrame += WindUpdate;
                m_scene.EventManager.OnMakeRootAgent += OnAgentEnteredRegion;

                // Register the wind module 
                m_scene.RegisterModuleInterface<IWindModule>(this);

                // Generate initial wind values
                GenWindPos();

                // Mark Module Ready for duty
                m_ready = true;
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (m_enabled)
            {
                m_ready = false;

                // REVIEW: If a region module is closed, is there a possibility that it'll re-open/initialize ??
                if (m_activeWindPlugin != null)
                {
                    m_activeWindPlugin.Dispose();
                    m_activeWindPlugin = null;
                }

                //  Remove our hooks
                m_scene.EventManager.OnFrame -= WindUpdate;
                m_scene.EventManager.OnMakeRootAgent -= OnAgentEnteredRegion;
            }
        }

        public string Name
        {
            get { return "WindModule"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }


        #endregion

        #region Console Commands
        private void ValidateConsole()
        {
            if (m_scene.ConsoleScene() == null)
            {
                // FIXME: If console region is root then this will be printed by every module.  Currently, there is no
                // way to prevent this, short of making the entire module shared (which is complete overkill).
                // One possibility is to return a bool to signal whether the module has completely handled the command
                m_log.InfoFormat("[WIND]: Please change to a specific region in order to set Sun parameters.");
                return;
            }

            if (m_scene.ConsoleScene() != m_scene)
            {
                m_log.InfoFormat("[WIND]: Console Scene is not my scene.");
                return;
            }
        }

        /// <summary>
        /// Base console command handler, only used if a person specifies the base command with now options
        /// </summary>
        private void HandleConsoleCommand(string module, string[] cmdparams)
        {
            ValidateConsole();
            m_log.Info("[WIND] The wind command can be used to change the currently active wind model plugin and update the parameters for wind plugins.");
        }

        private void HandleConsoleBaseCommand(string module, string[] cmdparams)
        {
            ValidateConsole();

            if (cmdparams.Length != 3)
            {
                m_log.Info("[WIND] Invalid parameters to change parameters for Wind module base, usage: wind base <parameter> <value>");
                return;
            }

            switch (cmdparams[1])
            {
                case "wind_update_rate":
                    int newRate = 1;

                    if (int.TryParse(cmdparams[2], out newRate))
                    {
                        m_frameUpdateRate = newRate;
                    }
                    else
                    {
                        m_log.InfoFormat("[WIND] Invalid value {0} specified for {1}", cmdparams[2], cmdparams[1]);
                        return;
                    }

                    break;
            }
        }

        /// <summary>
        /// Called to change plugin parameters.
        /// </summary>
        private void HandleConsoleParamCommand(string module, string[] cmdparams)
        {
            ValidateConsole();

            // wind <plugin> <param> [value]
            if (cmdparams.Length != 3 && cmdparams.Length != 2)
            {
                m_log.Info("[WIND] Usage: wind <param> [value]");
                return;
            }

            string param = cmdparams[1];
            float value = 0f;
            if (cmdparams.Length == 3)
            {
                if (!float.TryParse(cmdparams[2], out value))
                {
                    m_log.InfoFormat("[WIND] Invalid value {0}", cmdparams[2]);
                }

                try
                {
                    WindParamSet(param, value);
                }
                catch (Exception e)
                {
                    m_log.InfoFormat("[WIND] {0}", e.Message);
                }
            }
            else
            {
                try
                {
                    value = WindParamGet(param);
                    m_log.InfoFormat("[WIND] {0} : {1}", param, value);
                }
                catch (Exception e)
                {
                    m_log.InfoFormat("[WIND] {0}", e.Message);
                }
            }

        }
        #endregion


        #region IWindModule Methods

        /// <summary>
        /// Retrieve the wind speed at the given region coordinate.  This 
        /// implimentation ignores Z.
        /// </summary>
        /// <param name="x">0...255</param>
        /// <param name="y">0...255</param>
        public Vector3 WindSpeed(int x, int y, int z)
        {
            if (m_activeWindPlugin != null)
            {
                return m_activeWindPlugin.WindSpeed(x, y, z);
            }
            else
            {
                return new Vector3(0.0f, 0.0f, 0.0f);
            }
        }

        public void WindParamSet(string param, float value)
        {
            if (m_activeWindPlugin != null)
            {
                m_activeWindPlugin.WindParamSet(param, value);
                m_log.InfoFormat("[WIND] {0} set to {1}", param, value);
            }
        }

        public float WindParamGet(string param)
        {
            if (m_activeWindPlugin != null)
                return m_activeWindPlugin.WindParamGet(param);
            return 0.0f;
        }

        public string WindActiveModelPluginName
        {
            get 
            {
                if (m_activeWindPlugin != null)
                {
                    return m_activeWindPlugin.Name;
                }
                else
                {
                    return String.Empty;
                }
            }
        }

        #endregion

        /// <summary>
        /// Called on each frame update.  Updates the wind model and clients as necessary.
        /// </summary>
        public void WindUpdate()
        {
            if (((m_frame++ % m_frameUpdateRate) != 0) || !m_ready)
            {
                return;
            }

            GenWindPos();

            SendWindAllClients();
        }

        public void OnAgentEnteredRegion(ScenePresence avatar)
        {
            if (m_ready)
            {
                if (m_activeWindPlugin != null)
                {
                    // Ask wind plugin to generate a LL wind array to be cached locally
                    // Try not to update this too often, as it may involve array copies
                    if (m_frame >= (m_frameLastUpdateClientArray + m_frameUpdateRate))
                    {
                        windSpeeds = m_activeWindPlugin.WindLLClientArray();
                        m_frameLastUpdateClientArray = m_frame;
                    }
                }

                avatar.ControllingClient.SendWindData(windSpeeds);
            }
        }

        private void SendWindAllClients()
        {
            if (m_ready)
            {
                if (m_scene.GetRootAgentCount() > 0)
                {
                    // Ask wind plugin to generate a LL wind array to be cached locally
                    // Try not to update this too often, as it may involve array copies
                    if (m_frame >= (m_frameLastUpdateClientArray + m_frameUpdateRate))
                    {
                        windSpeeds = m_activeWindPlugin.WindLLClientArray();
                        m_frameLastUpdateClientArray = m_frame;
                    }

                    m_scene.ForEachScenePresence(delegate(ScenePresence sp)
                    {
                        if (!sp.IsChildAgent)
                            sp.ControllingClient.SendWindData(windSpeeds);
                    });
                }
            }
        }
        /// <summary>
        /// Calculate the sun's orbital position and its velocity.
        /// </summary>

        private void GenWindPos()
        {
            if (m_activeWindPlugin != null)
            {
                // Tell Wind Plugin to update it's wind data
                m_activeWindPlugin.WindUpdate(m_frame);
            }
        }
    }
}
