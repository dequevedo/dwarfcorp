# TODO List

> **Escopo:** features menores, bug fixes, UX, balanceamento. Um item = um pedaço de trabalho isolado.
>
> **Migração grande (FNA → MonoGame + stack composável moderno) NÃO mora aqui** — ela é o projeto principal e tem tracker próprio em [`PERF_PLAN_EXTREME.md`](PERF_PLAN_EXTREME.md). Fases M/B/C/D/L/E/F/G e progresso ficam todos lá.

## Concluído

- [x] 1. Mover a câmera mais rápido ao pressionar Shift.
  OrbitCamera.cs: Shift aplica multiplicador de 3× em WASD/setas.

- [x] 2. Mover o minimap para o canto superior direito.
  PlayState-GUI.cs: AutoLayout do MinimapFrame trocado para FloatTopRight.

- [x] 3. Remover o logo do Holy Donut da tela de load.
  IntroState.cs: sprite HoleyDonutLogo removido, logo principal centralizado.

- [x] 5. Profiler com exportação para CSV.
  PerformanceMonitor.cs ganhou ring-buffer de histórico, snapshot thread-safe e ExportCsv. Novo ProfilerPanel.cs com dropdown de range e botão Export CSV. F11 abre/fecha o painel.

- [x] 6. Remover o botão Map da toolbar e tornar o minimap colapsável.
  MinimapFrame.cs: botão −/+ substitui o X. MinimapIcon removido da IconTray. Hotkey de mapa chama ToggleCollapsed().

- [x] 7. Remover o painel de madeira do HUD inferior.
  PlayState-GUI.cs: BottomBackground virou Widget transparente.

- [x] 4 + 8. Melhorar FPS ao mover a câmera.
  ChunkRenderer.cs: early-out por matriz view×projection quando câmera está parada, HashSet/List reutilizados como scratch, PushFrame/PopFrame para medir no profiler. Água já estava em thread de background, não era o gargalo.

- [x] 24. Crash do MojoShader em RenderPatternIcons/RenderVoxelIcons (mitigação).
  Shader.cs ganhou `TryGetSharedIconShader` com cache + lock + try/catch (inclui `[HandleProcessCorruptedStateExceptions]` pra AV em .NET Framework) e breadcrumbs. RailLibrary/VoxelLibrary reusam o shader compartilhado em vez de clonar toda vez; em caso de falha retornam placeholder transparente. SpriteAtlas endurecido pra capturar exceção do generator e usar textura fallback. Fix real é subir o FNA (Fase 0.2 do plano de performance) — isso é stopgap.

- [x] Fase 0.4 do plano de performance: remover SharpRaven (deprecated).
  Enum `BreadcrumbLevel` próprio em CrashBreadcrumbs.cs; `LogSentryBreadcrumb` agora escreve também no ring-buffer do CrashBreadcrumbs (antes só no Console). Referências a `SharpRaven.Data.BreadcrumbLevel` trocadas em 6 arquivos. Reference removida do csproj, entrada tirada do packages.config. Dependência morta há anos, corta 1 lib.

- [ ] Fase 1.1 do plano de performance: paralelizar chunk rebuild — **REVERTIDO**, instável.
  Tentativa inicial usava `Parallel.ForEach` em `ChunkManager.RebuildVoxelsThread`, mas causou HEAP CORRUPTION (0xC0000374) ao entrar no PlayState. `chunk.Rebuild()` toca `GraphicsDevice` (aloca vertex/index buffers via FNA3D) e disposa a primitive anterior sob mutex — rodar N em paralelo gerou race nativo no driver Vulkan da AMD. Caminho correto é separar mesh-gen (CPU, paraleliza) de GPU upload (thread única) — requer refactor no `GeometricPrimitive` / `VoxelChunk.Rebuild`. Ticket para sessão dedicada.

- [x] Fase 1.2 do plano de performance: A* data structures pooled per pathing thread.
  AStarPlanner.cs: HashSet<MoveState> closedSet/openSet, Dictionary<MoveState,MoveAction> cameFrom, Dictionary<MoveState,float> gScore, PriorityQueue<MoveState> fScore, MoveActionTempStorage, List<GameComponent> playerObjects/teleportObjects viraram **[ThreadStatic]** — um set por worker thread (PlanService já roda NumPathingThreads). Cada Path() faz `.Clear()` no início em vez de `new HashSet/Dictionary/PQ`. Pathfinding já era async, faltava eliminar a tempestade de GC por request. LINQ `.Where().ToList()` do teleportObjects substituído por loop explícito.

