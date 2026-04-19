# Plano: Performance EXTREMA v4 — DwarfCorp (migração Stride + stack ultra-moderno)

> **Status v4 (2026-04-19):** Decisão final: migrar para **Stride 4.2+** (engine C# open-source MIT) + adotar stack .NET moderno extremo. Engine entrega PBR/CSM/SSAO/VFX/compute/ECS/editor prontos. DwarfCorp mantém apenas o que é único (voxel, IA, colony sim). Workflow LLM-first via code-first patterns do Stride. **Escopo: 6-9 meses.**
> **v3 (2026-04-19):** Considerou MonoGame 3.8.x — abandonado; ainda seria muita infra custom.
> **v2 (2026-04-19 cedo):** Plano reorientado pós-migração .NET 10 + FNA 26.04.
> **v1 (2026-04-18):** Plano original pré-migração.

## Context

Após a Fase A descobrir que FNA não expõe compute ([docs/perf_compute_discovery.md](docs/perf_compute_discovery.md)) e após discussão estratégica (2026-04-19), decisão de migrar o projeto pra **Stride Engine 4.2+** ao invés de MonoGame. Racional:

- **Stride traz pronto** o que MonoGame exigiria escrever do zero: pipeline PBR, cascaded shadows, SSAO/GTAO, post-processing stack, VFX editor, ECS moderno, physics Bullet, audio, content pipeline.
- **C# puro** (engine e game code no mesmo runtime .NET 10+) — zero FFI, LLM-friendly.
- **MIT license**, ownership claro, sem risco de fee model.
- **DwarfCorp deleta ~40k linhas de infra custom** (WorldRenderer, Primitives, Gui framework, content loader, save infra parcial) e foca nas ~85% de código que é gameplay único.
- **Compute shaders** via SDSL (shader language do Stride, ~HLSL) — compute dispatch first-class.
- **LLM-first workflow** viável: todos os assets são texto (YAML/XML), editor é opcional; cenas/entidades/materiais construíveis 100% em código C#.

Meta final: 60 FPS estável em cena stress (mapa grande + 30+ dwarfs + combate) **COM** PBR + shadows + SSAO + bloom + GTAO + compute particles, Gen2 GC estável por 30+ min, mesh-gen absorvendo todos os cores, stack .NET atualizado ao extremo.

**Escopo realista:** 6-9 meses. Fase G (rewrite pra Stride) é bloqueante pra E/H.

## Stack-alvo (libs modernas adotadas)

Princípio: **tecnologias de ponta, nada legado, terceirizar máxima complexidade.**

| Camada | Tech alvo | Substitui |
|---|---|---|
| Runtime | .NET 10 (já), `ServerGC`+`TieredPGO`+`AllowUnsafeBlocks` (já) | — |
| Engine | **Stride 4.2+** | FNA + render custom + Gui custom |
| ECS | **Stride ECS nativo** (ou **Arch ECS** se quiser alternativa performática) | `ComponentManager` custom |
| Renderer | Stride Forward+ / deferred com PBR | `WorldRenderer` + `ChunkRenderer` custom |
| Physics | Stride Bullet integration (ou **BepuPhysics v2** C# puro se preferir) | Physics custom |
| Audio | Stride audio stack (ou **FMOD** via wrapper se querer pro-audio) | `SoundManager` custom |
| Logging | **ZLogger** (zero-alloc, async) | `Console.WriteLine`, LogWriter |
| DI | **Microsoft.Extensions.DependencyInjection** + Stride services | Singletons/globals |
| Messaging | **MessagePipe** (in-process pub/sub zero-alloc) | Eventos C# crus |
| Serialization | **MemoryPack** (2-4× mais rápido que MessagePack, 10× que JSON) | Newtonsoft.Json |
| Config | **Tomlyn** ou **Microsoft.Extensions.Configuration** | `GameSettings` custom |
| Random | **Xoshiro256+** ou `System.Random.Shared` | `ThreadSafeRandom` custom |
| Noise | **FastNoiseLite** (já) | — |
| Pathfinding | A* próprio (feito) + possível **Roy-T.AStar** / **NavMeshPlus** pra nav 3D | Manter A* custom |
| Testing | **xUnit v3** + **BenchmarkDotNet** pra perf tests | Ausente hoje |
| Build | `dotnet` SDK-style (já) + **Nuke Build** ou `dotnet build` puro | PostBuildEvent custom |
| Telemetria | **OpenTelemetry** + existing `PerformanceMonitor` | Só `PerformanceMonitor` |

Qualquer adição acima é opcional por camada — decide-se ao chegar na Fase correspondente. O compromisso v4 é: **nunca manter lib legada quando tem substituta moderna direta.**

---

## O que JÁ foi feito (referência — não refazer)

| Item original | Status | Onde |
|---|---|---|
| Fase 0.1/0.2/0.6 (TFM, FNA, SDK csproj) | ✅ | `DwarfCorpFNA.csproj` → `net10.0-windows`, FNA 26.04, Vulkan ativo |
| Fase 0.4 (remover SharpRaven) | ✅ | `CrashBreadcrumbs.cs` com enum próprio |
| Fase 0.5 (LibNoise → FastNoiseLite) | ✅ | Shim em `LibNoise/Perlin.cs`, `FastRidgedMultifractal.cs` |
| Item 24 (crash MojoShader) | ✅ (stopgap + fix real pelo FNA novo) | `Shader.TryGetSharedIconShader` |
| Fase 1.2 (A* pooled) | ✅ | `[ThreadStatic]` em `AStarPlanner.cs` |
| Fase 1.3 (pools + Mutex→lock) | ✅ | Double-buffer em `ComponentManager`, scratch drain em `WaterManager` |
| GpuLock (novo — serializa Vulkan) | ✅ | `DwarfGame.cs:37` envolvendo Draw/ResetBuffer/SharedTexture/Flush |
| Fase 1.1 (parallel chunk rebuild) | ❌ **REVERTIDO** (heap corruption AMD) | precisa refactor correto — ver Fase B |

---

## Fases v4 (ordem de execução recomendada)

### Fase A — Discovery: Compute Shaders via Vulkan ✅ **CONCLUÍDA (2026-04-19)**

**Verdict:** compute **NÃO disponível** em FNA. Ver [docs/perf_compute_discovery.md](docs/perf_compute_discovery.md). Ativa Fase G.

### Fase G — Rewrite para Stride Engine 4.2+ 🚧 **BLOQUEANTE pra E/H**

**Escopo:** 6-9 meses. Rewrite arquitetural. DwarfCorp vira um projeto Stride; só o core único (voxel + IA + colony sim) é portado, todo o resto vem do engine.

**G.0 — Avaliação inicial (1 semana)**
- Instalar Stride 4.2+ SDK, rodar tutoriais mínimos pra internalizar idiomas do engine (Entity, Component, Scene, Service, Script).
- POC: um chunk voxel 32³ renderizando via Stride (sem LOD, sem lighting — só prova que mesh upload + camera + draw funciona).
- Validar workflow LLM-only: criar entidade/material 100% em C# sem tocar no Game Studio editor.
- Decisão intermediária: se POC falhar, fallback pra MonoGame (ver v3 no histórico).

**G.1 — Setup do projeto Stride**
- Novo repo ou branch `stride-rewrite`. Projeto `.sdpkg` gerado via CLI `stride-cli new` ou template.
- Estrutura: `DwarfCorp.Game/` (projeto Stride), `DwarfCorp.Core/` (logic pura, sem render — AI, colony, world gen), `DwarfCorp.Voxel/` (chunking/meshing).
- Target: .NET 10, `ServerGC`+`TieredPGO` herdado.
- Adoção inicial de libs modernas do stack-alvo: **ZLogger**, **MemoryPack**, **MessagePipe**, **Microsoft.Extensions.DependencyInjection**.

**G.2 — Port do core logic (sem render)**
- Extrair puro: `Dwarf*Act`, `CreatureAI`, `AStarPlanner` (já pooled), `WaterManager` sim, `ComponentManager` → mapear pra ECS do Stride, colony jobs/stockpiles/economy.
- `World/Generation/*` continua idêntico (world gen é math puro + FastNoiseLite).
- Saves: nova implementação via **MemoryPack** — v4 não tenta compat com saves antigos (breaking change explícita, clean slate).
- Testes: xUnit v3 cobrindo geração + IA + pathfinding *sem dependência de render*. Isso é o firewall que garante que quebra de render não derruba gameplay.

**G.3 — Voxel + chunking + meshing em Stride**
- `VoxelChunk` mantém layout interno (byte arrays SoA).
- `GeometryBuilder`: já inclui greedy mesh + SIMD AVX2 (Fase B do plano v2 — pode ser feita junto ou antes).
- Upload pra GPU via API nativa do Stride (`Buffer.New<T>`, `Mesh`, `ModelComponent`) — já é thread-safe no Stride, `GpuLock` deixa de existir.
- Meshing paralelo trivial: workers geram `VertexData[]`, main thread consome queue e faz `Mesh.Draw` — o que Fase B.1 tentou no FNA, aqui é idiomático.

**G.4 — Camera + input + HUD mínimo**
- Camera orbital reescrita como `SyncScript` do Stride.
- Input via `Stride.Input.InputManager`.
- HUD inicial usando UI system do Stride **via código puro** (`StackPanel`, `TextBlock` em C#, sem `.sdpag` do editor).

**G.5 — Port de entidades e componentes**
- Dwarfs, animais, props viram `Entity` com componentes Stride.
- `TransformComponent` do Stride substitui `Body.GlobalTransform` custom (SoA embutido).
- Animation via Stride (`AnimationComponent`) se conseguirmos importar os assets atuais; caso contrário, placeholder + port incremental.

**G.6 — Audio + particles + UI completa**
- Áudio: Stride native (`AudioListenerComponent` + `AudioEmitterComponent`).
- Partículas: Stride VFX (`ParticleSystemComponent`) — Fase E.1 vira configuração desse sistema, não implementação from-scratch.
- UI completa (menus, save/load, stockpile panels, etc.) em código.

**G.7 — Baseline pós-migração**
- Cena stress canônica (mapa grande + 30 dwarfs), 10 min, profiler do Stride + `PerformanceMonitor` portado.
- Export `baseline_v4.csv`.
- Aceite: feature parity com o jogo atual + FPS ≥ atual, Gen2 estável, 30 min sem crash.

**Arquivos críticos:** projeto Stride novo, `DwarfCorp.Core/` extraído, `DwarfCorp.Voxel/` portado. O código FNA atual permanece no branch `legacy-fna` como referência e possível fallback durante o rewrite.

**Riscos e mitigação:**
- **Risco:** LLM-only workflow falhar em alguma feature do Stride que exija editor. → Mitigação: G.0 é gate; se bater muro no POC, fallback pra MonoGame (v3).
- **Risco:** port do voxel pipeline bater em perf ruim no Stride. → Mitigação: G.0 já prova o caso; se perf regressar >2×, avaliar se vale continuar.
- **Risco:** assets (textures, fonts, sounds) incompatíveis com pipeline do Stride. → Mitigação: converter via scripts; a maioria é PNG/WAV/TTF, formatos universais.
- **Risco:** saves antigos perdidos. → Decisão explícita: **v4 não preserva saves v3**. Jogadores começam fresh. Se precisar compat, port-in posterior.

### Estado do jogo atual durante o rewrite

- Branch `legacy-fna` congelado (só bugfixes críticos que vazarem pra produção).
- Main branch migra pra Stride; releases saem do branch novo quando atingir parity.
- Fases B, C, D (CPU-side, plataforma-agnostic) continuam fazendo sentido executar **antes** de G, porque o código migra junto e já estará paralelo/pooled/async.

### Fase B — Chunk Rebuild Refactor **(mesh-gen paralelo + GPU upload serial + greedy meshing)**

Recurso crítico. Integra 3 ganhos num só refactor arquitetural, porque tudo toca `GeometryBuilder`/`ChunkManager`/`GeometricPrimitive`.

**B.1 — Separar mesh-gen de GPU upload**

- `GeometryBuilder.Rebuild*` passa a retornar um `MeshData` (POCO: `VertexPositionColor[]`, `ushort[]`) — **zero contato com `GraphicsDevice`**.
- Novo `MeshUploadQueue`: `ConcurrentQueue<(VoxelChunk, MeshData)>`. Workers enfileiram; um **single GPU-upload step** no início do `Draw()` (sob `DwarfGame.GpuLock`) consome até N itens e aplica `SetData` nos `DynamicVertexBuffer` existentes.
- `ChunkManager.RebuildVoxelsThread` vira pool de workers que só fazem mesh-gen (CPU) + enfileiram.

**B.2 — Greedy meshing**

- Em `GenerateSliceGeometry` ([GeometryBuilder.cs:85](DwarfCorp/World/Voxels/ChunkBuilder/GeometryBuilder.cs:85)): substituir iteração voxel-a-voxel por algoritmo Mikola/Tantan (scan 2D por face-direction × eixo, agrupando quads coplanares adjacentes do mesmo tipo/tile/luz).
- Preserva atributos por-vértice existentes (AO, grass, decal) via chave composta (`tile | grass | decal | ambient`) na máscara greedy.
- Queda esperada: 10-50× menos vértices/tri por chunk.

**B.3 — SIMD no scan de visibilidade**

- .NET 10 + `<AllowUnsafeBlocks>` destravam `System.Runtime.Intrinsics.X86.Avx2`/`Sse2`.
- `VoxelData.Types` já é `byte[]` linear ([VoxelData.cs:16](DwarfCorp/World/Voxels/VoxelData.cs:16)). Scan de face-visibility vira `Vector256<byte>` XOR com neighbor-slice → `MoveMask` → bitmask de faces expostas, batching 32 voxels por iteração.
- Fallback scalar pra hardware sem AVX2 (`Avx2.IsSupported`).

**B.4 — Instrumentação**

- `PerformanceMonitor.PushFrame("ChunkMeshGen")` / `PushFrame("ChunkGpuUpload")` separados pra medir cada metade independentemente.
- `CrashBreadcrumbs` nas transições worker→queue→upload.

**Arquivos críticos:** `GeometryBuilder.cs`, `ChunkManager.cs` (l.148+), `VoxelChunk.cs` (l.79), `GeometricPrimitive.cs`, novo `ChunkMeshJobs.cs` + `MeshUploadQueue.cs`.

### Fase C — Pathfinding async + heuristic cache

Hoje: `AStarPlanner.Path()` roda síncrono dentro do worker de `PlanService`, com `[ThreadStatic]` pools (bom!) — mas a API de saída ainda é polling.

**C.1 — API async/TCS**

- `PlanService.Enqueue(request)` passa a devolver `Task<List<MoveAction>>` via `TaskCompletionSource<>`.
- AI chama `var path = await world.Pathfinder.PlanAsync(start, goal, ct)`; idle behavior segue durante o await.
- Cancellation token propaga para o worker (checa em cada pop do open set).

**C.2 — Spatial heuristic cache**

- `Dictionary<VoxelHandle, float>` per-thread (já no `[ThreadStatic]`) de h-values recentes, invalidado por chunk rebuild (hook em `VoxelChunk.InvalidateMesh` já existente).
- Landmarks ALT-pattern (3-5 voxels distantes, pré-computados por bioma) pra tighter heuristic quando destino é longe.

**C.3 — FindRootBodies / spatial queries**

- `ComponentManager.FindRootBodiesInsideScreenRectangle` ([PlayState-Input.cs:92](DwarfCorp/GameStates/Play/PlayState-Input.cs:92)): `HashSet`/`ToList` alocados por chamada → `ArrayPool<GameComponent>` + struct enumerator.
- Começa adoção de `ArrayPool<T>.Shared` no projeto (hoje zero uso).

**Arquivos críticos:** `AStarPlanner.cs`, `PlanService.cs`, `ComponentManager.cs`, `PlayState-Input.cs`, consumidores (`CreatureAI`, `Dwarf*Act`).

### Fase D — Component Update Paralelo

Hoje: `ComponentManager.Update` ([ComponentManager.cs:241](DwarfCorp/Components/Systems/ComponentManager.cs:241)) é `foreach` single-thread com ~1000+ entidades.

- Novo `Tools/Threading/JobScheduler.cs` (thin wrapper sobre `Parallel.ForEach` + partitioner custom por hash de chunk-coord — entidades na mesma coord 3D ficam no mesmo worker = zero contenção entre buckets).
- Commits (spawn/despawn, adição/remoção de componentes) em `ConcurrentQueue` drenada no fim do frame — mantém determinismo.
- `TransformUpdater` já tem worker pool (bom); revalidar sync final.

**Riscos:** componentes que escrevem em estado compartilhado (lights, sound, UI) — auditar cada `IUpdateableComponent.Update` e anotar thread-safety requirements. Pode ser necessário segregar "parallel-safe" vs "main-thread-only".

**Arquivos críticos:** `ComponentManager.cs`, `JobScheduler.cs` (novo), auditoria de todos os `Update()` em `Components/`.

### Fase E — GPU pesada (com compute via Stride SDSL, pós-Fase G)

Com Stride, compute shaders são first-class via SDSL. Execução mescla compute onde compensa + VS+instancing onde compute é overkill:

- **E.1** Particles em **compute shader**: `StructuredBuffer<Particle>` atualizado por compute dispatch (physics, collisions simples, lifetime decay). VS lê buffer e renderiza. 10-100× mais partículas viáveis vs CPU.
  - Fallback CPU mantido pra hardware antigo (flag `GameSettings.ComputeParticles`).
- **E.2** Light culling via **compute** (Forward+ / Clustered Forward): compute dispatch constrói lista de luzes por tile screen-space, fragment shader lê só as luzes do tile. Escalável pra 256+ luzes dinâmicas.
- **E.3** Greedy meshing permanece na **Fase B.2 (CPU SIMD)** — meshing em compute é arquitetura complexa com pouco ganho vs AVX2; custo-benefício favorece CPU.
- **E.4** Liquid instancing: `DrawInstancedPrimitives` agrupado por tipo.
- **E.5** Hi-Z occlusion culling via **compute** (GPU-side occlusion query com depth pyramid). Alternativa CPU (MSOC) fica como plan B caso compute path seja instável.
- **E.6** Mega-mesh batching: 1 ring `VertexBuffer` compartilhado + `baseVertex` offset por chunk → cut ~50% draw-call overhead.

### Fase H — Shading moderno (PBR + shadows + SSAO + post-FX) 🎨 via Stride built-ins

**Boa notícia:** todos os itens H.1-H.5 **já vêm prontos no Stride**. Fase H vira ativar + configurar + tunar, não implementar do zero. Escopo cai de 2-3 meses (MonoGame-path v3) pra ~3-4 semanas de integração.

**H.1 — PBR materials**
- Stride tem `MaterialAsset` com slots albedo/normal/metallic/roughness/AO/emissive nativos. IBL (image-based lighting) built-in.
- Trabalho: converter texturas DwarfCorp existentes pra PBR (muitas ficam OK com defaults metallic=0/roughness=0.8) + criar materials programaticamente por voxel type.

**H.2 — Cascaded Shadow Maps**
- `LightComponent` + `LightShadowMapDirectional` já dá CSM com PCF soft shadows.
- Trabalho: configurar cascades (4 é padrão razoável), tunar bias/slope-scale, habilitar pra sol direcional + point lights relevantes.

**H.3 — SSAO / GTAO**
- Stride tem `PostProcessingEffects.AmbientOcclusion` (SSAO built-in). GTAO não é nativo mas há shader SDSL público portável.
- Trabalho: ativar, tunar radius/intensity; opcional substituir por GTAO community shader.

**H.4 — Pipeline (já é forward+)**
- Stride já roda forward+ clustered por default. **Zero trabalho aqui** — o que era risk alto no v3 (refatorar pipeline manualmente) vira não-evento no v4.

**H.5 — Post-processing stack**
- `PostProcessingEffects`: tonemapping (ACES), bloom, DOF, film grain, chromatic aberration, color grading — tudo built-in.
- Trabalho: criar preset razoável pra DwarfCorp (bloom discreto, ACES tonemap, pequeno SSAO, sem DOF).

**H.6 — Features bonus que Stride desbloqueia de graça**
- Volumetric lighting / god rays
- Screen-space reflections
- Temporal anti-aliasing (TAA)
- Dynamic skybox com Atmospheric Scattering
- Habilita se couber no budget de perf.

**Arquivos novos:** `DwarfCorp.Game/Rendering/Presets.cs` (curva de post-FX), `DwarfCorp.Game/Materials/VoxelMaterials.cs` (factory pra material por voxel type).
**Risco:** baixo — tudo é configuração sobre sistemas do engine. Gate por `GameSettings.GraphicsPreset = Low|Medium|High|Ultra`.

**Arquivos críticos:** `ParticleEmitter.cs`, `WorldRenderer.cs` (l.243-265), `LiquidPrimitive.cs`, `VoxelChunk.cs`, novos shaders em `Content/Shaders/`.

### Fase F — Estrutural (dívida de longo prazo)

Opcional; só entra se profile após B/C/D/E ainda mostrar gargalos.

- **F.1** `DynamicBVH` 2D com Morton codes substituindo grid-de-chunks em `ComponentManager.EnumerateIntersectingRootEntitiesLoose` — O(n) → O(log n).
- **F.2** Water tile-partitioned — `WaterManager` particiona grid XZ, workers atualizam tiles em paralelo, sync só nas bordas.
- **F.3** `Body.cs` Transform SoA — `Vector3[] Positions`, `Quaternion[] Rotations` externos ao `TransformModule`; permite SIMD batch e melhora cache. (Cuidado: API pública de `Body` quebra — escopo grande.)

---

## Ordem / Rollout (cada item = 1 PR isolado, atrás de profiler)

| # | Fase | Risco | ROI | Depende de |
|---|---|---|---|---|
| 1 | A — Compute discovery | ✅ Concluída 2026-04-19 | — | — |
| 2 | C.3 — ArrayPool em FindRootBodies | Baixo | Médio | — |
| 3 | B.1 — Mesh-gen/upload split | Médio | Muito alto | — |
| 4 | B.2 — Greedy meshing | Médio | Muito alto | B.1 |
| 5 | B.3 — SIMD scan | Baixo | Alto | B.2 |
| 6 | C.1 — Pathfinding async | Médio | Alto | — |
| 7 | C.2 — Heuristic cache | Baixo | Médio | C.1 |
| 8 | D — Component update paralelo | Alto | Muito alto | — |
| **9** | **G.0 — POC Stride (gate LLM-workflow)** | **Médio** | **Valida caminho** | — |
| 10 | **G.1-G.7 — Rewrite pra Stride** | **Muito alto** | **Destrava tudo** | G.0 |
| 11 | E.4 — Liquid instancing (nativo Stride) | Baixo | Médio | G |
| 12 | E.1 — Particles via Stride VFX | Baixo (config) | Alto | G |
| 13 | E.6 — Mega-mesh batching | Médio | Alto | G + B.1 |
| 14 | E.2 — Light culling (Stride clustered) | Baixo (config) | Muito alto | G |
| 15 | E.5 — Hi-Z occlusion compute (SDSL) | Médio | Alto | G + B.1 |
| 16 | H.1 — PBR materials | Médio | Visual alto | G |
| 17 | H.2 — Cascaded shadow maps | Baixo (config) | Visual alto | G |
| 18 | H.3 — SSAO / GTAO | Baixo (config) | Visual médio | G |
| 19 | H.5 — Post-processing preset | Baixo | Visual médio | G |
| 20 | H.6 — Extras (volumetrics, TAA, SSR) | Médio | Visual extra | H.1-5 |
| 21 | F.x — Estrutural (BVH, SoA, water partition) | Alto | Médio | profile |

**Estratégia:** C.3 + B.* + C.1/2 + D rodam **antes** da Fase G (esses ganhos são platform-agnostic e o código migra com eles já aplicados). G é o divisor de águas; E/H vêm depois e ficam quase triviais porque Stride já provê quase tudo.

**Janela de execução típica (estimativa):**
- Mês 1: B.1-B.4, C.1-C.3, D (finaliza v4 CPU-side no FNA atual).
- Mês 2: G.0 POC + setup Stride + port inicial.
- Mês 3-6: G.1-G.7 rewrite incremental.
- Mês 7: E.* (major é config Stride).
- Mês 8: H.* (major é config/asset).
- Mês 9: F.x opcional + polish + release.

---

## Verificação (end-to-end)

1. **Baselines por marco:** `baseline_v2.csv` (estado atual FNA), `baseline_v3_postG.csv` (pós MonoGame), `baseline_v3_final.csv` (após E+H). Cada é re-run da cena stress canônica por 10 min, export via F11.
2. **Cena de stress canônica:** seed fixo + `GameSettings.Current.WorldSize = Large` + 30 dwarfs + trigger de combate (documentar em `docs/perf_bench.md`).
3. Após cada PR: re-export CSV, diff de `ComponentManager.Update`, `ChunkMeshGen`, `ChunkGpuUpload`, `ChunkRenderer.Render`, `ParticleManager.Update`, `WaterManager.UpdateWater`, frame p50/p95, `GC.CollectionCount(2)`.
4. Sessão 30 min sem crash + Gen2 estável = critério de aceite.
5. Validar compatibilidade de save antes/depois de cada fase (Fase D toca lifecycle de componentes → risco de quebrar serialização).
6. Em AMD Vulkan especificamente (onde o bug anterior apareceu): rodar B.1 com Vulkan validation layers ativas, 10 min com chunk edits constantes, verificar zero `HEAP_CORRUPTION`/`VkCommandPool` warnings.

## Utilities existentes a reusar (não reescrever)

- `PerformanceMonitor.PushFrame/PopFrame` ([PerformanceMonitor.cs:192](DwarfCorp/Tools/PerformanceMonitor.cs:192))
- `CrashBreadcrumbs.Push` para transições entre threads críticas
- `DwarfGame.DoLazyAction` ([DwarfGame.cs](DwarfCorp/DwarfGame.cs)) — callback na main thread (útil na Fase B.1 pra GPU upload)
- `DwarfGame.GpuLock` ([DwarfGame.cs:37](DwarfCorp/DwarfGame.cs:37)) — serialização Vulkan (obrigatório em toda `SetData`/`new VertexBuffer` fora do render loop)
- `FixedInstanceArray` ([FixedInstanceArray.cs:210](DwarfCorp/Graphics/Primitives/FixedInstanceArray.cs:210)) — já tem fallback instancing
- `[ThreadStatic]` pattern de `AStarPlanner` — modelo para C.3 e D
