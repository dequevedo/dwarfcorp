# Plano v5 — DwarfCorp: FNA → MonoGame + Stack Composável Moderno

> **Status v5 (2026-04-19):** Após reverter a tentativa Stride (v4), decisão final: migrar FNA → **MonoGame 3.8.x (DX11)** + montar stack composável de libs modernas .NET (Arch ECS, ImGui.NET, ZLogger, MessagePipe, etc.). Workflow 100% code-first, LLM-native. Detalhes e justificativas abaixo.
> **v4:** tentativa Stride, revertida após análise honesta.
> **v3:** MonoGame com PBR/shadows custom, absorvido no v5 sem a ambição gráfica.
> **v2:** reorientação pós-migração .NET 10 + FNA 26.04.

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

**Fase 1.1 revertida** (parallel chunk rebuild) — HEAP_CORRUPTION AMD Vulkan. Corrigida em Fase B.1 abaixo.

---

## Fases v5 (ordem de execução)

### Fase M — Migração FNA → MonoGame 3.8.x

**Escopo:** 1-2 semanas. Port mecânico. Bloqueante pra compute mas não pra B/C/D.

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

### Fase B — Chunk rebuild refactor (carry de v2)

Resolve o revertido Fase 1.1.

**B.1 — Separar mesh-gen de GPU upload**
- `GeometryBuilder.Rebuild*` retorna `MeshData` POCO (sem `GraphicsDevice`).
- `MeshUploadQueue` `ConcurrentQueue<(VoxelChunk, MeshData)>` — workers enfileiram, single GPU-upload step em `Draw()` consome.
- N workers = `Environment.ProcessorCount - 1` em `ChunkManager.RebuildVoxelsThread`.

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

### Fase C — Pathfinding async + ArrayPool

**C.1 — API async/TCS**
- `PlanService.Enqueue` → `Task<List<MoveAction>>` via `TaskCompletionSource`.
- AI: `await world.Pathfinder.PlanAsync(start, goal, ct)`.
- CT propaga ao worker.

**C.2 — Spatial heuristic cache**
- `Dictionary<VoxelHandle, float>` per-thread via `[ThreadStatic]`.
- Invalidação em hook de `VoxelChunk.InvalidateMesh`.

**C.3 — ArrayPool em spatial queries**
- `ComponentManager.FindRootBodiesInsideScreenRectangle`: `HashSet`/`ToList` → `ArrayPool<GameComponent>.Shared`.
- Inicia adoção de ArrayPool (hoje zero usos).

---

### Fase D — Component update paralelo

**D.1 — JobScheduler**
- Novo `DwarfCorp/Tools/Threading/JobScheduler.cs` — `Parallel.ForEach` + partitioner por hash chunk-coord.

**D.2 — ComponentManager.Update paralelo**
- [ComponentManager.cs:241](DwarfCorp/Components/Systems/ComponentManager.cs:241): partição em buckets.
- Commits em `ConcurrentQueue` drenada fim-de-frame.

**D.3 — Auditoria thread-safety**
- Cada `IUpdateableComponent.Update` marcado "parallel-safe" vs "main-thread-only".
- Main-thread-only em pass serial no fim.

---

### Fase L — Adoção do stack composável (incremental)

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

**L.5 — BenchmarkDotNet + xUnit v3**
- Novo projeto `DwarfCorp.Tests/`.
- Tests: save roundtrip, world gen determinism, A* correctness, ECS migration idempotence.
- Benchmarks: `GeometryBuilder.Rebuild`, `AStarPlanner.Path`, `ComponentManager.Update`, Voxel face visibility.

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

| # | Fase | Risco | ROI | Depende de |
|---|---|---|---|---|
| 1 | C.3 (ArrayPool FindRootBodies) | Baixo | Médio | — |
| 2 | B.1 (mesh-gen/upload split) | Médio | Muito alto | — |
| 3 | B.2 (greedy mesh) | Médio | Muito alto | B.1 |
| 4 | B.3 (SIMD scan) | Baixo | Alto | B.2 |
| 5 | C.1 (pathfinding async) | Médio | Alto | — |
| 6 | C.2 (heuristic cache) | Baixo | Médio | C.1 |
| 7 | D (component update paralelo) | Alto | Muito alto | — |
| 8 | M (FNA → MonoGame) | Médio | Destrava compute | pref. após B/C/D |
| 9 | L.5 (test infra) | Baixo | Médio (dev vel.) | — |
| 10 | L.1 (ImGui debug) | Baixo | Médio | M |
| 11 | L.2 (ZLogger + DI) | Baixo | Médio | — |
| 12 | L.3 (MessagePipe) | Baixo | Médio | L.2 |
| 13 | L.4 (Arch + save shim) | Alto | Alto (longo prazo) | M, L.5 |
| 14 | L.6 (Stb*/FontStash) | Baixo | On-demand | — |
| 15 | E/F/G | — | profile-driven | — |

**Janela estimada:**
- Mês 1: B/C/D no FNA (platform-agnostic).
- Mês 2: M + L.5 + L.1.
- Mês 3: L.2/L.3 + L.4.A.
- Meses 4-5: L.4.B (cutover Arch + save shim).
- Mês 6+: E/F/G conforme profile.

---

## Verificação

1. **Baselines:** `baseline_v5_pre.csv` (antes), `baseline_v5_cpu.csv` (após B+C+D), `baseline_v5_monogame.csv` (após M), `baseline_v5_arch.csv` (após L.4).
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