- [x] Fase 1.3 do plano de performance: pools + Mutex→lock nos hot paths de add/remove + dirty queues.
  - ComponentManager: Additions/Removals viraram **double-buffer A/B swap** (zero alloc per frame). `AdditionMutex`/`RemovalMutex` (kernel-object Mutex, microsseconds por Wait/Release) → `lock(object)`. `HasComponent` não chama LINQ Any mais.
  - WaterManager: `ClearDirtyQueue` e `UpdateWater` drenam pro scratch reutilizado em vez de `new List<>(DirtyCells)` per tick. `SplashLock` Mutex→lock, `GetSplashQueue` no mesmo bloco.
  - Meta: zero alocação de List em steady state nos add/remove/dirty-drain. GC Gen2 não cresce em sessões longas.

- [x] Fase 0.5 do plano de performance: LibNoise → FastNoiseLite (via shim).
  FastNoiseLite.cs (Auburn/FastNoiseLite, MIT, single-file) adicionado ao LibNoise project. Perlin.cs e FastRidgedMultifractal.cs reescritos como wrappers que delegam pra FastNoiseLite, mantendo API pública idêntica (Frequency/Persistence/Lacunarity/Seed/OctaveCount/GetValue). 52 call sites em DwarfCorp não precisaram mudar. FastNoiseLite tem inner loops SIMD-friendly — ganho em chunk gen + vertex noise + mote noise.

- [x] Fase 0.1 + 0.2 + 0.6 do plano de performance: migração MASSIVA pra .NET 10 + FNA 26.04 + SDK-style csproj.
  - DwarfCorpFNA/LibNoise/YarnSpinner/PhotoShop csprojs reescritos como SDK-style, TargetFramework=net10.0-windows, x64, ServerGC+ConcurrentGC+TieredPGO, AllowUnsafeBlocks, LangVersion=latest. Deu destrava de SIMD moderno, AVX2/AVX-512, Span/ArrayPool/Channels, Native AOT opcional.
  - FNA submodule subido de 19.07 (2019) → 26.04 (2026). FNA3D moderno com **backend Vulkan ativo**, SDL3, FAudio atualizado. O crash do item 24 (MojoShader) sumiu sozinho.
  - fnalibs atualizados em FNA_libs/win64/ (SDL3.dll, FNA3D.dll, FAudio.dll, libtheorafile.dll, D3D12 Agility SDK).
  - Newtonsoft.Json 13.0.1 → 13.0.3 via PackageReference; Steamworks.NET local DLL (x86) trocado pelo NuGet 20.2.0 (x64). Antlr 4.7.1 via NuGet (compat com geradores existentes).
  - ModCompiler stubado: CodeDomProvider foi removido do .NET Core; ficou retornando null com log. Mod .cs loading desabilitado até port pra Roslyn.
  - 3 `using System.Management.Instrumentation` zumbis removidos; `using System.Net.Configuration` e `using System.Runtime.Remoting.Messaging` também.
  - Drawer2D.Initialize virou tolerante a falha de carga de font.
  - PostBuildEvent batch substituído por `<Target>` MSBuild proper com tasks `Copy` + `Exec`.
  - Game compila 0 erros, roda, chega no MainMenuState. Alguns XNBs (fonts) ainda precisam ser gerados pelo content pipeline — issue separado do upgrade.

---

## Pendente

- [ ] 9. Abrir a janela de detalhes do dwarf ao clicá-lo.

- [ ] 10. Buscar reviews da Steam e resumir problemas.
  Status: puxei 87 de 175 via API pública (o resto a Steam filtra). Os principais temas viraram os itens 17-22 abaixo.

- [ ] 11. Melhorar a câmera em primeira pessoa (modo Walk).
  Sensibilidade e movimento ruins. Adicionar crosshair no centro quando o cursor está escondido.

- [ ] 12. Investigar stockpiles — dwarfs não estão usando os novos stockpiles.

