using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DwarfCorp.GameStates;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DwarfCorp.Gui.Widgets;

namespace DwarfCorp
{
    public class HandledException: Exception
    {
        public HandledException(Exception e) : base("Exception handled, user aborting", e)
        { }
    }

    public class DwarfGame : Game
    {
        /// <summary>
        /// Global lock serializing any code that touches the GraphicsDevice or allocates/updates
        /// GPU resources (Texture2D, VertexBuffer, IndexBuffer, RenderTarget2D, SetData calls).
        ///
        /// ORIGINAL REASON (FNA 26 Vulkan, pre-M.1): VkCommandPool is not thread-safe and FNA 26
        /// didn't auto-marshal calls to the main thread like FNA 19's OpenGL backend did. Without
        /// this lock, RebuildVoxelsThread raced vkCmdBindVertexBuffers on the render thread and
        /// corrupted the heap on AMD drivers (STATUS_HEAP_CORRUPTION).
        ///
        /// CURRENT REASON (MonoGame 3.8 DX11, post-M.1): the specific Vulkan failure mode is gone,
        /// but `ID3D11DeviceContext` (the immediate context MonoGame dispatches through) is still
        /// not thread-safe per Microsoft's contract. Resource creation (`new Texture2D`, `new
        /// VertexBuffer`) is thread-safe on `ID3D11Device`, but `SetData` routes through the
        /// context and racing it with the render thread is undefined behaviour — silent
        /// corruption in the best case, a hard crash in the worst. Keeping the lock costs ~50 ns
        /// when uncontended (the common path, since the main thread holds it for the whole
        /// Draw()) and is cheap insurance against the exact class of bug that bit us on Vulkan.
        ///
        /// Take-hold pattern (unchanged): wrap every `new Texture2D/VertexBuffer/IndexBuffer/
        /// RenderTarget2D` plus any `SetData` coming from a non-render-loop context. The main
        /// render frame takes the lock once per Draw() and holds it for the frame.
        ///
        /// WHEN TO RETIRE: Fase B.1 refactor (split mesh-gen from GPU upload — workers generate
        /// mesh data on CPU, a single thread consumes a queue and does all GPU writes). After
        /// that lands, every `new XxxBuffer` and `SetData` sits on the main thread again, and
        /// this lock becomes dead code. Tracked as TODO_LIST item 29.
        /// </summary>
        public static readonly object GpuLock = new object();

        public GraphicsDeviceManager Graphics;
        public static SpriteBatch SpriteBatch { get; set; }
        public Terrain2D ScreenSaver { get; set; }

        public static Gui.Input.GumInputMapper GumInputMapper;
        public static Gui.Input.Input GumInput;
        public static Gui.RenderData GuiSkin;

        private static Gui.Root ConsoleGui;
        private static bool ConsoleVisible = false;
        public static bool IsConsoleVisible { get { return ConsoleVisible; } }
        public static Gui.Widget ConsolePanel { get { return ConsoleGui.RootItem.GetChild(0); } }

        public const string GameName = "DwarfCorp";
        public static bool HasRendered = false;
        private static LogWriter _logwriter;
        private static TextWriter _initialOut;
        private static TextWriter _initialError;

        private static int MainThreadID;

        public class LazyAction
        {
            public Action Action;
            public Func<bool> Result;
        }

        private List<LazyAction> _lazyActions = new List<LazyAction>();
        private object _actionMutex = new object();
        private System.Threading.Timer _autoExitTimer;

