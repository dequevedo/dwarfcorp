# Engine Decision — DwarfCorp (2026-04-19)

Decisão final: **MonoGame 3.8.x (DX11)** + stack composável de libs .NET modernas.

Este doc registra a análise comparativa feita pra chegar nessa decisão, pra referência futura quando alguém perguntar "por que não Stride/Unity/Godot?".

## Constraints (o que pesou)

1. **LLM-first workflow**: IA tem que conseguir fazer tudo sem abrir editor.
2. **DwarfCorp já existe** em FNA (fork moderno de XNA4), ~10+ anos de código.
3. **Rendering basic**: low-poly pixelated, sem PBR/shadows/SSAO fancy.
4. **Save compat**: não pode quebrar saves existentes.
5. **Performance**: ainda é prioridade máxima — CPU multithreading, GPU compute onde valer.
6. **Stack moderno**: nada legado onde existe substituto de ponta.

## Tabela comparativa

| Dimensão | FNA (stay) | MonoGame 3.8 | Stride 4.2 | Godot 4 + C# | Unity 6 | Flax | Veldrid + libs |
|---|---|---|---|---|---|---|---|
| **Linguagem** | C# puro | C# puro | C# puro | C# via binding (core C++) | C# via binding (core C++) | C# + C++ híbrido | C# puro |
| **Licença** | MS-PL | MIT | MIT | MIT | Proprietária + Runtime Fee histórico | Source-available | MIT |
| **Editor obrigatório?** | ❌ Inexistente | ❌ Inexistente | ⚠️ Opcional mas cultura assume | ⚠️ Culturalmente sim | ✅ Sim (4GB+ RAM) | ✅ Sim na prática | ❌ Inexistente |
| **LLM-first viável?** | ✅ Natural | ✅ Natural | ⚠️ Anti-idiomático | ⚠️ Anti-idiomático | ❌ Doloroso | ⚠️ Anti-idiomático | ✅ Natural |
| **Training data do LLM** | ✅ Grande (XNA4) | ✅ **Enorme** (20 anos XNA) | ❌ Pequeno (SDSL proprietária) | ⚠️ Médio (GDScript domina) | ✅ Enorme (mas Unity-specific) | ❌ Pequeno | ⚠️ Médio |
| **Compute shaders** | ❌ Não | ✅ Sim (DX11) | ✅ Sim (SDSL) | ✅ Sim (GLSL) | ✅ Sim (HLSL) | ✅ Sim | ✅ Sim |
| **PBR/CSM/SSAO prontos** | ❌ Não | ❌ Você escreve | ✅ Built-in | ✅ Built-in | ✅ Built-in (URP/HDRP) | ✅ Built-in | ❌ Você escreve |
| **ECS nativo** | ❌ Não | ❌ Não (integrar Arch) | ✅ Sim | ⚠️ Node-based (não ECS) | ⚠️ DOTS opcional (complexo) | ⚠️ Component-based | ❌ Integrar Arch/DefaultEcs |
| **Community/Support** | Pequeno (Ethan Lee) | Grande, ativo | Pequeno | ✅ Enorme | ✅ Enorme | Pequeno | Pequeno, manutenção lenta |
| **Esforço migração FNA→X** | 0 | **~1-2 semanas** mecânico | ~6-9 meses rewrite | ~6-9 meses rewrite | ~6-12 meses rewrite | ~6-9 meses rewrite | ~2-4 meses camada render |

## Track record (shipped games)

| Engine | Hits |
|---|---|
| **FNA** | FEZ, Rogue Legacy, Bastion, Transistor, Pyre, Stardew Valley (FNA port), Terraria (FNA port), Streets of Rage 4 |
| **MonoGame** | Celeste, Stardew Valley, Fez, Bastion, Towerfall, Axiom Verge, Salt & Sanctuary, Stacklands |
| **Stride** | Nenhum hit conhecido. Alguns indie open-source, demos |
| **Godot** | Brotato, Cruelty Squad, Halls of Torment, Dome Keeper, Cassette Beasts, Buckshot Roulette |
| **Unity** | Incontáveis (Hollow Knight, Cuphead, Hades, Cities Skylines, Rimworld, Cult of the Lamb) |
| **Flax** | Poucos projetos pequenos, nenhum hit comercial |
| **Veldrid** | Principalmente tooling (editores, visualizadores). Poucos jogos comerciais |

## Workflow LLM-first específico

| Critério | FNA | MonoGame | Stride | Godot | Unity | Flax | Veldrid |
|---|---|---|---|---|---|---|---|
| Tudo texto diff-ável | ✅ | ✅ | ⚠️ YAML mas ruidoso | ⚠️ .tscn texto mas grafos | ❌ Scenes/Prefabs bináveis | ⚠️ | ✅ |
| LLM escreve shader sem atrito | ✅ HLSL | ✅ HLSL | ❌ SDSL pouco documentado | ⚠️ GDScript-shaders | ⚠️ ShaderLab/URP graphs dominam | ⚠️ | ✅ HLSL/SPIR-V |
| Código idiomático LLM-writable | ✅ | ✅ | ⚠️ APIs engine específicas | ⚠️ Node-paradigm | ⚠️ Inspector refs quebram fluxo | ⚠️ | ✅ |
| Build headless CLI | ✅ `dotnet build` | ✅ `dotnet build` | ⚠️ Asset processor chama editor | ⚠️ `godot --headless` mas pouco testado | ❌ `Unity -batchmode` lento | ⚠️ | ✅ |
| Diff de PR honesto | ✅ Perfeito | ✅ Perfeito | ⚠️ YAML às vezes ruidoso | ⚠️ | ❌ Ruim | ⚠️ | ✅ |