- [ ] 13. Redesenhar o layout visual dos stockpiles (estilo RimWorld).
  Cada tipo de item ocupa um tile. Hoje tudo vai pra uma caixa no meio.

- [ ] 14. Adicionar overlay com o nome de cada item no mundo (estilo RimWorld), toggleable.

- [ ] 15. Implementar tinted outline shader screen-space.
  Referência: https://godotshaders.com/shader/thick-3d-screen-space-depth-normal-based-outline-shader/
  Toggle nas settings.

- [ ] 16. Reposicionar os botões do minimap.
  Botão de collapse ficou muito pra baixo, deveria estar colado na borda do minimap. Mesmo problema para +, −, home e dropdown.

- [ ] 17. Performance / FPS drop em sessões longas (Steam #1).
  Reviews recorrentes: FPS cai progressivamente, trava com 18+ dwarfs, jogo usa só 1 core (pathfinding no main thread), crash após ~20 min em alguns casos. Plano: medir com o profiler (F11), isolar pathfinding em thread separada se confirmar, investigar leak de entidades/partículas em sessão longa.

- [ ] 18. Crashes e instabilidade (Steam #2).
  Crashes sem mensagem depois de alguns minutos, saves corrompendo, jogo não iniciando em hardware novo, saves incompatíveis entre versões. Plano: adicionar crash handler que loga exception + stack no disco, testar em Windows 11 + Ryzen recente, versionar saves com migration path.

- [ ] 19. UI e controles confusos (Steam #3).
  Reclamações: tutorial some e não volta, menus não-intuitivos, sem cancelar construção, HUD às vezes some, hover tooltip mostrando "hover gui" em vez do nome do bloco, modo x-ray sem affordance. Plano: auditar fluxo do tutorial, consertar tooltip, adicionar botão cancelar em tool overlays.

- [ ] 20. AI e pathfinding com bugs (Steam #4).
  Dwarfs travam em loop executando task, minam blocos não-designados, múltiplos pegam a mesma tarefa e quebram, AI quebra teto pra chegar em quartos, pulam forges/anvils, atacam alvos onde eles estavam (não onde estão). Plano: revisar invalidação de task, adicionar teste de path antes de commit, recomputar alvo de ataque a cada tick.

- [ ] 21. Falta de conteúdo / progressão (Steam #6).
  End-game vazio, sem skill tree real, upgrades de ferramenta inexistentes, trade imprevisível (não sabe o que os NPCs compram). Plano: definir progressão de tier (picareta/machado/armadura), expor preview do interesse de cada facção na tela de trade.

- [ ] 22. Bugs específicos mencionados na Steam (Steam #7).
  - Dwarfs gastam 90% do tempo gambleando (review de 2018, confirmar se ainda ocorre).
  - Spider extermina colônia em minutos (balancing).
  - Animações perma-stuck.
  - Bug na map-select ao reiniciar.
  - Árvores flutuando após mineração do bloco de baixo.
  - Dwarfs presos dentro de blocos próximos ao spawn.

- [ ] 23. Temos Libs que podem ajudar a simplificar o nosso codigo ou melhorar a qualiadade? tanto em termos de performance quanto qualidade em geral? Estou considerando usar libs pra melhorar a qualidade e trocar os sistemas que temos por essas libs, sem perder as features existentes é claro.

- [ ] 25. Gostaria de uma feature pra mostrar FPS na UI do game. Isso deve ser uma opcao disponivel pra ligar/desligar nos settings do game

- [ ] 26. Revisitar o renderer custom ImGui↔MonoGame (`DwarfCorp/Gui/Debug/ImGuiRenderer.cs`).
  Durante L.1 (commit `1914b7e04`) tentei 3 pacotes NuGet (`MonoGame.ImGuiNet`, `Monogame.ImGui.Standard`, `Monogame.Imgui.Renderer`) e todos tinham problema: empacotamento quebrado (DLL fora de `lib/`), pinagem velha de ImGui.NET (1.89.x), ou sem sinal de manutenção. Acabei escrevendo o renderer do zero (~280 LOC) seguindo o pattern da amostra oficial do ImGui.NET. Funciona bem, mas é código de infra que passa a ser nosso pra manter.
  **Rever quando:** (a) surgir um package mantido de verdade; (b) atualizar pra ImGui.NET 2.x (se/quando ImGui upstream quebrar o formato de ImDrawVert); (c) tocar em MonoGame DX→Vulkan; (d) precisar docking/multi-viewport (nosso renderer é single-viewport por simplicidade).

- [ ] 29. Remover `DwarfGame.GpuLock` quando Fase B.1 (split mesh-gen / GPU upload serial) estiver em produção.
  Lock global que serializa background threads contra o render thread. Motivação original (commit `4eef2369c`) era FNA 26 Vulkan VkCommandPool race que corrompia heap. M.4 (commit pendente) revalidou: **a causa Vulkan sumiu, mas o lock continua necessário em DX11** — `ID3D11DeviceContext` (o context que MonoGame despacha) também não é thread-safe, e `SetData` vai por ele. Custo atual é ~50ns não contendido, irrelevante.
  **Disparar remoção quando:** Fase B.1 terminar de canalizar TODA escrita GPU vinda de background threads por uma fila drenada em thread única (worker gera `MeshData` POCO em CPU, main thread consome queue e faz `SetData`). Com esse design, ninguém toca `GraphicsDevice` fora do render thread, e o lock vira dead code.
  **Locais pra limpar quando for hora:** `DwarfGame.cs` (field + Draw wrapper), `GeometricPrimitive.ResetBuffer`, `TiledInstanceGroup.Flush`, `ResourceGraphicsHelper.GetOrCreateSharedTexture`, `DwarfLayerStack.Update` (este último é WIP do Daniel).

- [ ] 28. Consertar o screen-space outline post-effect após migração MonoGame.
  `ScreenSpaceOutline.fx` + `OutlineEffect.cs` estão desativados por default (`GameSettings.EnableOutline = false`) porque o blit via SpriteBatch não funciona em MonoGame DX11 — vira um frame preto que esconde o viewport 3D inteiro. Confirmado pela toggle do Render Inspector (F12): desligando, o mundo renderiza normal; ligando, preto.
  **Causas prováveis:** (a) SpriteBatch em MonoGame DX11 usa vertex layout fixo (`VertexPositionColorTexture`) e o shader só declara `POSITION + TEXCOORD0`; (b) SpriteBatch sobrescreve matrices via `MatrixTransform` parameter mas o shader usa `World/View/Projection` diretamente; (c) `POSITION0` como output semantic do VS vira ambíguo em SM 4.0 (deveria ser `SV_POSITION`).
  **Abordagens de fix:** (1) reescrever o shader pra seguir o pattern padrão SpriteBatch (usar `MatrixTransform` + aceitar `COLOR + TEXCOORD`); (2) substituir `SpriteBatch.Draw` por um draw direto com quad fullscreen e vertex buffer custom (mais controle, sem SpriteBatch interferir). Opção 2 é mais limpa.
  **Escopo:** 30-60 min focado. Não urgente — outline é cosmético.

- [ ] 27. Lighting: substituir o array fixo `xLightPositions[MAX_LIGHTS]` por clustered/tiled forward lighting.
  Pós-M.2, `Shader.MaxLights` e `#define MAX_LIGHTS` em `TexturedShaders.fx` **precisam ficar iguais** (hoje ambos = 16). FNA/MojoShader tolerava mismatch; MonoGame DX11 crasha com `IndexOutOfRangeException` em `EffectParameter.SetValue`. O setter de `Shader.LightPositions` tem um clamp defensivo como safety net, mas é curativo — o design estrutural certo é um forward+ clustered pass (cada tile de tela carrega lista de luzes que o tocam, shader itera apenas O(k) com k<<16). Isso destrava cenas com >16 luzes dinâmicas sem crash nem silent drop.
  **Planejado em** `PERF_PLAN_EXTREME.md` Fase E.2′. **Disparar quando:** a) profile mostrar iluminação como gargalo; b) feature nova exigir mais de 16 lights visíveis (fogueiras densas, cristais brilhantes por todo o mapa); c) PBR entrar em jogo (cada luz vira mais custosa, worth tiling).

> Itens 27-35 (migração FNA→MonoGame e Fases B/C/D/M/L/E/F/G de performance) **movidos pra [`PERF_PLAN_EXTREME.md`](PERF_PLAN_EXTREME.md)** — veja a seção "Status da Migração" lá pro tracker oficial.
