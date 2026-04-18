using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Threading;

namespace DwarfCorp.GameStates
{
    public class IntroState : GameState
    {
        private Texture2D Logo;
        private Timer IntroTimer = new Timer(1, true);
        private bool LoadFinished = false;
        private Thread LoadThread;

        public IntroState(DwarfGame game) :
            base(game)
        {
        }

        public override void OnEnter()
        {
            IsInitialized = true;
            Logo = AssetManager.GetContentTexture(ContentPaths.Logos.companylogo);
            IntroTimer.Reset(3);

            LoadThread = new Thread(LibraryLoadThread);
            LoadThread.Start();
            base.OnEnter();
        }

        public override void Update(DwarfTime gameTime)
        {
            Game.IsMouseVisible = false;
            IntroTimer.Update(gameTime);

            if (IntroTimer.HasTriggered && LoadFinished)//|| Keyboard.GetState().GetPressedKeys().Length > 0)
            {
                GameStateManager.PopState();
                var version = Program.Version;
                if (GameSettings.Current.LastVersionChangesDisplayed != version)
                    GameStateManager.PushState(new ChangeLogState(Game));
                else
                    GameStateManager.PushState(new MainMenuState(Game));
            }

            base.Update(gameTime);
        }

        public override void Render(DwarfTime gameTime)
        {
            DwarfGame.SafeSpriteBatchBegin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.Default, RasterizerState.CullNone, null, Matrix.Identity);
            // Center the company logo vertically as well — previously it sat above the holy-donut logo.
            Vector2 screenCenter = new Vector2(Game.GraphicsDevice.Viewport.Width / 2 - Logo.Width / 2, Game.GraphicsDevice.Viewport.Height / 2 - Logo.Height / 2);
            DwarfGame.SpriteBatch.Draw(Logo, screenCenter, null, new Color(1f, 1f, 1f));

            DwarfGame.SpriteBatch.End();

            base.Render(gameTime);
        }

        private void LibraryLoadThread()
        {
            Library.EnumerateBiomes();
            Library.EnumerateClasses();
            Library.EnumerateDecalTypes();
            Library.EnumerateDifficulties();
            Library.EnumerateCategoryIcons();
            Library.EnumerateEquipmentSlotTypes();
            Library.EnumerateGrassTypes();
            Library.EnumerateLoadouts();
            Library.EnumerateRailChainPatterns();
            Library.EnumerateRailPatterns();
            Library.EnumerateRailPieces();
            Library.EnumerateResourceTypes();
            Library.EnumerateSpecies();
            Library.EnumerateVoxelTypes();
            Library.EnumerateZoneTypes();
            Library.EnumerateLiquids();
            LoadFinished = true;
        }
    }
}