## Específico pra DwarfCorp (voxel colony sim)

| Critério | FNA | MonoGame | Stride | Godot | Unity | Flax | Veldrid |
|---|---|---|---|---|---|---|---|
| Perf voxel/meshing custom | ✅ | ✅ | ✅ | ⚠️ C#→C++ FFI em hot paths | ⚠️ Burst/DOTS ajuda mas complexo | ✅ | ✅ |
| Pathfinding 1000+ agentes | ✅ | ✅ | ✅ | ⚠️ Overhead Node API | ⚠️ Overhead GameObject | ✅ | ✅ |
| Fases B/C/D (CPU-side) transferem? | N/A | ✅ Direto | ⚠️ Mapear pra ECS | ⚠️ Reestruturar pra nodes | ⚠️ Reestruturar | ⚠️ | ✅ Direto |
| Saves custom preservados | ✅ | ✅ | ⚠️ Stride serializer opinativo | ⚠️ Godot Resource system | ⚠️ Unity serialization | ⚠️ | ✅ |
| % do código DwarfCorp reusado | 100% | **~95%** | ~30% | ~20% | ~20% | ~30% | ~70% |

## Por que MonoGame venceu

Pesando todos os critérios duros:

1. **Migração quase trivial** (1-2 semanas, port mecânico). Nenhuma outra opção chega perto disso mantendo modernidade.
2. **LLM-native culture** — MonoGame nunca teve editor, toda a documentação/tutorials assume código puro.
3. **HLSL training data** é massivo — LLM escreve HLSL com alta qualidade.
4. **Track record forte** — Celeste/Stardew/Fez/Bastion provam que MG escala.
5. **Compute shaders disponíveis** (DX11 backend) destravam Fase E do plano se precisar.
6. **DwarfCorp é low-poly pixelated** — PBR/shadows que Stride/Godot trazem "de graça" **não são valor pra este jogo**. Preço zero = ganho zero.
7. **Stack composável fecha os gaps**: Arch ECS + ImGui.NET + BepuPhysics (on-demand) + ZLogger + MessagePipe etc. preenchem tudo que o engine base não dá, e cada peça é swappable.

## Por que outras engines perderam

**Stride:** feature gráfica avançada não tem valor pra este jogo; SDSL mata training data; editor-centric culture atrita com LLM-first; track record preocupante.

**Godot:** C# é second-class (core C++), node paradigm anti-idiomático pra LLM, rewrite de 6-9 meses sem justificativa concreta.

**Unity:** 4GB editor obrigatório, scenes binárias, inspector refs quebram LLM workflow. Seus 10 anos de experiência ajudam mas o atrito diário inviabiliza.

**Flax:** risco de engine minoritário sem payoff gráfico único pra compensar.

**Veldrid:** meio-termo sem vantagem clara — você monta tudo igual MonoGame mas perde o ecossistema XNA.

**FNA (stay):** aceita gap permanente em compute. Só faz sentido se "extremo de performance" não precisar de compute.

## Stack composável adotado (complemento ao MonoGame)

Detalhes completos em `PERF_PLAN_EXTREME.md` seção "Stack-alvo". Resumo:

| Gap do MonoGame | Lib escolhida |
|---|---|
| ECS | **Arch** (archetype-based, zero-alloc, C# puro) |
| UI debug/dev | **ImGui.NET** (code-first, LLM-friendly) |
| UI de jogo | Manter `DwarfCorp/Gui/*` custom por ora |
| Physics | Manter custom (gravity + AABB); **BepuPhysics** sob demanda |
| Serialization (saves) | **Newtonsoft.Json 13.0.3** (mantido — compat obrigatória) |
| Logging | **ZLogger** (zero-alloc, async) |
| DI | **Microsoft.Extensions.DependencyInjection** |
| Messaging | **MessagePipe** (pub/sub zero-alloc) |
| Content | **MGCB** default; **Stb*/Assimp** on-demand |
| Font | **FontStashSharp** |
| Noise | **FastNoiseLite** (já adotado) |
| Benchmarks | **BenchmarkDotNet** |
| Testing | **xUnit v3** |
| SIMD | `System.Runtime.Intrinsics` + `System.Numerics` (built-in) |

## Caminho de reversão

Se MonoGame se mostrar inviável durante a Fase M:

1. Commit tag `legacy-fna-final` preserva o estado FNA 26.04.
2. `git revert` restaura.
3. Re-avaliar: Veldrid + libs (próxima escolha em preferência).

## Assinatura

Decisão tomada em conversa DwarfCorp maintainer × Claude Opus 4.7 (sessão 2026-04-19), com segunda opinião de LLM externa (Claude via outro contexto) reforçando MonoGame sobre Stride por LLM-workflow + training data + track record. Plan v5 em `PERF_PLAN_EXTREME.md`.
