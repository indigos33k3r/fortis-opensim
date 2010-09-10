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

using System.Collections.Generic;
using System.Xml;
using OpenMetaverse;

namespace OpenSim.Region.Framework.Scenes.Animation
{
    public static class AvatarAnimations
    {
        public static readonly Dictionary<UUID, string> AnimStateNames = new Dictionary<UUID, string>
        {
            { new UUID("201f3fdf-cb1f-dbec-201f-7333e328ae7c"), "Crouching" },
            { new UUID("47f5f6fb-22e5-ae44-f871-73aaaf4a6022"), "CrouchWalking" },
            { new UUID("666307d9-a860-572d-6fd4-c3ab8865c094"), "Falling" },
            { new UUID("f5fc7433-043d-e819-8298-f519a119b688"), "Walking" },
            { new UUID("aec4610c-757f-bc4e-c092-c6e9caf18daf"), "Flying" },
            { new UUID("2b5a38b2-5e00-3a97-a495-4c826bc443e6"), "FlyingSlow" },
            { new UUID("20f063ea-8306-2562-0b07-5c853b37b31e"), "Hovering Down" },
            { new UUID("62c5de58-cb33-5743-3d07-9e4cd4352864"), "Hovering Up" },
            { new UUID("2305bd75-1ca9-b03b-1faa-b176b8a8c49e"), "Jumping" },
            { new UUID("7a17b059-12b2-41b1-570a-186368b6aa6f"), "Landing" },
            { new UUID("7a4e87fe-de39-6fcb-6223-024b00893244"), "PreJumping" },
            { new UUID("05ddbff8-aaa9-92a1-2b74-8fe77a29b445"), "Running" },
            { new UUID("1a5fe8ac-a804-8a5d-7cbd-56bd83184568"), "Sitting" },
            { new UUID("b1709c8d-ecd3-54a1-4f28-d55ac0840782"), "Sitting" },
            { new UUID("245f3c54-f1c0-bf2e-811f-46d8eeb386e7"), "Sitting" },
            { new UUID("1c7600d6-661f-b87b-efe2-d7421eb93c86"), "Sitting on Ground" },
            { new UUID("1a2bd58e-87ff-0df8-0b4c-53e047b0bb6e"), "Sitting on Ground" },
            { new UUID("f4f00d6e-b9fe-9292-f4cb-0ae06ea58d57"), "Soft Landing" },
            { new UUID("2408fe9e-df1d-1d7d-f4ff-1384fa7b350f"), "Standing" },
            { new UUID("15468e00-3400-bb66-cecc-646d7c14458e"), "Standing" },
            { new UUID("370f3a20-6ca6-9971-848c-9a01bc42ae3c"), "Standing" },
            { new UUID("42b46214-4b44-79ae-deb8-0df61424ff4b"), "Standing" },
            { new UUID("f22fed8b-a5ed-2c93-64d5-bdd8b93c889f"), "Standing" },
            { new UUID("3da1d753-028a-5446-24f3-9c9b856d9422"), "Standing Up" },
            { new UUID("1cb562b0-ba21-2202-efb3-30f82cdf9595"), "Striding" },
            { new UUID("56e0ba0d-4a9f-7f27-6117-32f2ebbf6135"), "Turning Left" },
            { new UUID("2d6daa51-3192-6794-8e2e-a15f8338ec30"), "Turning Right" },
            { new UUID("6ed24bd8-91aa-4b12-ccc7-c97c857ab4e0"), "Walking" },
        };
        public static readonly Dictionary<string, UUID> AnimsUUID;
        public static readonly Dictionary<UUID, string> AnimsNames;

        static AvatarAnimations()
        {
            AnimsNames = Animations.ToDictionary();
            foreach (KeyValuePair<UUID, string> anim in AnimsNames)
                AnimsUUID[anim.Value] = anim.Key;
        }
    }
}
