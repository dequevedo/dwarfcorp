# Plano v5 — DwarfCorp: FNA → MonoGame + Stack Composável Moderno

> **Status v5 (2026-04-19):** Após reverter a tentativa Stride (v4), decisão final: migrar FNA → **MonoGame 3.8.x (DX11)** + montar stack composável de libs modernas .NET (Arch ECS, ImGui.NET, ZLogger, MessagePipe, etc.). Workflow 100% code-first, LLM-native. Detalhes e justificativas abaixo.
> **v4:** tentativa Stride, revertida após análise honesta.
> **v3:** MonoGame com PBR/shadows custom, absorvido no v5 sem a ambição gráfica.
> **v2:** reorientação pós-migração .NET 10 + FNA 26.04.

---

## Status da Migração (tracker único)

Este é o **único** tracker oficial. Features menores/UX/balance ficam em [`TODO_LIST.md`](TODO_LIST.md).

**Princípio:** a migração é um projeto só. Código que está saindo não recebe otimização — subsistemas são **reimplementados limpos e performantes** no stack novo (MonoGame + libs modernas), não portados "como estão".

Legenda: ✅ concluída · 🚧 em andamento · ⬜ pendente · ⏸️ deferida (aguarda gatilho) · ❌ revertida

### Decisões já tomadas
- ✅ **Fase A** — Compute discovery em FNA 26.04 ([docs/perf_compute_discovery.md](docs/perf_compute_discovery.md))
- ✅ **Engine decision** — MonoGame 3.8 escolhido; 7 engines avaliados ([docs/engine_decision.md](docs/engine_decision.md))

### Fase M — Port mecânico (MonoGame rodando, 1-2 semanas)
Mínimo pra o jogo compilar e rodar em MonoGame. **Sem otimização** nesta fase — só o swap de plataforma.
- ✅ **M.1** — Swap FNA submodule → `MonoGame.Framework.WindowsDX 3.8.4.1` (commit `8c2d1b939`)
- ✅ **M.2** — Content pipeline migrado: platform Windows, EffectProcessor default, profiles SM4, specialization removida (commits `35ce163ee`, `57cc80257`)
- ✅ **M.3** — API compat: SDL2/TextInputEXT/OnExiting/GetVertexBuffers substituídos (commit `7629f3fdd`)
- ✅ **M.4** — Revalidado: lock **mantido**. Motivação original (FNA/Vulkan VkCommandPool) sumiu, mas DX11 `ID3D11DeviceContext` também não é thread-safe, e `SetData` vai por ele. XML-doc reescrito com a nova razão. Remoção definitiva = quando Fase B.1 canalizar escritas GPU por queue single-thread (TODO item 29).
- ✅ **M.5** — Framework pronto (auto-export CSV via `DWARFCORP_PERF_EXPORT` + `ForceFrameCapture` sticky + [`docs/perf_bench.md`](docs/perf_bench.md)). **Baseline capturado**: [`docs/baselines/baseline_v5_monogame.csv`](docs/baselines/baseline_v5_monogame.csv). Diagnóstico em [`.analysis.md`](docs/baselines/baseline_v5_monogame.analysis.md) — p50=16.85ms (59 fps) saudável, mas p99=559ms e max=1060ms: problema é **hitches de longa cauda**, não CPU bound geral. Valida ordem do rollout B.1 (chunk rebuild hitch), C.3+L.4 (GC), GUI migration (18% do frame).
- ⚠️ **Follow-up** — main menu visual não aparece (jogo chega no PlayState); debug pós-L.1 com ImGui.

