# Fase A — Compute Shader Discovery (FNA 26.04 / Vulkan)

**Data:** 2026-04-19
**Fase:** A do `PERF_PLAN_EXTREME.md`
**Pergunta:** O backend Vulkan adicionado na migração FNA 19.07 → 26.04 expõe compute shaders pra código C# do DwarfCorp?

## TL;DR

**NÃO.** FNA 26.04 mantém paridade estrita com API XNA4. Não há tipos `ComputeShader`, `StructuredBuffer`, `UnorderedAccessView`, nem `Dispatch*` na superfície C# — mesmo com backend Vulkan ativo. **Decisão:** migração pra MonoGame 3.8.x (Fase M do plano v5), que expõe compute shaders via backend DX11.

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

## Consequência pro plano

Fase M do plano v5: migrar FNA → **MonoGame 3.8.x (DX11 backend)**. MonoGame 3.8 adiciona `ComputeShader` na superfície C# quando usando backend DX11. Isso destrava:

- Compute para particles (evita CPU update per-frame).
- Compute para light culling (Forward+ / Clustered Forward).
- Compute para occlusion culling GPU-side (depth pyramid).
- Higher shader profiles (SM4/SM5) em vez de SM3.0 forçado pelo MojoShader.

Nota: visual final do DwarfCorp permanece **basic low-poly pixelated** (decisão do usuário). Compute fica como ferramenta disponível pra hot paths específicos (particles/culling/occlusion) se profile apontar gargalo — não como mandato.

## Alternativas consideradas e rejeitadas

1. **P/Invoke Vulkan direto** (bypass FNA3D pra falar com o `VkDevice` nativo).
   - Rejeitado: FNA3D não expõe o handle do device; reimplementar abriria conflito com o próprio driver FNA3D usando o mesmo queue/pool. Risco de reproduzir exatamente o `HEAP_CORRUPTION` que motivou o `GpuLock`.

2. **Trocar FNA por Silk.NET / Veldrid / OpenTK.**
   - Rejeitado inicialmente; Veldrid entrou como alternativa em [docs/engine_decision.md](engine_decision.md) e foi superado por MonoGame pelo workflow LLM-first e training data.

3. **Subir pro branch next do FNA** (se existir com compute).
   - Investigado: não há branch público do FNA com compute stage. Filosofia do mantenedor descarta adicionar.

## Ações derivadas

- [x] Confirmar verdict e documentar (este arquivo).
- [ ] Fase M do plano v5 — migrar pra MonoGame 3.8.x (DX11).
- [ ] Compute usado sob demanda em Fase E se profile mostrar gargalo GPU (deferida).
