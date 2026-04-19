# Fase A — Compute Shader Discovery (FNA 26.04 / Vulkan)

**Data:** 2026-04-19
**Fase:** A do `PERF_PLAN_EXTREME.md` v2
**Pergunta:** O backend Vulkan adicionado na migração FNA 19.07 → 26.04 expõe compute shaders pra código C# do DwarfCorp?

## TL;DR

**NÃO.** FNA 26.04 mantém paridade estrita com API XNA4. Não há tipos `ComputeShader`, `StructuredBuffer`, `UnorderedAccessView`, nem `Dispatch*` na superfície C# — mesmo com backend Vulkan ativo. **Decisão:** Fase E segue pelo caminho E.x′ (CPU + VS + instancing).

## Evidências

### 1. P/Invoke FNA3D — zero entry points de compute

`FNA/src/Graphics/FNA3D.cs` (1101 linhas) expõe apenas pipeline gráfico clássico:

- Draw: `FNA3D_DrawIndexedPrimitives`, `FNA3D_ApplyVertexBufferBindings`
- State: `FNA3D_SetBlendState`, `FNA3D_SetDepthStencilState`, `FNA3D_SetRenderTargets`
- Resources: `FNA3D_CreateTexture2D/3D/Cube`, `FNA3D_SetVertexBufferData`, `FNA3D_CreateEffect`
- Sem `FNA3D_DispatchCompute`, sem `FNA3D_CreateComputePipeline`, sem `FNA3D_CreateStorageBuffer`

Grep por `[Cc]ompute|[Dd]ispatch|UAV|[Ss]tructured` em `FNA3D.cs`: **zero hits**.

### 2. Superfície C# de Graphics — só VS/PS

`FNA/src/Graphics/` não tem `ComputeShader.cs`, `ComputePass.cs`, `ComputeEffect.cs`. O enum `GraphicsProfile` mantém apenas `Reach` e `HiDef` (XNA4, pré-compute).

### 3. Effect system — MojoShader limitado a 2 stages

`FNA/src/Graphics/Effect/Effect.cs` referencia apenas:

```csharp
MOJOSHADER_SYMTYPE_PIXELSHADER
MOJOSHADER_SYMTYPE_VERTEXSHADER
```

Nenhum `GEOMETRYSHADER`, `HULLSHADER`, `DOMAINSHADER`, `COMPUTESHADER`. O Effect system passa `.fxb` compilado pro MojoShader, que sabe falar apenas VS/PS em SM ≤3.0.

### 4. Por quê

FNA é **reimplementação de XNA4**. Filosofia de projeto do Ethan Lee: "não adicione API que XNA4 não tinha". O backend Vulkan substituiu o antigo OpenGL/D3D11 por performance/portabilidade, mas **não expande a API C#**. Vulkan é usado internamente pra acelerar as mesmas operações XNA4.

## Consequências pro plano v2

| Fase | Caminho escolhido |
|---|---|
| E.1 (Particles) | **E.1′** — via VS analítico: instance buffer com `(p0, v0, t0, life, seed)`, VS calcula `pos = p0 + v0·Δt + ½g·Δt²`. CPU só spawn/despawn. |
| E.2 (Light culling) | **E.2′** — tile grid 16×16 CPU-side, texture 2D de índices, fragment shader lê só luzes do tile do pixel atual. |
| E.3 (Greedy mesh) | Fica na **Fase B.2** (CPU, paralelo, com SIMD AVX2). GPU greedy não é opção. |
| E.4 (Liquid instancing) | **Disponível** — `DrawInstancedPrimitives` existe na API XNA4/FNA. |
| E.5 (Hi-Z occlusion) | **Software-only** — port de Masked Software Occlusion Culling (Intel, MIT) rasterizando depth low-res na CPU. |

## Alternativas consideradas e rejeitadas

1. **P/Invoke Vulkan direto** (bypass FNA3D pra falar com o `VkDevice` nativo).
   - Rejeitado: FNA3D não expõe o handle do device; reimplementar abriria conflito com o próprio driver FNA3D usando o mesmo queue/pool. Risco de reproduzir exatamente o `HEAP_CORRUPTION` que motivou o `GpuLock`.

2. **Trocar FNA por Silk.NET / Veldrid / OpenTK.**
   - Rejeitado: reescrever toda a camada de render do DwarfCorp (WorldRenderer, Primitives, SpriteBatch, Effect) é escopo incompatível com "melhorar performance". Fica fora do plano.

3. **Subir pro branch next do FNA** (se existir com compute).
   - Investigado: não há branch público do FNA com compute stage. Filosofia do mantenedor descarta adicionar.

## Ações derivadas

- [x] Confirmar verdict e documentar (este arquivo).
- [ ] Atualizar `PERF_PLAN_EXTREME.md` Fase E pra marcar E.1/E.2/E.3 como **descartados** e elevar E.1′/E.2′/E.4′/E.5 pra execução.
- [ ] Atualizar `TODO_LIST.md` item 26 pra "Concluído" apontando pra este doc.