### Fase L — Adoção do stack moderno (bases antes das reimplementações)
Traz as libs novas antes de reescrever subsistemas. Elas são os blocos de construção das Fases B/C/D.
- 🚧 **L.5** — Projeto `DwarfCorp.Tests/` criado com xUnit v3 (10 testes passando: Perlin/DwarfBux/FileUtils JSON roundtrip). BenchmarkDotNet adicionado quando houver perf work concreto.
- 🚧 **L.2** — DI container + ZLogger bootstrap via `DwarfCorp/Infrastructure/Services.cs`. `Services.Initialize()` em Program.Main, log file em `%APPDATA%/DwarfCorp/dwarfcorp.log`. Migração `Console.WriteLine → ILogger` incremental.
- 🚧 **L.1** — ImGui.NET integrado com renderer custom `DwarfCorp/Gui/Debug/ImGuiRenderer.cs` (~280 LOC). `DebugOverlay` (F12) mostra FPS + backbuffer + GameState ativo. Base pra debugar o main-menu invisível.
- 🚧 **L.3** — MessagePipe registrado em DI + `EventBus` static façade pra callsites legados. Dois eventos demo (`AppStarted`, `GameStateEntered`). 4 testes roundtrip passando. Eventos reais (`ChunkInvalidated`, `DwarfSpawned`, `TaskCompleted`) vêm conforme B/C/D.
- ⬜ **L.4** — Arch ECS + save-migration shim (`SaveFormatVersion` v1→v2)
- ⬜ **L.6** — Stb* + FontStashSharp (on-demand)

### Fase B — Subsistema de Voxel/Mesh (reimplementação limpa em DX11)
Escrever do zero com arquitetura paralela correta (mesh-gen CPU / upload serial) + greedy meshing + SIMD. Não é "refactor do GeometryBuilder antigo" — é o módulo novo.
- ⬜ **B.1** — Mesh-gen em workers + `MeshUploadQueue` serial no `Draw()`
- ⬜ **B.2** — Greedy meshing (Mikola/Tantan) na geração de sliceGeometry
- ⬜ **B.3** — SIMD AVX2 (`Vector256<byte>`) no scan de face-visibility
- ⬜ **B.4** — Instrumentação profiler separada (mesh-gen vs upload)

### Fase C — Subsistema de Pathfinding (reimplementação async-first)
A* escrito do zero como async/TCS + pools + cache. Não é retrofit do `AStarPlanner` atual.
- ⬜ **C.1** — API `Task<List<MoveAction>>` via TCS + CancellationToken
- ⬜ **C.2** — Spatial heuristic cache por thread
- ⬜ **C.3** — ArrayPool em `FindRootBodiesInside*` + padrão estendido a outros hot paths

### Fase D — Subsistema de Update (Arch systems paralelos)
Com Arch ECS já adotado em L.4, o update paralelo é natural (archetype-based iter). Não é refactor do `ComponentManager`.
- ⬜ **D.1** — JobScheduler wrapper pra `Parallel.ForEach` + partitioner por chunk-coord
- ⬜ **D.2** — Arch systems paralelos substituem `ComponentManager.Update`
- ⬜ **D.3** — Auditoria thread-safety dos componentes main-thread-only

### Deferidas (aguardam gatilho concreto via profiling)
- ⏸️ **Fase E** — GPU avançada (compute particles, light culling, mesh batching, liquid instancing, Hi-Z occlusion).
- ⏸️ **Fase F** — Estrutural (DynamicBVH, WaterManager tile-partitioned, Body SoA).
- ⏸️ **Fase G** — GUI de jogo (96 arquivos `Gui/*`) migrada pra ImGui. Debug/dev UI já sai em L.1.

### Histórico
- ❌ **Plano v4 (Stride)** — tentado 2026-04-19, revertido no mesmo dia em `fdd522708`.

---

## Context

Estado atual: DwarfCorp roda em FNA 26.04 + Vulkan + .NET 10 + Server GC após migração massiva (v2). A Fase A de discovery ([docs/perf_compute_discovery.md](docs/perf_compute_discovery.md)) fechou que FNA **não expõe compute shaders**.

Consideramos Stride (v4, revertido). Rejeitado porque:
- Cultura editor-centric (anti-LLM-first).
- SDSL proprietária, pouco training data de LLM.
- Track record fraco (nenhum hit shipped).
- Rewrite de 6-9 meses.