        public DwarfGame()
        {
            GameState.Game = this;
            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Window.Title = "DwarfCorp";
            Window.AllowUserResizing = false;
            MainThreadID = Thread.CurrentThread.ManagedThreadId;
            GameSettings.Load();

            // Check GUI scale - if the settings are bad, fix.
            if (GameSettings.Current.GuiScale * 480 > GameSettings.Current.ResolutionY)
                GameSettings.Current.GuiScale = 1;

            Graphics.IsFullScreen = GameSettings.Current.Fullscreen;
            Graphics.PreferredBackBufferWidth = GameSettings.Current.Fullscreen ? GameSettings.Current.ResolutionX : Math.Min(GameSettings.Current.ResolutionX, GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width);
            Graphics.PreferredBackBufferHeight = GameSettings.Current.Fullscreen ? GameSettings.Current.ResolutionY : Math.Min(GameSettings.Current.ResolutionY,
                GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height);
            Graphics.SynchronizeWithVerticalRetrace = GameSettings.Current.VSync;
            MathFunctions.Random = new ThreadSafeRandom(new Random().Next());
            Graphics.PreparingDeviceSettings += WorldRenderer.GraphicsPreparingDeviceSettings;
            Graphics.PreferMultiSampling = false;

            // Optional batch-mode auto-exit. Set DWARFCORP_AUTOEXIT_SECONDS=N in the environment
            // to force the process to quit N seconds after launch — used by run-quick.ps1 so I
            // can smoke-test the build end-to-end and read logs without having to close the
            // window manually. Only activates when the env var is set and parses to > 0.
            var autoExit = Environment.GetEnvironmentVariable("DWARFCORP_AUTOEXIT_SECONDS");
            if (!string.IsNullOrEmpty(autoExit) && int.TryParse(autoExit, out var seconds) && seconds > 0)
            {
                _autoExitTimer = new System.Threading.Timer(_ =>
                {
                    Console.Out.WriteLine("[DWARFCORP_AUTOEXIT_SECONDS] " + seconds + "s elapsed — calling Exit()");
                    try { this.Exit(); }
                    catch (Exception e) { Console.Error.WriteLine("AutoExit Exit() threw: " + e); }
                }, null, seconds * 1000, System.Threading.Timeout.Infinite);
            }
            // Don't call ApplyChanges() here — FNA 26 emits "Forcing CreateDevice! Avoid
            // calling ApplyChanges before Game.Run!" and creates the GraphicsDevice twice
            // (once here, once during FNA's Run()), which orphans every texture/buffer
            // allocated against the first device. All the properties set above are applied
            // automatically when FNA creates the device inside Run(). WorldManager.StartLoad()
            // calls ApplyChanges() once later — that's the only one we need.

            // Todo: Restore steam functionality
            //if (AssetManagement.Steam.Steam.InitializeSteam() == AssetManagement.Steam.Steam.SteamInitializationResult.QuitImmediately)
            //    Exit();
        }

