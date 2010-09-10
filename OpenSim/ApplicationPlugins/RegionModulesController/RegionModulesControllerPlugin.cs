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
using OpenSim;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.ApplicationPlugins.RegionModulesController
{
    [ApplicationModule("RegionModulesController")]
    public class RegionModulesControllerPlugin : IRegionModulesController, IApplicationPlugin
    {
        // Logger
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        // Config access
        private OpenSimBase m_openSim;

        // Internal lists to collect information about modules present
        private List<INonSharedRegionModule> m_nonSharedModules = new List<INonSharedRegionModule>();
        private List<ISharedRegionModule> m_sharedModules = new List<ISharedRegionModule>();

        // List of shared module instances, for adding to Scenes
        //private List<ISharedRegionModule> m_sharedInstances =
        //        new List<ISharedRegionModule>();

#region IApplicationPlugin implementation

        public void Initialise (OpenSimBase openSim)
        {
            m_openSim = openSim;
            m_openSim.ApplicationRegistry.RegisterInterface<IRegionModulesController>(this);
            m_log.DebugFormat("[REGIONMODULES]: Initializing...");

            // The [Modules] section in the ini file
            IConfig modulesConfig = m_openSim.ConfigSource.Source.Configs["Modules"];
            if (modulesConfig == null)
                modulesConfig = m_openSim.ConfigSource.Source.AddConfig("Modules");

            CompositionContainer moduleContainer = openSim.ModuleContainer;
            IEnumerable<Lazy<object, object>> exportEnumerable = moduleContainer.GetExports(typeof(IRegionModuleBase), null, null);

            foreach (Lazy<object, object> lazyExport in exportEnumerable)
            {
                IDictionary<string, object> metadata = (IDictionary<string, object>)lazyExport.Metadata;
                object nameObj;
                if (metadata.TryGetValue("Name", out nameObj))
                {
                    string name = (string)nameObj;

                    // TODO: Whitelist before we call lazyExport.Value, which instantiates
                    if (lazyExport.Value is ISharedRegionModule)
                    {
                        m_log.DebugFormat("[REGIONMODULES]: Found shared region module {0}", name);
                        m_sharedModules.Add((ISharedRegionModule)lazyExport.Value);
                    }
                    else if (lazyExport.Value is INonSharedRegionModule)
                    {
                        m_log.DebugFormat("[REGIONMODULES]: Found non-shared region module {0}", name);
                        m_nonSharedModules.Add((INonSharedRegionModule)lazyExport.Value);
                    }
                }
            }

            foreach (ISharedRegionModule node in m_sharedModules)
            {
                // OK, we're up and running
                node.Initialise(m_openSim.ConfigSource.Source);
            }
        }

        public void PostInitialise ()
        {
            m_log.DebugFormat("[REGIONMODULES]: PostInitializing...");

            // Immediately run PostInitialise on shared modules
            foreach (ISharedRegionModule module in m_sharedModules)
            {
                module.PostInitialise();
            }
        }

#endregion

#region IPlugin implementation

        // We don't do that here
        //
        public void Initialise ()
        {
            throw new System.NotImplementedException();
        }

#endregion

#region IDisposable implementation

        // Cleanup
        //
        public void Dispose ()
        {
            // We expect that all regions have been removed already
            while (m_sharedModules.Count > 0)
            {
                m_sharedModules[0].Close();
                m_sharedModules.RemoveAt(0);
            }
            m_nonSharedModules.Clear();
        }

#endregion


        public string Version
        {
            get
            {
                return "1.0";
            }
        }

        public string Name
        {
            get
            {
                return "RegionModulesController";
            }
        }

#region IRegionModulesController implementation

        // The root of all evil.
        // This is where we handle adding the modules to scenes when they
        // load
        public void AddRegionToModules (Scene scene)
        {
            // This will hold the shared modules we actually load
            List<ISharedRegionModule> sharedlist = new List<ISharedRegionModule>();

            // Iterate over the shared modules that have been loaded
            // Add them to the new Scene
            foreach (ISharedRegionModule module in m_sharedModules)
            {
                m_log.DebugFormat("[REGIONMODULE]: Adding scene {0} to shared module {1}",
                                  scene.RegionInfo.RegionName, module.Name);

                module.AddRegion(scene);
                scene.AddRegionModule(module.Name, module);

                sharedlist.Add(module);
            }

            IConfig modulesConfig =
                    m_openSim.ConfigSource.Source.Configs["Modules"];

            // Scan for, and load, nonshared modules
            List<INonSharedRegionModule> list = new List<INonSharedRegionModule>();
            foreach (INonSharedRegionModule module in m_nonSharedModules)
            {
                m_log.DebugFormat("[REGIONMODULE]: Adding scene {0} to non-shared module {1}",
                                  scene.RegionInfo.RegionName, module.Name);

                // Initialise the module
                module.Initialise(m_openSim.ConfigSource.Source);

                list.Add(module);
            }

            // Now add the modules that we found to the scene
            foreach (INonSharedRegionModule module in list)
            {
                module.AddRegion(scene);
                scene.AddRegionModule(module.Name, module);
            }

            // This is needed for all module types. Modules will register
            // Interfaces with scene in AddScene, and will also need a means
            // to access interfaces registered by other modules. Without
            // this extra method, a module attempting to use another modules's
            // interface would be successful only depending on load order,
            // which can't be depended upon, or modules would need to resort
            // to ugly kludges to attempt to request interfaces when needed
            // and unneccessary caching logic repeated in all modules.
            // The extra function stub is just that much cleaner
            foreach (ISharedRegionModule module in sharedlist)
                module.RegionLoaded(scene);

            foreach (INonSharedRegionModule module in list)
                module.RegionLoaded(scene);
        }

        public void RemoveRegionFromModules (Scene scene)
        {
            foreach (IRegionModuleBase module in scene.RegionModules.Values)
            {
                m_log.DebugFormat("[REGIONMODULE]: Removing scene {0} from module {1}",
                                  scene.RegionInfo.RegionName, module.Name);
                module.RemoveRegion(scene);
                if (module is INonSharedRegionModule)
                {
                    // as we were the only user, this instance has to die
                    module.Close();
                }
            }
            scene.RegionModules.Clear();
        }

#endregion

    }
}