Tabela comparativa completa de engines considerados: [docs/engine_decision.md](docs/engine_decision.md).

**Decisão final v5:** MonoGame 3.8.x + stack composável de libs modernas. Workflow 100% code-first, LLM-native.

Justificativa MonoGame vs alternativas:
- Migração FNA→MonoGame é **port mecânico de 1-2 semanas** (APIs irmãs de XNA4).
- HLSL tem 20 anos de training data. SDSL/ShaderLab não.
- Track record forte (Celeste, Stardew Valley, Fez, Bastion, Streets of Rage 4).
- Cultura code-first nativa — zero editor em lugar algum.
- MonoGame 3.8 expõe **compute shaders** no backend DX11, destrava tudo que Fase A bloqueou.
- Mantém 95% do código DwarfCorp intacto.

Meta: 60 FPS estável em cena stress, Gen2 GC não crescendo em 30+ min, stack moderno adotado incrementalmente sem quebrar saves. Visual fica **basic low-poly/pixelated** — Fases E/H gráficas ficam deferidas.

---

## Stack-alvo — decisões e justificativas

### Runtime: .NET 10 + ServerGC + TieredPGO + AllowUnsafeBlocks + x64
**Mantido (já feito em v2).**

### Engine base: MonoGame 3.8.x (DX11 backend)
- API 95% compatível com FNA (ambos XNA4). Port mecânico.
- Compute shaders destravados. Training data massivo. MIT. Culture code-first.

### ECS: Arch
- Zero-alloc, archetype-based, perf tipo Burst sem native deps.
- C# puro, actively maintained.
- Constraint: saves atuais têm FQN persistido (`TypeNameHandling.Auto`) → requer **shim de migração** (+2-3 semanas) ao carregar save antigo.

### UI: ImGui.NET (gradual) + manter `DwarfCorp/Gui/*` custom por ora
- Game UI custom (96 arquivos) funcional; rewrite total = 6-10 semanas sem urgência.
- ImGui entra primeiro em **debug/dev UI** (profiler, console). Risco zero.
- Port completo fica planejado pra depois.

### Physics: manter custom por ora (BepuPhysics **adiado**)
- Physics atual simples (gravity + AABB) e funciona.
- Jogo low-poly estilizado — não precisa ragdoll/soft body/joints.
- Gatilho pra migrar: feature concreta que exija physics avançada.

### Serialization: manter **Newtonsoft.Json 13.0.3**
- Saves existentes usam `TypeNameHandling.Auto` + binder customizado.
- Trocar seria quebrar saves (constraint: "não podemos arriscar").
- Converters customizados já escritos (Vector3/Matrix/Color).
- System.Text.Json source-gen fica como opção pra config/settings novos (não-saves).

### Logging: ZLogger
- Zero-alloc, async, source-generator. Substitui `Console.WriteLine` + `LogWriter`.

### DI: Microsoft.Extensions.DependencyInjection
- Padrão .NET moderno. Substitui singletons/globals. Facilita testes.

### Messaging: MessagePipe
- Pub/sub in-process zero-alloc (Cysharp). Substitui delegates espalhados.

### Content / Assets: MGCB default; Stb* sob demanda
- `Content/Content.mgcb` já existe e funciona. Migrar 718 assets é trabalho sem urgência.
- `StbImageSharp` / `StbVorbis.NET` / `Assimp.NET` entram quando precisar hot-reload ou asset fora do MGCB (mods dinâmicos).

### Fontes: FontStashSharp (gradual, junto com ImGui)
- TTF dinâmico runtime. Boa integração com ImGui.

### Noise: FastNoiseLite
**Mantido (já feito em v2).**

### Benchmarks: BenchmarkDotNet
- Regression testing das Fases B/C/D. Padrão .NET.

### Testing: xUnit v3
- DwarfCorp tem zero testes. Adotar padrão desde dia 1.

### Profiler: `PerformanceMonitor` custom + OpenTelemetry opcional
- `PerformanceMonitor.cs` já tem ring-buffer + export CSV + F11 (v2).

