/*
* Copyright (c) Contributors, http://www.openmetaverse.org/
* See CONTRIBUTORS.TXT for a full list of copyright holders.
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions are met:
*     * Redistributions of source code must retain the above copyright
*       notice, this list of conditions and the following disclaimer.
*     * Redistributions in binary form must reproduce the above copyright
*       notice, this list of conditions and the following disclaimer in the
*       documentation and/or other materials provided with the distribution.
*     * Neither the name of the OpenSim Project nor the
*       names of its contributors may be used to endorse or promote products
*       derived from this software without specific prior written permission.
*
* THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS AND ANY
* EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
* WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
* DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
* DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
* ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
* (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
* SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
* 
*/
using System;
using libsecondlife;
using libsecondlife.Packets;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Timers;
using OpenSim.Physics.Manager;
using OpenSim.Framework.Interfaces;
using OpenSim.Framework.Types;
using OpenSim.Framework.Inventory;
using OpenSim.Framework;
using OpenSim.Region.Scripting;
using OpenSim.Terrain;
using OpenGrid.Framework.Communications;
using OpenSim.Caches;
using OpenSim.Region;
using OpenSim.Servers;

namespace OpenSim.Region.Scenes
{
    public delegate bool FilterAvatarList(ScenePresence avatar);

    public partial class Scene : SceneBase, ILocalStorageReceiver, IScriptAPI
    {
        protected System.Timers.Timer m_heartbeatTimer = new System.Timers.Timer();
        protected Dictionary<libsecondlife.LLUUID, ScenePresence> Avatars;
        protected Dictionary<libsecondlife.LLUUID, Primitive> Prims;
        private PhysicsScene phyScene;
        private float timeStep = 0.1f;
        public ILocalStorage localStorage;
        private Random Rand = new Random();
        private uint _primCount = 702000;
        private int storageCount;
        private Dictionary<LLUUID, ScriptHandler> m_scriptHandlers;
        private Dictionary<string, ScriptFactory> m_scripts;
        private Mutex updateLock;
        public string m_datastore;
        protected AuthenticateSessionsBase authenticateHandler;
        protected RegionCommsListener regionCommsHost;
        protected CommunicationsManager commsManager;

        protected List<Caps> capsHandlers = new List<Caps>();
        protected BaseHttpServer httpListener;

        public ParcelManager parcelManager;
        public EstateManager estateManager;

