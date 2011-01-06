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
using Nini.Config;

namespace OpenSim
{
    /*
    ** *** First Run ... Lots & lots of comments ***
    ** The OpenSimulator configuration system could be better. There is
    ** no way to enforce a scheme to track and enforce sane configuration
    ** management in the OpenSimulator application. In preliminary research,
    ** I have found two configuration systems in use. The application is far
    ** too large and complex to allow such lax practices. For instance - how
    ** would a person go about gathering all the configuration options in the
    ** application to construct a complete set of initialization files? How
    ** would one know when a new module is introduced, whether the author
    ** updated the files needed to configure the applicaton? What if we want
    ** to grow a more sophisticated configuration management system? The
    ** present system just won't cut it. Several attempts to improve the
    ** current mechanisim have just moved files around and changed the format
    ** a little. That might help on the surface of things. But, there is no real
    ** way to manage the configuration sources, sections and options.
    **
    ** So, here is a shot at organizing some of the disparate parts into a
    ** more useable component that will facilitate the configuration tasks in
    ** a more organized and usable way.
    **
    ** Need to handle configuration sources:
    **   *command line switches - ro
    **   *shell environment - rw
    **   *local ini files - rw
    **   *local xml files - rw
    **   *remote sources via http - ro
    **   *database - rw
    **
    ** We know that we already handle command line switches. And any of
    ** the (upcoming) shell environment processing should take place here
    ** at the start as well. What happens here will determine what goes next.
    ** We will be the single configuration source for the system, and will
    ** manage the application's options.
    **
    ** Every module and plugin will be required, via their base interface,
    ** to declare the options required to operate the module and their
    ** default settings. These will be used to configure default settings
    ** for the system and generate default ini files. This data will, then,
    ** be used to construct complete configuration documentation from a
    ** console command.
    **
    **
    */

    /*
    ** Might try declaring the architecture of our installation early on
    ** to have the application load up the right set of configurations
     **
    ** Look at making a configuration option to ask to load the module.
    ** If the configuration doesn't ask to load the module, it is skipped.
     **
    ** Can and event help enforce the configuration registrations?
    */

    /// <summary>
    /// OpenSimulator Configuritaion Manager
    /// New Configuration System
    ///
    /// </summary>
    public class ConfigManager
    {
        IConfigSource m_ArgConfigCource = null;
        IConfigSource m_EnvConfigSource = null;

        /// <summary>
        /// Constructor
        /// After we get this set, we will be able to chart a course to our next
        /// point - either using files in our filesystem, load a url or get
        /// our settings from a database
        /// <param name="argv">initial command line arguments</param>
        /// </summary>
        public ConfigManager(string[] argv)
        {
            m_ArgConfigCource = new  ArgvConfigSource(argv);
            m_EnvConfigSource = new EnvConfigSource();
        }

        /*
         * Might call a function to see if all modules have declared their
         * configuration items to the application just prior to loading
         * the first scene. Refuse to run until all have registered.
        */
    }
}