### SIMD / Intrinsics: System.Runtime.Intrinsics + System.Numerics
**Já disponível** (.NET 10). Uso: Fase B.3.

---

## O que JÁ foi feito (referência)

Lista completa em `TODO_LIST.md` "Concluído". Destaques:

- .NET 10 + FNA 26.04 + Vulkan + SDK-style csproj
- Server GC + TieredPGO + AllowUnsafeBlocks + x64
- Profiler com export CSV, ProfilerPanel F11
- LibNoise → FastNoiseLite
- A* com `[ThreadStatic]` pools
- ComponentManager double-buffer + Mutex→lock
- WaterManager scratch drain + Mutex→lock
- GpuLock (Vulkan thread-safety)
- Fase A (compute discovery) ✅ em [docs/perf_compute_discovery.md](docs/perf_compute_discovery.md)

---

## Fases v5 (ordem de execução)

### Fase M — Port mecânico FNA → MonoGame 3.8.x

**Escopo:** 1-2 semanas. Objetivo único: jogo compilando e rodando em MonoGame. Nenhuma otimização aqui — isso vem nas fases seguintes.

**M.1 — Swap de reference**
- `DwarfCorpFNA.csproj`: remover `<ProjectReference Include="..\FNA\FNA.Core.csproj" />`, adicionar `<PackageReference Include="MonoGame.Framework.WindowsDX" Version="3.8.*" />`.
- Remover submódulo `FNA/` (arquivar SHA `5d510f8837d3c88bbfe53176fa913fa6578bf238` em tag `legacy-fna-final`).
- Atualizar `CopyNativesAndContent`: MonoGame resolve binários via NuGet.

**M.2 — Content pipeline: mantém MGCB**
- `Content/Content.mgcb` já é formato MonoGame. Rebuild via `MonoGame.Content.Builder.Task`.
- Regerar `.xnb`. Resolver quirks de fonts/effects caso a caso.

**M.3 — API compat audit**
- Compilar, listar erros, corrigir em bloco.
- Quebras prováveis: `SpriteBatch.Begin` params, `RenderTarget2D` constructors, `PresentationParameters`.

**M.4 — GpuLock revalidação**
- Provavelmente removível após MonoGame DX11 (driver tem locking próprio).
- Decisão: remover após M.3 rodar estável 30 min.

**M.5 — Baseline pós-MonoGame**
- CSV `baseline_v5_monogame.csv`. Aceite: FPS ≥ FNA atual, Gen2 estável, save antigo carrega.

---

### Fase B — Subsistema de Voxel/Mesh (reimplementação limpa)

Módulo novo em MonoGame DX11 com arquitetura paralela correta desde o dia 1. Não porta `GeometryBuilder` antigo: constrói em cima dos tipos limpos (`MeshData` POCO, `MeshUploadQueue`, workers dedicados).

**B.1 — Mesh-gen paralelo + GPU upload serial**
- `MeshData` POCO: `VertexPositionColor[]`, `ushort[]`. Zero dependência de `GraphicsDevice`.
- `MeshUploadQueue` = `ConcurrentQueue<(VoxelChunk, MeshData)>`.
- N workers (= `Environment.ProcessorCount - 1`) geram mesh em paralelo e enfileiram.
- Single upload step no início de `Draw()` drena até K items e aplica `SetData` em `DynamicVertexBuffer`.

**B.2 — Greedy meshing**
- Algoritmo Mikola/Tantan em `GenerateSliceGeometry` ([GeometryBuilder.cs:85](DwarfCorp/World/Voxels/ChunkBuilder/GeometryBuilder.cs:85)).
- Chave composta greedy: `(tile | grass | decal | ambient)`.
- Queda 10-50× em tris/chunk.

**B.3 — SIMD scan de visibilidade**
- `Avx2.IsSupported`: `Vector256<byte>` XOR com neighbor-slice → `MoveMask` → bitmask.
- Fallback scalar.

