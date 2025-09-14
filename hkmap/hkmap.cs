using System;
using Modding;
using UnityEngine;
using MapChanger;

namespace hkmap
{
    public class HkmapMod : Mod
    {
        public override string GetVersion() => GetType().Assembly.GetName().Version.ToString();

        public HkmapMod() : base("hkmap")
        {
        }

        public override void Initialize()
        {
            Log("Initializing");

            // Spawn persistent minimap overlay
            try
            {
                var go = new GameObject("Hkmap.MinimapOverlay");
                GameObject.DontDestroyOnLoad(go);
                go.AddComponent<Overlay>();

                // Ensure MapChanger has at least one mode so it initializes BuiltInObjects on enter game
                try
                {
                    Events.OnEnterGame += () =>
                    {
                        ModeManager.AddModes([new MapMode()]);
                    };
                }
                catch (Exception ex)
                {
                    Log($"Failed to register MapChanger mode: {ex}");
                    throw;
                }
            }
            catch (Exception e)
            {
                Log($"Failed to create Hkmap.MinimapOverlay: {e}");
            }

            Log("Initialized");
        }
    }
}