        public static string GetGameDirectory()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), GameName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string osConfigDir = Environment.GetEnvironmentVariable("HOME");
                if (String.IsNullOrEmpty(osConfigDir))
                {
                    return "."; // Oh well.
                }
                osConfigDir += "/Library/Application Support";
                return Path.Combine(osConfigDir, GameName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string osConfigDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (String.IsNullOrEmpty(osConfigDir))
                {
                    osConfigDir = Environment.GetEnvironmentVariable("HOME");
                    if (String.IsNullOrEmpty(osConfigDir))
                    {
                        return "."; // Oh well.
                    }
                    osConfigDir += "/.local/share";
                }
                return Path.Combine(osConfigDir, GameName);
            }
            throw new Exception("Unhandled platform: " + RuntimeInformation.OSDescription);
        }


        public static string GetSaveDirectory()
        {
            if (String.IsNullOrEmpty(GameSettings.Current.SaveLocation))
                return DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar + "Saves";
            else
                return GameSettings.Current.SaveLocation + Path.DirectorySeparatorChar + "Saves";
        }

        public static string GetWorldDirectory()
        {
            if (String.IsNullOrEmpty(GameSettings.Current.SaveLocation))
                return DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar + "Worlds";
            else
                return GameSettings.Current.SaveLocation + Path.DirectorySeparatorChar + "Worlds";
        }


        public void DoLazyAction(Action action, Func<bool> callback = null)
        {
            lock (_actionMutex)
            {
                _lazyActions.Add(new LazyAction() { Action = action, Result = callback });
            }
        }

        public static void InitializeLogger()
        {
            try
            {
                Trace.Listeners.Clear();
                var dir = GetGameDirectory();
                if (!global::System.IO.Directory.Exists(dir))
                {
                    global::System.IO.Directory.CreateDirectory(dir);
                }
                var path = ProgramData.CreatePath(dir, "log.txt");
                if (!File.Exists(path))
                {
                    File.Create(path).Close();
                }

                var logFile = new FileInfo(path);
                if (logFile.Length > 5e7)
                {
                    Console.Out.WriteLine("Log file at {0} was too large ({1} bytes). Clearing it.", path, logFile.Length);
                    global::System.IO.File.WriteAllText(path, string.Empty);
                }
                FileStream writerOutput = new FileStream(path, FileMode.Append, FileAccess.Write);
                _logwriter = new LogWriter(Console.Out, writerOutput);
                _initialOut = Console.Out;
                _initialError = Console.Error;
                Console.SetOut(_logwriter);
                Console.SetError(_logwriter);
                Console.Out.WriteLine("Game started at " + DateTime.Now.ToShortDateString() + " : " + DateTime.Now.ToShortTimeString());
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Failed to initialize logger: {0}", exception.ToString());
            }
        }

        // Todo: Kill passthrough
        public static void LogSentryBreadcrumb(string category, string message, BreadcrumbLevel level = BreadcrumbLevel.Info)
        {
            Program.LogSentryBreadcrumb(category, message, level);
        }

        protected override void Initialize()
        {
            var dir = GetGameDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            InitializeLogger();
            Thread.CurrentThread.Name = "Main";
            SpriteBatch = new SpriteBatch(GraphicsDevice);

            // L.1: ImGui debug overlay. Needs a live GraphicsDevice, so it goes
            // here rather than in the constructor. Toggled with F12 at runtime.
            Gui.Debug.ImGuiService.Initialize(this);

            base.Initialize();
        }

        protected override void LoadContent()
        {
            LogSentryBreadcrumb("Loading", "LoadContent was called.", BreadcrumbLevel.Info);
            CrashBreadcrumbs.Push("DwarfGame.LoadContent enter");
            AssetManager.Initialize(Content, GraphicsDevice, GameSettings.Current);

            //DwarfSprites.LayerLibrary.ConvertTestPSD();


            // Prepare GemGui
            if (GumInputMapper == null)
            {
                GumInputMapper = new Gui.Input.GumInputMapper(Window);
                GumInput = new Gui.Input.Input(GumInputMapper);
            }

            GuiSkin = new RenderData(GraphicsDevice, Content);

            // Create console.
            ConsoleGui = new Gui.Root(GuiSkin);
            ConsoleGui.SetMetrics = false;
            ConsoleGui.RootItem.AddChild(new Gui.Widgets.StaticGridPanel
            {
                Rows = 4,
                Columns = 4,
                AutoLayout = AutoLayout.DockFill,
                Panels = new List<Gui.Widgets.StaticGridPanel.Panel>
                {
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "LOG",
                        X = 0,
                        Y = 0,
                        RowSpan = 2,
                        ColSpan = 2
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "COMMAND",
                        X = 2,
                        Y = 0,
                        RowSpan = 2,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "STATS",
                        X = 0,
                        Y = 2,
                        RowSpan = 2,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "PERFORMANCE",
                        X = 1,
                        Y = 2,
                        RowSpan = 2,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "FPS",
                        X = 2,
                        Y = 2,
                        RowSpan = 2,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "FORECAST",
                        X = 3,
                        Y = 0,
                        RowSpan = 1,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "MODULES",
                        X = 3,
                        Y = 1,
                        RowSpan = 1,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "STEAM",
                        X = 3,
                        Y = 2,
                        RowSpan = 1,
                        ColSpan = 1
                    },
                    new Gui.Widgets.StaticGridPanel.Panel
                    {
                        Name = "VERSION",
                        X = 3,
                        Y = 3,
                        RowSpan = 1,
                        ColSpan = 1
                    }

                }
            });

            ConsoleGui.RootItem.Layout();
            if (_logwriter != null)
                _logwriter.SetConsole(GetConsoleTile("LOG"));

            Console.Out.WriteLine("Console created.");

            if (SoundManager.Content == null)
            {
                SoundManager.Content = Content;
                SoundManager.LoadMixerSettings();
            }

            if (GameStateManager.StateStackIsEmpty)
            {
                LogSentryBreadcrumb("GameState", "There was nothing in the state stack. Starting over.");

                // DWARFCORP_QUICKPLAY=1 bypasses the intro + main menu, drops straight into a
                // generated world. Used by run-quick.ps1 so I can smoke-test the PlayState
                // path end-to-end without manual clicks. Skips ChangeLogState by marking the
                // current version as already-displayed.
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DWARFCORP_QUICKPLAY")))
                {
                    CrashBreadcrumbs.Push("DWARFCORP_QUICKPLAY set — bypassing IntroState");
                    GameSettings.Current.LastVersionChangesDisplayed = Program.Version;
                    GameStateManager.PushState(new GameStates.QuickPlayLauncherState(this));
                }
                else
                {
                    GameStateManager.PushState(new IntroState(this));
                }
            }

            ControlSettings.Load();
            Drawer2D.Initialize(Content, GraphicsDevice);
            ScreenSaver = new Terrain2D(this);

            base.LoadContent();

            var ver = GetConsoleTile("VERSION");
            ver.Lines.Clear();
            ver.Lines.Add("VERSION");
            ver.Lines.Add(Program.Version);
            ver.Lines.Add(Program.Commit);
        }

        public static void RebuildConsole()
        {
            ConsoleGui.RootItem.Layout();
        }

        protected override void Update(GameTime time)
        {
            if (!IsActive)
            {
                base.Update(time);
                return;
            }

#if !DEBUG
            try
            {
#endif
                if (GumInputMapper.WasConsoleTogglePressed())
                {
                    ConsoleVisible = !ConsoleVisible;

                    if (ConsoleVisible)
                    {
                        var commandPanel = GetConsoleTile("COMMAND");
                        commandPanel.AddCommandEntry();
                        ConsoleGui.SetFocus(commandPanel.Children[0]);
                    }
                }

                if (ConsoleVisible)
                {
                    ConsoleGui.Update(time);
                    if (ConsoleGui.FocusItem != null)
                        DwarfGame.GumInput.FireKeyboardActionsOnly(ConsoleGui);
                }

            Debugger.ClearConsoleCommandContext();


            PerformanceMonitor.BeginFrame();
                PerformanceMonitor.PushFrame("Update");
                //AssetManagement.Steam.Steam.Update(); // Todo: Restore Steam functionality
                DwarfTime.LastTimeX.Update(time);
                GameStateManager.Update(DwarfTime.LastTimeX);

                lock (_actionMutex)
                {
                    foreach (var action in _lazyActions)
                    {
                        action.Action();
                        action.Result?.Invoke();
                    }
                    _lazyActions.Clear();
                }

                base.Update(time);
                PerformanceMonitor.PopFrame();
#if !DEBUG
            }
            catch (HandledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Program.CaptureException(exception);
                if (Program.ShowErrorDialog(exception.Message))
                    throw new HandledException(exception);
            }
#endif
            HasRendered = false;
        }

        protected override void Draw(GameTime time)
        {
            if (GraphicsDevice.IsDisposed) return;

            HasRendered = true;

            // Hold the global GPU lock for the entire frame render. Any background thread that
            // needs to touch the GraphicsDevice (chunk rebuild, resource texture create, water
            // geometry upload) blocks here until the frame finishes. Without this, FNA 26 with
            // the AMD Vulkan backend races on the VkCommandPool and corrupts the heap.
            lock (GpuLock)
            {
#if !DEBUG
                try
                {
#endif
                    PerformanceMonitor.PushFrame("Render");

                    GraphicsDevice.Clear(Color.Black);

                    if (GameStateManager.DrawScreensaver)
                        ScreenSaver.Render(GraphicsDevice, DwarfTime.LastTimeX);

                    GameStateManager.Render(DwarfTime.LastTimeX);

                    GraphicsDevice.SetRenderTarget(null);
                    base.Draw(time);
                    PerformanceMonitor.PopFrame();
                    PerformanceMonitor.Render();

                    if (ConsoleVisible)
                        ConsoleGui.Draw();

                    // L.1: ImGui overlay always last so it sits on top of everything else.
                    // Hidden by default; F12 toggles visibility at runtime.
                    Gui.Debug.ImGuiService.Render(time);

#if !DEBUG
                }
                catch (Exception exception)
                {
                    Program.CaptureException(exception);
                    if (Program.ShowErrorDialog(exception.Message))
                        throw new HandledException(exception);
                }
#endif
            }
        }

        public static void SafeSpriteBatchBegin(SpriteSortMode sortMode, BlendState blendState, SamplerState samplerstate, 
            DepthStencilState depthState, RasterizerState rasterState, Effect effect, Matrix world)
        {
            Debug.Assert(IsMainThread);
            if (SpriteBatch.GraphicsDevice.IsDisposed || SpriteBatch.IsDisposed)
            {
                SpriteBatch = new SpriteBatch(GameState.Game.GraphicsDevice);
            }

            try
            {
                SpriteBatch.Begin(sortMode,
                    blendState,
                    samplerstate,
                    depthState,
                    rasterState,
                    effect,
                    world);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.Write(exception);
                SpriteBatch.Dispose();
                SpriteBatch = new SpriteBatch(GameState.Game.GraphicsDevice);
                SpriteBatch.Begin(sortMode,
                    blendState,
                    samplerstate,
                    depthState,
                    rasterState,
                    effect,
                    world);
            }
        }

        protected override void OnExiting(object sender, ExitingEventArgs args)
        {
            if (_initialOut != null)
                Console.SetOut(_initialOut);
            if (_initialError != null)
                Console.SetError(_initialError);
            if (_logwriter != null)
                _logwriter.Dispose();
            ExitGame = true;
            Program.SignalShutdown();
            base.OnExiting(sender, args);
        }

        // If called in the non main thread, will return false;
        public static bool IsMainThread
        {
            get { return Thread.CurrentThread.ManagedThreadId == MainThreadID; }
        }

        public static bool ExitGame = false;
        
        public static Gui.Widgets.DwarfConsole GetConsoleTile(String Name)
        {
            var display = DwarfGame.ConsolePanel.EnumerateChildren().Where(c =>
            {
                if (c.Tag is String tag) return tag == Name;
                return false;
            }).FirstOrDefault() as Gui.Widgets.DwarfConsole;

            if (display == null)
            {
                display = DwarfGame.ConsolePanel.AddChild(new Gui.Widgets.DwarfConsole
                {
                    Tag = Name
                }) as Gui.Widgets.DwarfConsole;

                DwarfGame.RebuildConsole();
            }

            return display;
        }
    }
}