**B.4 — Instrumentação**
- `PerformanceMonitor.PushFrame("ChunkMeshGen")` / `PushFrame("ChunkGpuUpload")` separados.

---

### Fase C — Subsistema de Pathfinding (reimplementação async-first)

Novo `Pathfinder` async/TCS construído sobre `System.Threading.Channels` + pools per-thread. Não é retrofit do `AStarPlanner` atual — é módulo novo, API Task-based desde o início.

**C.1 — API async/TCS**
- Novo `IPathfinder.PlanAsync(start, goal, ct)` → `Task<List<MoveAction>>` via `TaskCompletionSource`.
- AI: `var path = await world.Pathfinder.PlanAsync(start, goal, ct);`.
- CancellationToken propaga ao worker (verificado em cada pop do open set).
- Worker pool com `System.Threading.Channels` (N workers = `NumPathingThreads` do settings).

**C.2 — Spatial heuristic cache**
- `Dictionary<VoxelHandle, float>` per-thread via `[ThreadStatic]`.
- Invalidação em hook de `VoxelChunk.InvalidateMesh`.

**C.3 — ArrayPool em spatial queries**
- Spatial queries do Arch world (ex: entidades dentro de um screen rectangle pra seleção) implementadas zero-alloc desde o início, usando `ArrayPool<Entity>.Shared` + struct enumerators.
- Padrão depois estendido a todos os hot paths (AI perception, building placement validation, etc.).

---

### Fase D — Subsistema de Update (Arch systems paralelos)

Com Arch ECS adotado em L.4, update paralelo é natural: archetype-based iter é trivialmente paralelizável. `ComponentManager` **deixa de existir** — substituído por Arch `World` + systems tipados.

**D.1 — JobScheduler**
- Novo `DwarfCorp/Tools/Threading/JobScheduler.cs` — wrapper sobre `Parallel.ForEach` + partitioner por hash chunk-coord (entidades no mesmo chunk caem no mesmo worker, zero contenção).

**D.2 — Arch systems paralelos**
- Cada system (movement, health, AI tick, physics tick) roda sobre query archetype-based, paralelizado por default.
- Commits (spawn/despawn) em `ConcurrentQueue` drenada fim-de-frame.

**D.3 — Auditoria thread-safety**
- Systems marcados "parallel-safe" vs "main-thread-only" (lights/sound/UI escrevem estado compartilhado).
- Main-thread-only em pass serial no fim do frame.

---

### Fase L — Adoção do stack composável (fundação antes dos subsistemas)

Stack moderno entra **antes** das reimplementações B/C/D porque essas são construídas em cima dele. Ordem dentro de L: testes (L.5) e observabilidade (L.2) primeiro; ImGui e messaging secundários; Arch por último porque tem save migration crítica.

**L.1 — ImGui.NET integration**
- `PackageReference ImGui.NET` (ou `Hexa.NET.ImGui`).
- `ImGuiRenderer` ponte MonoGame↔ImGui.
- Primeiro panel: alongside existing profiler (F12). Depois port ProfilerPanel pra ImGui. Dev console `~`.

