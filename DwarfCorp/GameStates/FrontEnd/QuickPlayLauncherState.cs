using System;
using System.Collections.Generic;
using System.Linq;
using DwarfCorp.GameStates;

namespace DwarfCorp.GameStates
{
    /// <summary>
    /// Auto-launches a default world and drops into PlayState. Used when the env var
    /// DWARFCORP_QUICKPLAY=1 is set — replaces the full Intro → MainMenu → Worldgen flow so
    /// run-quick.ps1 can smoke-test the PlayState path end-to-end without manual clicks.
    ///
    /// Replicates the QUICKPLAY button handler in MainMenuState: creates a default Overworld
    /// with a random seed, fills biome + embarkment with defaults, then pushes a LoadState
    /// which asynchronously builds the world and transitions into PlayState on its own.
    /// </summary>
    public class QuickPlayLauncherState : GameState
    {
        private int _framesWaited;
        private bool _launched;

        public QuickPlayLauncherState(DwarfGame game) : base(game)
        {
        }

        public override void OnEnter()
        {
            CrashBreadcrumbs.Push("QuickPlayLauncherState.OnEnter");
            IsInitialized = true;
            base.OnEnter();
        }

        public override void Update(DwarfTime gameTime)
        {
            if (_launched) return;
            // Wait a couple of frames so the GameStateManager / LoadContent has fully
            // initialized the GUI root and ready the graphics pipeline.
            if (_framesWaited++ < 3) return;

            try
            {
                _launched = true;
                CrashBreadcrumbs.Push("QuickPlayLauncherState: creating default overworld");

                var overworld = Overworld.Create();
                overworld.InstanceSettings.InitalEmbarkment = new Embarkment(overworld);

                var biomes = new List<BiomeData>();
                for (var x = 0; x < 4; ++x)
                    biomes.Add(Library.EnumerateBiomes().Where(b => !b.Underground).SelectRandom());
                overworld.InstanceSettings.SelectedBiomes = biomes.Distinct().ToList();

                foreach (var loadout in Library.EnumerateLoadouts())
                    overworld.InstanceSettings.InitalEmbarkment.Employees.Add(
                        Applicant.Random(loadout, overworld.Company));

                CrashBreadcrumbs.Push("QuickPlayLauncherState: pushing LoadState (GenerateOverworld)");
                GameStateManager.PushState(new LoadState(Game, overworld, LoadTypes.GenerateOverworld));
                GameStateManager.PopState(false); // Remove this launcher — LoadState is now on top.
            }
            catch (Exception e)
            {
                CrashBreadcrumbs.Push("QuickPlayLauncherState FAILED: " + e.GetType().Name + " — " + e.Message);
                Console.Error.WriteLine("[QuickPlayLauncherState] " + e);
                throw;
            }
        }

        public override void Render(DwarfTime gameTime)
        {
        }
    }
}
