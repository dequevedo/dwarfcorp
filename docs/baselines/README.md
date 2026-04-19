# DwarfCorp Performance Baselines

CSVs produced by running the canonical stress scene described in
[`../perf_bench.md`](../perf_bench.md) with `DWARFCORP_PERF_EXPORT` set
(or manually via F11). Each file here is a snapshot of one milestone.

## Filenames

Keep the naming tight so diffs are easy to find:

- `baseline_v5_fna.csv` — last FNA 26 Vulkan state, pre-migration
- `baseline_v5_monogame.csv` — right after the MonoGame port (M.1-M.5)
- `baseline_v5_cpu.csv` — after Fase B chunk rebuild work
- `baseline_v5_arch.csv` — after Fase L.4 Arch ECS migration

If you capture a mid-work snapshot for debugging, prefix it with your
initials and the date (`dq_20260420_lighting_spike.csv`) so it doesn't get
mistaken for a milestone baseline.

## Reading the files

See [`../perf_bench.md`](../perf_bench.md) under "What the CSV contains"
and "What to compare" for the format and the three numbers that matter.