**L.2 — ZLogger + DI**
- PackageReferences: `ZLogger`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`.
- Novo `DwarfCorp/Infrastructure/Services.cs` — host builder.
- Substituir `Console.WriteLine` gradualmente por `ILogger<T>`.

**L.3 — MessagePipe**
- PackageReferences: `MessagePipe`, `MessagePipe.Microsoft.Extensions.DependencyInjection`.
- Primeiros use-cases: `ChunkInvalidated`, `DwarfSpawned`, `TaskCompleted`.

**L.4 — Arch ECS + save-migration shim**
- PackageReference `Arch`.
- Novo `DwarfCorp/ECS/World.cs` wrapper sobre `Arch.Core.World`.
- **Shim**: `DwarfCorp/Saving/ComponentSaveMigration.cs` lê `ComponentSaveData` antigo JSON, reconstrói Arch entities.
- Versioning: `SaveFormatVersion` em `MetaData` (v1 = ComponentManager, v2 = Arch).
- xUnit: save antigo → load via shim → save v2 → reload v2 idempotente.
- Fase D vira parte deste trabalho (Arch systems já são archetype-parallelizable).

**L.5 — BenchmarkDotNet + xUnit v3 (primeira fundação de L, antes de tudo)**
- Novo projeto `DwarfCorp.Tests/`.
- Tests cobrindo o que **existe hoje** pra garantir que nada quebra na reimplementação: save roundtrip, world gen determinism, A* correctness (contra o planner atual), pathing end-to-end.
- Benchmarks das reimplementações futuras (mesh-gen, A*, systems Arch) conforme cada subsistema for substituído.

**L.6 — Stb* + FontStashSharp (on-demand)**
- Quando precisar carregar asset fora de MGCB.

---

### Fase E — GPU avançada (DEFERIDA)

Visual basic low-poly pixelated → PBR/shadows não entram. Se profile mostrar gargalo GPU:
- E.1 Particles VS analítico ou compute.
- E.2 Light culling CPU-side.
- E.3 Mega-mesh batching (ring buffer).
- E.4 Liquid instancing.
- E.5 Hi-Z occlusion (MSOC port).

### Fase F — Estrutural (OPCIONAL, profile-driven)

F.1 DynamicBVH (spatial queries O(log n)) — F.2 Water tile-partitioned — F.3 Body SoA.

### Fase G — GUI migração completa (DEFERIDA)

Rewrite dos 96 arquivos `Gui/*` pra ImGui. 6-10 semanas. Depois de L.1 provar pipeline.

---

## Rollout (ordem)

Princípio: **M primeiro, depois L (ferramentas), depois B/C/D (subsistemas reimplementados).** Código do FNA atual não recebe otimização — subsistemas novos são escritos limpos em cima do stack novo.

| # | Fase | Risco | ROI | Depende de |
|---|---|---|---|---|
| # | Fase | Risco | ROI | Depende de |
|---|---|---|---|---|
| 1 | **M.1** — swap reference FNA→MonoGame | Baixo | Destrava tudo | — |
| 2 | **M.2** — rebuild MGCB | Baixo | M | M.1 |
| 3 | **M.3** — API compat audit | Médio | M | M.2 |
| 4 | **M.4** — revalidar/remover `GpuLock` | Baixo | Limpa código | M.3 |
| 5 | **M.5** — baseline MonoGame | Baixo | Validação | M.4 |
| 6 | **L.5** — test infra (xUnit + BenchmarkDotNet) | Baixo | Base pra tudo que vem | M |
| 7 | **L.2** — ZLogger + DI + Hosting | Baixo | Base pra tudo | M |
| 8 | **L.1** — ImGui.NET + debug panel | Baixo | Dev velocity | M |
| 9 | **L.3** — MessagePipe | Baixo | Desacoplamento | L.2 |
| 10 | **L.4** — Arch ECS + save-migration shim | Alto | Pré-req D | L.5 |
| 11 | **B.1** — mesh-gen paralelo + upload serial | Médio | Muito alto | M |
| 12 | **B.2** — greedy meshing | Médio | Muito alto | B.1 |
| 13 | **B.3** — SIMD AVX2 scan | Baixo | Alto | B.2 |
| 14 | **B.4** — profiler separado | Baixo | Instrumentação | B.1 |
| 15 | **C.1** — pathfinding async/TCS | Médio | Alto | L.5 |
| 16 | **C.2** — heuristic cache A* | Baixo | Médio | C.1 |
| 17 | **C.3** — ArrayPool em spatial queries | Baixo | Médio | L.5 |
| 18 | **D.1-D.3** — Arch systems paralelos | Alto | Muito alto | L.4 |
| 19 | **L.6** — Stb*/FontStash | Baixo | On-demand | — |
| 20 | E/F/G | — | profile-driven | — |