        #region Properties
        /// <summary>
        /// 
        /// </summary>
        public PhysicsScene PhysScene
        {
            set
            {
                this.phyScene = value;
            }
            get
            {
                return (this.phyScene);
            }
        }

        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new World class, and a region to go with it.
        /// </summary>
        /// <param name="clientThreads">Dictionary to contain client threads</param>
        /// <param name="regionHandle">Region Handle for this region</param>
        /// <param name="regionName">Region Name for this region</param>
        public Scene(Dictionary<uint, IClientAPI> clientThreads, RegionInfo regInfo, AuthenticateSessionsBase authen, CommunicationsManager commsMan, AssetCache assetCach, BaseHttpServer httpServer)
        {
            try
            {
                updateLock = new Mutex(false);
                this.authenticateHandler = authen;
                this.commsManager = commsMan;
                this.assetCache = assetCach;
                m_clientThreads = clientThreads;
                m_regInfo = regInfo;
                m_regionHandle = m_regInfo.RegionHandle;
                m_regionName = m_regInfo.RegionName;
                this.m_datastore = m_regInfo.DataStore;
                this.RegisterRegionWithComms();

                parcelManager = new ParcelManager(this, this.m_regInfo);
                estateManager = new EstateManager(this, this.m_regInfo);

                m_scriptHandlers = new Dictionary<LLUUID, ScriptHandler>();
                m_scripts = new Dictionary<string, ScriptFactory>();

                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs - creating new entitities instance");
                Entities = new Dictionary<libsecondlife.LLUUID, Entity>();
                Avatars = new Dictionary<LLUUID, ScenePresence>();
                Prims = new Dictionary<LLUUID, Primitive>();

                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs - creating LandMap");
                TerrainManager = new TerrainManager(new SecondLife());
                Terrain = new TerrainEngine();

                ScenePresence.LoadAnims();
                this.httpListener = httpServer;
                
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Error( "World.cs: Constructor failed with exception " + e.ToString());
            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        public void StartTimer()
        {
            m_heartbeatTimer.Enabled = true;
            m_heartbeatTimer.Interval = 100;
            m_heartbeatTimer.Elapsed += new ElapsedEventHandler(this.Heartbeat);
        }


        #region Update Methods


        /// <summary>
        /// Performs per-frame updates regularly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Heartbeat(object sender, System.EventArgs e)
        {
            this.Update();
        }

        /// <summary>
        /// Performs per-frame updates on the world, this should be the central world loop
        /// </summary>
        public override void Update()
        {
            updateLock.WaitOne();
            try
            {
                if (this.phyScene.IsThreaded)
                {
                    this.phyScene.GetResults();

                }

                foreach (libsecondlife.LLUUID UUID in Entities.Keys)
                {
                    Entities[UUID].updateMovement();
                }

                lock (this.m_syncRoot)
                {
                    this.phyScene.Simulate(timeStep);
                }

                foreach (libsecondlife.LLUUID UUID in Entities.Keys)
                {
                    Entities[UUID].update();
                }

                foreach (ScriptHandler scriptHandler in m_scriptHandlers.Values)
                {
                    scriptHandler.OnFrame();
                }
                foreach (IScriptEngine scripteng in this.scriptEngines.Values)
                {
                    scripteng.OnFrame();
                }
                //backup world data
                this.storageCount++;
                if (storageCount > 1200) //set to how often you want to backup 
                {
                    this.Backup();
                    storageCount = 0;
                }
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: Update() - Failed with exception " + e.ToString());
            }
            updateLock.ReleaseMutex();

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool Backup()
        {
            /*
            try
            {
                // Terrain backup routines
                if (Terrain.tainted > 0)
                {
                    Terrain.tainted = 0;
                    OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs: Backup() - Terrain tainted, saving.");
                    localStorage.SaveMap(Terrain.getHeights1D());
                    OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs: Backup() - Terrain saved, informing Physics.");
                    lock (this.m_syncRoot)
                    {
                        phyScene.SetTerrain(Terrain.getHeights1D());
                    }
                }

                // Primitive backup routines
                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs: Backup() - Backing up Primitives");
                foreach (libsecondlife.LLUUID UUID in Entities.Keys)
                {
                    Entities[UUID].BackUp();
                }

                //Parcel backup routines
                ParcelData[] parcels = new ParcelData[parcelManager.parcelList.Count];
                int i = 0;
                foreach (OpenSim.Region.Parcel parcel in parcelManager.parcelList.Values)
                {
                    parcels[i] = parcel.parcelData;
                    i++;
                }
                localStorage.SaveParcels(parcels);

                // Backup successful
                return true;
            }
            catch (Exception e)
            {
                // Backup failed
                OpenSim.Framework.Console.MainLog.Instance.WriteLine(OpenSim.Framework.Console.LogPriority.HIGH, "World.cs: Backup() - Backup Failed with exception " + e.ToString());
                return false;
            }
            */
            return true;
        }
        #endregion

        #region Setup Methods
        /// <summary>
        /// Loads a new storage subsystem from a named library
        /// </summary>
        /// <param name="dllName">Storage Library</param>
        /// <returns>Successful or not</returns>
        public bool LoadStorageDLL(string dllName)
        {
            try
            {
                Assembly pluginAssembly = Assembly.LoadFrom(dllName);
                ILocalStorage store = null;

                foreach (Type pluginType in pluginAssembly.GetTypes())
                {
                    if (pluginType.IsPublic)
                    {
                        if (!pluginType.IsAbstract)
                        {
                            Type typeInterface = pluginType.GetInterface("ILocalStorage", true);

                            if (typeInterface != null)
                            {
                                ILocalStorage plug = (ILocalStorage)Activator.CreateInstance(pluginAssembly.GetType(pluginType.ToString()));
                                store = plug;

                                store.Initialise(this.m_datastore);
                                break;
                            }

                            typeInterface = null;
                        }
                    }
                }
                pluginAssembly = null;
                this.localStorage = store;
                return (store == null);
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: LoadStorageDLL() - Failed with exception " + e.ToString());
                return false;
            }
        }

        #endregion

        #region Regenerate Terrain

        /// <summary>
        /// Rebuilds the terrain using a procedural algorithm
        /// </summary>
        public void RegenerateTerrain()
        {
            try
            {
                Terrain.hills();

                lock (this.m_syncRoot)
                {
                    this.phyScene.SetTerrain(Terrain.getHeights1D());
                }
                this.localStorage.SaveMap(this.Terrain.getHeights1D());

                foreach (IClientAPI client in m_clientThreads.Values)
                {
                    this.SendLayerData(client);
                }

                foreach (libsecondlife.LLUUID UUID in Entities.Keys)
                {
                    Entities[UUID].LandRenegerated();
                }
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: RegenerateTerrain() - Failed with exception " + e.ToString());
            }
        }

        /// <summary>
        /// Rebuilds the terrain using a 2D float array
        /// </summary>
        /// <param name="newMap">256,256 float array containing heights</param>
        public void RegenerateTerrain(float[,] newMap)
        {
            try
            {
                this.Terrain.setHeights2D(newMap);
                lock (this.m_syncRoot)
                {
                    this.phyScene.SetTerrain(this.Terrain.getHeights1D());
                }
                this.localStorage.SaveMap(this.Terrain.getHeights1D());

                foreach (IClientAPI client in m_clientThreads.Values)
                {
                    this.SendLayerData(client);
                }

                foreach (libsecondlife.LLUUID UUID in Entities.Keys)
                {
                    Entities[UUID].LandRenegerated();
                }
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: RegenerateTerrain() - Failed with exception " + e.ToString());
            }
        }

        /// <summary>
        /// Rebuilds the terrain assuming changes occured at a specified point[?]
        /// </summary>
        /// <param name="changes">???</param>
        /// <param name="pointx">???</param>
        /// <param name="pointy">???</param>
        public void RegenerateTerrain(bool changes, int pointx, int pointy)
        {
            try
            {
                if (changes)
                {
                    /* Dont save here, rely on tainting system instead */

                    foreach (IClientAPI client in m_clientThreads.Values)
                    {
                        this.SendLayerData(pointx, pointy, client);
                    }
                }
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: RegenerateTerrain() - Failed with exception " + e.ToString());
            }
        }

        #endregion

        #region Load Terrain
        /// <summary>
        /// Loads the World heightmap
        /// </summary>
        /// 
        public override void LoadWorldMap()
        {
            try
            {
                float[] map = this.localStorage.LoadWorld();
                if (map == null)
                {
                    if (string.IsNullOrEmpty(this.m_regInfo.estateSettings.terrainFile))
                    {
                        Console.WriteLine("No default terrain, procedurally generating...");
                        this.Terrain.hills();

                        this.localStorage.SaveMap(this.Terrain.getHeights1D());
                    }
                    else
                    {
                        try
                        {
                            this.Terrain.loadFromFileF32(this.m_regInfo.estateSettings.terrainFile);
                            this.Terrain *= this.m_regInfo.estateSettings.terrainMultiplier;
                        }
                        catch
                        {
                            Console.WriteLine("Unable to load default terrain, procedurally generating instead...");
                            Terrain.hills();
                        }
                        this.localStorage.SaveMap(this.Terrain.getHeights1D());
                    }
                }
                else
                {
                    this.Terrain.setHeights1D(map);
                }

                CreateTerrainTexture();

            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: LoadWorldMap() - Failed with exception " + e.ToString());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void CreateTerrainTexture()
        {
            //create a texture asset of the terrain 
            byte[] data = this.Terrain.exportJpegImage("defaultstripe.png");
            this.m_regInfo.estateSettings.terrainImageID = LLUUID.Random();
            AssetBase asset = new AssetBase();
            asset.FullID = this.m_regInfo.estateSettings.terrainImageID;
            asset.Data = data;
            asset.Name = "terrainImage";
            asset.Type = 0;
            this.assetCache.AddAsset(asset);
        }
        #endregion

        #region Primitives Methods


        /// <summary>
        /// Loads the World's objects
        /// </summary>
        public void LoadPrimsFromStorage()
        {
            try
            {
                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs: LoadPrimsFromStorage() - Loading primitives");
                this.localStorage.LoadPrimitives(this);
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: LoadPrimsFromStorage() - Failed with exception " + e.ToString());
            }
        }

        /// <summary>
        /// Loads a specific object from storage
        /// </summary>
        /// <param name="prim">The object to load</param>
        public void PrimFromStorage(PrimData prim)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addPacket"></param>
        /// <param name="agentClient"></param>
        public void AddNewPrim(Packet addPacket, IClientAPI agentClient)
        {
            AddNewPrim((ObjectAddPacket)addPacket, agentClient.AgentId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addPacket"></param>
        /// <param name="ownerID"></param>
        public void AddNewPrim(ObjectAddPacket addPacket, LLUUID ownerID)
        {
            try
            {

               // MainLog.Instance.Notice("World.cs: AddNewPrim() - Creating new prim");

                Primitive prim = new Primitive(m_regionHandle, this, addPacket, ownerID, this._primCount);

                this.Entities.Add(prim.uuid, prim);
                this._primCount++;
            }
            catch (Exception e)
            {

               // MainLog.Instance.Warn("World.cs: AddNewPrim() - Failed with exception " + e.ToString());

            }
        }

        #endregion

        #region Add/Remove Avatar Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="remoteClient"></param          
        /// <param name="agentID"></param>
        /// <param name="child"></param>
        public override void AddNewClient(IClientAPI remoteClient, LLUUID agentID, bool child)
        {
            remoteClient.OnRegionHandShakeReply += this.SendLayerData;
            //remoteClient.OnRequestWearables += new GenericCall(this.GetInitialPrims);
            remoteClient.OnChatFromViewer += this.SimChat;
            remoteClient.OnRequestWearables += this.InformClientOfNeighbours;
            remoteClient.OnAddPrim += this.AddNewPrim;
            remoteClient.OnUpdatePrimPosition += this.UpdatePrimPosition;
            remoteClient.OnRequestMapBlocks += this.RequestMapBlocks;
            remoteClient.OnTeleportLocationRequest += this.RequestTeleportLocation;

            /* remoteClient.OnParcelPropertiesRequest += new ParcelPropertiesRequest(parcelManager.handleParcelPropertiesRequest);
            remoteClient.OnParcelDivideRequest += new ParcelDivideRequest(parcelManager.handleParcelDivideRequest);
            remoteClient.OnParcelJoinRequest += new ParcelJoinRequest(parcelManager.handleParcelJoinRequest);
            remoteClient.OnParcelPropertiesUpdateRequest += new ParcelPropertiesUpdateRequest(parcelManager.handleParcelPropertiesUpdateRequest);
            remoteClient.OnEstateOwnerMessage += new EstateOwnerMessageRequest(estateManager.handleEstateOwnerMessage);
            */

            ScenePresence newAvatar = null;
            try
            {

                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs:AddViewerAgent() - Creating new avatar for remote viewer agent");
                newAvatar = new ScenePresence(remoteClient, this, this.m_regInfo);
                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs:AddViewerAgent() - Adding new avatar to world");
                OpenSim.Framework.Console.MainLog.Instance.Verbose( "World.cs:AddViewerAgent() - Starting RegionHandshake ");

                //newAvatar.SendRegionHandshake();
                this.estateManager.sendRegionHandshake(remoteClient);

                PhysicsVector pVec = new PhysicsVector(newAvatar.Pos.X, newAvatar.Pos.Y, newAvatar.Pos.Z);
                lock (this.m_syncRoot)
                {
                    newAvatar.PhysActor = this.phyScene.AddAvatar(pVec);
                }

                lock (Entities)
                {
                    if (!Entities.ContainsKey(agentID))
                    {
                        this.Entities.Add(agentID, newAvatar);
                    }
                    else
                    {
                        Entities[agentID] = newAvatar;
                    }
                }
                lock (Avatars)
                {
                    if (Avatars.ContainsKey(agentID))
                    {
                        Avatars[agentID] = newAvatar;
                    }
                    else
                    {
                        this.Avatars.Add(agentID, newAvatar);
                    }
                }
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.Warn("World.cs: AddViewerAgent() - Failed with exception " + e.ToString());
            }
            return;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="agentID"></param>
        public override void RemoveClient(LLUUID agentID)
        {
            return;
        }
        #endregion

        #region Request Avatars List Methods
        //The idea is to have a group of method that return a list of avatars meeting some requirement
        // ie it could be all Avatars within a certain range of the calling prim/avatar. 

        /// <summary>
        /// Request a List of all Avatars in this World
        /// </summary>
        /// <returns></returns>
        public List<ScenePresence> RequestAvatarList()
        {
            List<ScenePresence> result = new List<ScenePresence>();

            foreach (ScenePresence avatar in Avatars.Values)
            {
                result.Add(avatar);
            }

            return result;
        }

        /// <summary>
        /// Request a filtered list of Avatars in this World
        /// </summary>
        /// <returns></returns>
        public List<ScenePresence> RequestAvatarList(FilterAvatarList filter)
        {
            List<ScenePresence> result = new List<ScenePresence>();

            foreach (ScenePresence avatar in Avatars.Values)
            {
                if (filter(avatar))
                {
                    result.Add(avatar);
                }
            }

            return result;
        }

        /// <summary>
        /// Request a Avatar by UUID
        /// </summary>
        /// <param name="avatarID"></param>
        /// <returns></returns>
        public ScenePresence RequestAvatar(LLUUID avatarID)
        {
            if (this.Avatars.ContainsKey(avatarID))
            {
                return Avatars[avatarID];
            }
            return null;
        }
        #endregion

        #region ShutDown
        /// <summary>
        /// Tidy before shutdown
        /// </summary>
        public override void Close()
        {
            try
            {
                this.localStorage.ShutDown();
            }
            catch (Exception e)
            {
                OpenSim.Framework.Console.MainLog.Instance.WriteLine(OpenSim.Framework.Console.LogPriority.HIGH, "World.cs: Close() - Failed with exception " + e.ToString());
            }
        }
        #endregion

        #region RegionCommsHost

        /// <summary>
        /// 
        /// </summary>
        public void RegisterRegionWithComms()
        {
            this.regionCommsHost = this.commsManager.GridServer.RegisterRegion(this.m_regInfo);
            if (this.regionCommsHost != null)
            {
                this.regionCommsHost.OnExpectUser += new ExpectUserDelegate(this.NewUserConnection);
                this.regionCommsHost.OnAvatarCrossingIntoRegion += new AgentCrossing(this.AgentCrossing);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="agent"></param>
        public void NewUserConnection(ulong regionHandle, AgentCircuitData agent)
        {
            // Console.WriteLine("World.cs - add new user connection");
            //should just check that its meant for this region 
            if (regionHandle == this.m_regInfo.RegionHandle)
            {
                if (agent.CapsPath != "")
                {
                    //Console.WriteLine("new user, so creating caps handler for it");
                    Caps cap = new Caps(this.assetCache, httpListener, this.m_regInfo.IPListenAddr, 9000, agent.CapsPath, agent.AgentID);
                    cap.RegisterHandlers();
                    this.capsHandlers.Add(cap);
                }
                this.authenticateHandler.AddNewCircuit(agent.circuitcode, agent);
            }
        }

        public void AgentCrossing(ulong regionHandle, libsecondlife.LLUUID agentID, libsecondlife.LLVector3 position)
        {
            if (regionHandle == this.m_regInfo.RegionHandle)
            {
                if (this.Avatars.ContainsKey(agentID))
                {
                    this.Avatars[agentID].MakeAvatar(position);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void InformClientOfNeighbours(IClientAPI remoteClient)
        {
            // Console.WriteLine("informing client of neighbouring regions");
            List<RegionInfo> neighbours = this.commsManager.GridServer.RequestNeighbours(this.m_regInfo);

            //Console.WriteLine("we have " + neighbours.Count + " neighbouring regions");
            if (neighbours != null)
            {
                for (int i = 0; i < neighbours.Count; i++)
                {
                    // Console.WriteLine("sending neighbours data");
                    AgentCircuitData agent = remoteClient.RequestClientInfo();
                    agent.BaseFolder = LLUUID.Zero;
                    agent.InventoryFolder = LLUUID.Zero;
                    agent.startpos = new LLVector3(128, 128, 70);
                    agent.child = true;
                    this.commsManager.InterRegion.InformRegionOfChildAgent(neighbours[i].RegionHandle, agent);
                    remoteClient.InformClientOfNeighbour(neighbours[i].RegionHandle, System.Net.IPAddress.Parse(neighbours[i].IPListenAddr), (ushort)neighbours[i].IPListenPort);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <returns></returns>
        public RegionInfo RequestNeighbouringRegionInfo(ulong regionHandle)
        {
            return this.commsManager.GridServer.RequestNeighbourInfo(regionHandle);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="minX"></param>
        /// <param name="minY"></param>
        /// <param name="maxX"></param>
        /// <param name="maxY"></param>
        public void RequestMapBlocks(IClientAPI remoteClient, int minX, int minY, int maxX, int maxY)
        {
            List<MapBlockData> mapBlocks;
            mapBlocks = this.commsManager.GridServer.RequestNeighbourMapBlocks(minX, minY, maxX, maxY);
            remoteClient.SendMapBlock(mapBlocks);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="RegionHandle"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="flags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, ulong regionHandle, LLVector3 position, LLVector3 lookAt, uint flags)
        {
            if (regionHandle == this.m_regionHandle)
            {
                if (this.Avatars.ContainsKey(remoteClient.AgentId))
                {
                    remoteClient.SendTeleportLocationStart();
                    remoteClient.SendLocalTeleport(position, lookAt, flags);
                    this.Avatars[remoteClient.AgentId].Teleport(position);
                }
            }
            else
            {
                RegionInfo reg = this.RequestNeighbouringRegionInfo(regionHandle);
                if (reg != null)
                {
                    remoteClient.SendTeleportLocationStart();
                    AgentCircuitData agent = remoteClient.RequestClientInfo();
                    agent.BaseFolder = LLUUID.Zero;
                    agent.InventoryFolder = LLUUID.Zero;
                    agent.startpos = new LLVector3(128, 128, 70);
                    agent.child = true;
                    this.commsManager.InterRegion.InformRegionOfChildAgent(regionHandle, agent);
                    this.commsManager.InterRegion.ExpectAvatarCrossing(regionHandle, remoteClient.AgentId, position);
                    remoteClient.SendRegionTeleport(regionHandle, 13, reg.IPListenAddr, (ushort)reg.IPListenPort, 4, (1 << 4));               
                }
                //remoteClient.SendTeleportCancel();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="regionhandle"></param>
        /// <param name="agentID"></param>
        /// <param name="position"></param>
        public bool InformNeighbourOfCrossing(ulong regionhandle, LLUUID agentID, LLVector3 position)
        {
           return this.commsManager.InterRegion.ExpectAvatarCrossing(regionhandle, agentID, position);
        }

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="px"></param>
        /// <param name="py"></param>
        /// <param name="RemoteClient"></param>
        public override void SendLayerData(int px, int py, IClientAPI RemoteClient)
        {
            RemoteClient.SendLayerData(px, py, Terrain.getHeights1D());
        }
    }
}