**Janela estimada:**
- Semanas 1-2: **M** — migração mecânica completa, jogo rodando em MonoGame, baseline validado.
- Semanas 3-5: **L.5 + L.2 + L.1 + L.3** — ferramentas modernas montadas (testes, logging, DI, ImGui debug, messaging).
- Meses 2-3: **L.4** — Arch ECS adotado + save-migration shim + testes de idempotência.
- Mês 4: **B.1-B.4** — subsistema voxel/mesh reescrito limpo com mesh-gen paralelo + greedy + SIMD.
- Mês 5: **C.1-C.3** — subsistema de pathfinding reescrito async-first.
- Mês 6: **D.1-D.3** — update paralelo via Arch systems; `ComponentManager` apagado.
- Mês 7+: E/F/G apenas se profile apontar gargalo real.

---

## Verificação

1. **Baselines:** `baseline_v5_fna.csv` (estado atual, antes de M), `baseline_v5_monogame.csv` (após M), `baseline_v5_cpu.csv` (após B+C+D), `baseline_v5_arch.csv` (após L.4).
2. **Cena stress canônica:** seed fixo + WorldSize=Large + 30 dwarfs + combat trigger. Documentar em `docs/perf_bench.md`.
3. Após cada PR: diff CSV em `ComponentManager.Update`, `ChunkMeshGen`, `ChunkGpuUpload`, `ChunkRenderer.Render`, `WaterManager.UpdateWater`, frame p50/p95, `GC.CollectionCount(2)`.
4. 30 min sem crash + Gen2 estável = aceite.
5. **Save roundtrip obrigatório em M/L.4:** 3 saves reais, load/play 5 min/save/reload. L.4 especificamente: save v1 → shim → save v2 → reload v2 idempotente.
6. `dotnet test` em CI local.

---

## Arquivos críticos

- `DwarfCorp/DwarfCorpFNA.csproj` — M.1
- `DwarfCorp/DwarfGame.cs` — M.4 (GpuLock revalidation)
- `DwarfCorp/Content/Content.mgcb` — M.2
- `DwarfCorp/World/Voxels/ChunkBuilder/GeometryBuilder.cs` — B.1/B.2/B.3
- `DwarfCorp/World/Voxels/ChunkManager.cs` — B.1
- `DwarfCorp/Tools/Planning/AStarPlanner.cs` — C.1/C.2
- `DwarfCorp/Components/Systems/ComponentManager.cs` — C.3/D/L.4
- `DwarfCorp/AssetManagement/GameSave/SaveGame.cs` — L.4 shim
- `DwarfCorp/AssetManagement/GameSave/FileUtils-SaveJson.cs` — compat converters
- Novos: `DwarfCorp.Tests/` (L.5), `DwarfCorp/Tools/Threading/JobScheduler.cs` (D), `DwarfCorp/Infrastructure/Services.cs` (L.2), `DwarfCorp/ECS/World.cs` (L.4), `DwarfCorp/Saving/ComponentSaveMigration.cs` (L.4)

## Utilities existentes a reusar

- `PerformanceMonitor.PushFrame/PopFrame` ([PerformanceMonitor.cs:192](DwarfCorp/Tools/PerformanceMonitor.cs:192))
- `CrashBreadcrumbs` pra transições críticas
- `DwarfGame.DoLazyAction` — callback main thread
- `[ThreadStatic]` pattern de `AStarPlanner` — modelo pra D

## Riscos e mitigação

| Risco | Mitigação |
|---|---|
| MonoGame quebra assets XNB (fonts/effects) | M.2 regenerado via MGCB; validar antes de remover FNA |
| Saves antigos corrompem após L.4 | Shim com xUnit idempotência; `SaveFormatVersion` versionado |
| Arch perf não se paga vs ComponentManager | BenchmarkDotNet antes/depois; ComponentManager fallback sob flag |
| ImGui quebra em transição Vulkan→DX11 | L.1 isolado como panel paralelo, não substitui crítico |
| Newtonsoft mantido → perf pior | Aceito; saves não são hot path, medir antes de otimizar |
| FNA submodule hash perdido | Tag `legacy-fna-final` antes do remove |
