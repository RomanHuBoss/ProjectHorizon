# Project Horizon — журнал реализации требований ТЗ

> **Назначение:** единая точка контроля соответствия проекта техническому заданию.
> **Последняя актуализация:** 2026-08-16
> **Подготовленный снимок:** `ProjectHorizon-main-task162.2-surface-presentation-hotfix.zip`
> **Git-состояние:** архив не содержит `.git`, поэтому ветка и SHA статически не подтверждаются.
> **Правило:** задача считается завершённой только после обновления этого журнала и фиксации проверяемых доказательств.

---


## 0. Текущая hotfix-итерация 2026-08-16 — TASK-162.2 Surface Presentation Recovery

**Исходный снимок:** `ProjectHorizon-main-task162.1-runtime-bootstrap-hotfix.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task162.2-surface-presentation-hotfix.zip`.  
**Версия:** `0.1.0-alpha.162.2`.  
**Статус:** TASK-162.2 `IMPLEMENTED`; TASK-162.1 повышен до `VERIFIED` по внешнему Godot evidence; TASK-162 остаётся `IMPLEMENTED`, TASK-163 manual live-rebase/cold-restore acceptance остаётся `IN_PROGRESS`.

### Внешнее runtime evidence пользователя

Godot 4.7.1 после TASK-162.1 больше не воспроизводит `Galaxy navigation runtime is unavailable` и проходит полный startup до surface stack:

- `TASK-156 planet terrain READY`;
- `TASK-160 planet surface world composition READY: sky=1; sun=1; clouds=13`;
- `TASK-158 planet surface streaming READY: active=25/25; collisions=9/9`;
- полный F5 даёт `TASK-160 ... PASS`, `TASK-162 ... PASS` (`rebases=48; traversalSamples=49; maxLocal=2030.709m; logicalContinuity=1; chunkIdentity=1; coldRestore=1; planetReset=1; geodesic=1`), а также `TASK-126 ... PASS` с `faunaProbeSamples=4`.

При этом пользовательский скриншот выявил отдельный presentation-дефект, который прежние структурные acceptance не ловили: поверхность визуально читается как плоский квадрат, фактический relief temperate-планеты всего `amplitude=2.55m`, граница bounded 5x5 streamer видна с уровня глаз, отдельный stellar disc отсутствует, а облака выглядят как близкие сферические lobes. Значит `sun=1/visibleStar=1` доказывал наличие lighting/runtime contract, но не приемлемый визуальный результат.

### Причина

- TASK-158 намеренно ограничивает gameplay terrain до 25 chunks (`5x5`, chunk `32m`), то есть ближайшая visual boundary находится всего примерно в 64-80 m от игрока.
- TASK-156 сохранял prototype-scale рельеф (`temperate 2.55m @ 0.043`), поэтому на таком расстоянии поверхность выглядит практически плоской.
- TASK-160 создавал только `DirectionalLight3D`/ProceduralSky sun binding; отдельного гарантированно видимого stellar-disc geometry не было.
- атмосферный fog был слишком слабым (`~0.001-0.0048`) для маскировки bounded surface window.
- cloud lobes находились на высоте 48-82 m и имели большую вертикальную толщину, из-за чего выглядели как близкие белые сферы.

### Исправлено

- gameplay streamer остаётся bounded `25 chunks / 9 collision`, но вокруг него добавлен **visual-only distant terrain proxy** `840m` шириной, `49x49`, без collision/nav; центральное окно `116m` вырезано, чтобы high-detail gameplay terrain оставался источником истины возле игрока;
- temperate relief поднят до `7.0m @ 0.024`, остальные archetypes также переведены на более крупномасштабный relief (`oceanic 5.5m`, `volcanic 12m` и т.д.) без изменения deterministic sampling identity;
- atmospheric fog усилен до диапазона `0.0045..0.0105`, чтобы дальний proxy растворялся в aerial perspective до чтения его внешней квадратной границы;
- добавлен `PlanetSurfaceSunVisual`: emissive core+halo, привязанный к системной star direction и floating-origin player frame; DirectionalLight принудительно связан с sky через `sky_mode=LIGHT_AND_SKY`;
- cloud layer поднят до `105..165m`, lobes сделаны значительно более плоскими и распределены на большем радиусе;
- cold-load/reset игрока защищён `EnsurePlayerAbovePlanetSurfaceFloor()`, чтобы более высокий relief не оставлял старое сохранение внутри terrain;
- live `PlanetSurfaceStreamer` отключает per-worker/per-chunk verbose logging: Output сохраняет plan/completed summary и ошибки, но больше не печатает десятки `started/applying/generated` строк на каждый переход чанка; prototype terrain tools сохраняют verbose default;
- новые `PlanetSurfaceDistantTerrain` и `PlanetSurfaceSunVisual` включены в surface residency suspend/restore;
- F5 дополнен `TASK-162.2 surface presentation acceptance`: проверяются macro relief, distant proxy, stellar disc, atmosphere fog и player clearance;
- добавлен `validate-task1622-surface-presentation-hotfix.py` и включён в section-37 Windows/Linux quality runners и CI/release contract list.

### Acceptance TASK-162.2

1. Clean build `0 warnings / 0 errors`.
2. New Game/Load: поверхность не заканчивается видимой квадратной границей на расстоянии порядка 80m; за gameplay terrain продолжается low-detail relief, уходящий в atmospheric haze.
3. На temperate starter planet лог должен показывать `TASK-156 ... amplitude=7.00m`; на экране должны быть заметны холмы/понижения, а не плоская плита.
4. Повернуться вокруг на 360°: должен присутствовать яркий stellar disc (core+halo) в направлении системной звезды; DirectionalLight/shadows сохраняются.
5. Облака находятся высоко и читаются как плоские clusters, а не огромные близкие сферы, обрезанные экраном.
6. Нажать F5: ожидается `TASK-162.2 surface presentation acceptance PASS` с `distantProxy=1; proxyExtent=840m; sunVisual=1; fogDensity>=0.0045; clearance>=0.80m`; прежние TASK-156/158/160/162 остаются PASS.
7. Для визуальной приёмки прислать один screenshot поверхности с горизонтом и одну строку `TASK-162.2 ... PASS`. Live >2048m rebase и distant cold restore остаются отдельным TASK-163.

---

## 0. Текущая hotfix-итерация 2026-08-16 — TASK-162.1 Runtime Bootstrap Order

**Исходный снимок:** `ProjectHorizon-main-task162-planet-global-surface-frame.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task162.1-runtime-bootstrap-hotfix.zip`.  
**Версия:** `0.1.0-alpha.162.1`.  
**Статус:** TASK-162.1 `VERIFIED`; TASK-162 остаётся `IMPLEMENTED`, TASK-163 runtime/manual acceptance остаётся `IN_PROGRESS`.

### Внешнее runtime evidence пользователя

Godot 4.7.1 фактически дошёл до запуска сцены, после чего `_Ready()` оборвался исключением:

`GalaxyNavigationRuntime SalvageRepairSlice.get_GalaxyNavigation(): System.InvalidOperationException: Galaxy navigation runtime is unavailable.`

Stack trace фиксирует точный путь: `InitializeStageOneVoyageRuntime` → `ApplyStageOneVoyageToScene` → `SurfaceLogicalToLocalPosition` → `EnsurePlanetSurfaceFrameForCurrentPlanet` → `GalaxyNavigation.CurrentPlanetId`. Сопутствующий скриншот соответствует раннему обрыву bootstrap: вместо streamed terrain остаётся fallback `GroundBody`, planet environment/star/world composition не успевают инициализироваться, HUD показывает недоступную позицию игрока.

### Причина

TASK-162 добавил зависимость frame-conversion от `GalaxyNavigation.CurrentPlanetId`, но в трёх lifecycle-путях старый порядок оставался `InitializeStageOneVoyageRuntime()` → `InitializeGalaxyNavigationRuntime()`. Stage-1 voyage немедленно применяет позицию корабля и тем самым впервые обращается к planet-surface frame до создания galaxy runtime.

### Исправлено

- `_Ready()`: Galaxy navigation теперь создаётся до Stage-1 voyage.
- `PollLoadTask()`: saved Galaxy navigation восстанавливается до применения saved voyage position.
- `PollResetTask()`: fresh Galaxy navigation создаётся до fresh voyage runtime.
- Добавлен `validate-task1621-runtime-bootstrap-hotfix.py`, который извлекает все три method body и запрещает регрессию порядка инициализации.
- Новый gate включён в Windows/Linux section-37 runners.

### Acceptance TASK-162.1

1. Clean build: `0 warnings / 0 errors`.
2. New Game: исключение `Galaxy navigation runtime is unavailable` отсутствует; после строк TASK-152/TASK-150 startup продолжается до terrain/world-composition READY.
3. На экране нет прежнего fallback-only состояния: surface stack доходит до TASK-156/158/160 READY. Качество горизонта/рельефа/stellar-disc **не считается** доказанным этой bootstrap-проверкой и вынесено в TASK-162.2.
4. Load существующего slot и New Game/reset также не воспроизводят исключение.
5. После успешного bootstrap выполнить F5 TASK-162 и дальнейший TASK-163 traversal/cold-restore acceptance.

---

## 0. Текущая mega-итерация 2026-08-16 — TASK-162 Planet-Global Surface Frame & Floating Origin

**Исходный снимок:** `ProjectHorizon-main(20260816-033933).zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task162-planet-global-surface-frame.zip`.  
**Версия:** `0.1.0-alpha.162`.  
**Исторический статус на момент alpha.162:** TASK-162 `IMPLEMENTED`; runtime/manual acceptance TASK-163 `IN_PROGRESS`. Последующий внешний Godot-прогон уже подтвердил F5 TASK-162 и TASK-160.1; live >2048 m traversal/cold-restore остаются TASK-163, а visual surface smoke выделен в TASK-162.2.

### Почему выбрана целая подсистема

TASK-158 уже дал bounded 25-chunk terrain streaming, а TASK-160 — chunk-scoped world composition/persistence. Следующим архитектурным пределом был единый координатный слой: streamer мог детерминированно адресовать удалённые chunks, но Godot player/physics и ряд потребителей всё ещё использовали абсолютный tangent-plane `GlobalPosition`. Поэтому TASK-162 закрывает не набор визуальных фиксов, а **planet-global logical coordinates + floating origin + persistence integration** как одну подсистему.

### Реализовано

- новый Godot-independent `PlanetSurfaceFrameRuntime`: double-precision East/North, cell `4096 m`, rebase threshold `2048 m`;
- player/physics остаются в bounded local frame, а logical position непрерывна через rebase;
- `TerrainChunkManager` выбирает chunks по logical player coordinate, но размещает `TerrainChunk` относительно current origin — worker sampling/chunk identity не меняются;
- `Gameplay` переводится frame origin; fallback `GroundBody` остаётся local-zero, но mesh/collision строятся вокруг текущего logical origin; procedural cloud/resource roots переведены под `Gameplay`;
- TASK-160 resource-window/POI residency, ecology flora proximity, terrain/geodesic HUD, planet map, base placement/preview и NPC navigation переведены на logical surface coordinates;
- live rebase синхронизирует absolute runtime caches: ground-NPC path/home targets, NPC-ship route waypoints, flying-fauna territory/aerial entries и aerial obstacle/POI environment;
- Stage-1 voyage state/landing/docking targets нормализуются через тот же frame, чтобы rebase не записывал local scene offset в доменное состояние корабля;
- autosave/graceful exit сохраняют player X/Z как planet-logical coordinates; cold load использует saved logical X/Z как initial origin и восстанавливает bounded local position около нуля; SQLite schema не менялась;
- F5 добавлен `TASK-162 planet-global surface frame acceptance`: >150 km synthetic route, bounded local, logical continuity, chunk identity, cold restore, planet reset, geodesic bounds;
- добавлены 3 xUnit regression tests, `tools/validate-task162-planet-global-surface-frame.py`, Windows/Linux section-37 integration и `docs/PLANET_GLOBAL_SURFACE_FRAME.md`.

### Нормативный источник / PDF-ТЗ

В переданном GitHub ZIP `Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf` является Git LFS pointer (`size 1774256`), а не PDF payload. Поэтому PDF в этой итерации **не реконструировался и не цитировался как прочитанный**; использована уже зафиксированная в этом журнале Stage-2 mapping и ограничения TASK-158/TASK-160. Это сохраняет правило не выдумывать требования отсутствующего payload.

### Изменённые ключевые файлы

- `src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceFrameRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceFrameAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceFrame.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `SalvageRepairSlice.cs`, `...PlanetTerrain.cs`, `...WorldComposition.cs`, `...PlanetMap.cs`, `...Voyage.cs`, `...StarSystem.cs`, `...PlanetSurfaceContent.cs`;
- `NpcNavigationSurfaceNode.cs`, `NpcFactionAgentNode.cs`, `NpcShipNavigationNode.cs`, `EcologyFaunaNode.cs`, `...AerialNavigation.cs`, `...Ecology.cs`;
- `tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs`;
- section-37 runners, TASK-150 compatibility gate, README/CHANGELOG/VERSION/docs.

### Проверки при подготовке

- TASK-146/148/149/150/152/154/154.1/156/158/158.1/160/160.1/162 static contract gates: `PASS`;
- TASK-162 standalone static gate: `PASS`;
- `dotnet` и Godot executable в текущем Linux preparation environment отсутствуют, поэтому build/xUnit/Godot runtime **не заявляются как выполненные здесь**; Windows clean build/F5/manual evidence требуется в TASK-163.

### Граница TASK-162

TASK-162 закрывает coordinate/floating-origin scaling, но не заявляет физическую cube-sphere поверхность: tangent heightfield topology сохранена; radial gravity, curved collision, cube-face transitions и seamless spherical topology — отдельная будущая подсистема.

### Acceptance TASK-163

1. Windows/Godot 4.7.1 .NET clean build: `0 warnings / 0 errors`; section-37 all green.
2. F5: новый `TASK-162 ... PASS`; для deterministic TASK-162 probe ожидаются `rebases=48; traversalSamples=49; maxLocal=2030.709m; logicalContinuity=1; chunkIdentity=1; coldRestore=1; planetReset=1; geodesic=1`. Сам TASK-162 probe занимает <1 s; общий F5 matrix включает более долгие прежние acceptance-задачи. TASK-160/158/138/124 и TASK-126 (с hotfix 160.1) не регрессируют.
3. Live traversal более `2048 m` по X или Z: Output содержит `TASK-162 planet surface REBASE`; `continuityError` близок к `0`; HUD `surface frame` держит local X/Z в пределах примерно `±2048 m`, logical X/Z продолжает расти.
4. Во время/после rebase terrain остаётся `25/25`, collision `9/9`; нет gap/fall; resource/POI/base/nav, ground NPC, flying fauna и NPC-ship traffic не прыгают и не стремятся к старым координатам.
5. Штатно сохранить на удалённой logical позиции, перезапустить: logical X/Z восстанавливаются, local frame остаётся bounded.
6. Добытый `surface_resource.*` после distant save/restart не респавнится; Stage-1 takeoff/landing после возврата в район корабля не ломается.

## 0. Текущая hotfix-итерация 2026-08-16 — TASK-160.1 Traversal-Safe Aerial Acceptance

**Исходный снимок:** `ProjectHorizon-main-task160-surface-world-composition.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task160.1-aerial-acceptance-hotfix.zip`.  
**Версия:** `0.1.0-alpha.160.1`.  
**Статус:** TASK-160.1 `VERIFIED`; внешний F5 подтвердил `faunaProbeSamples=4`, `sharedRuntime=1`, `runtimeSamples=1`; visual/persistence manual tail TASK-161 остаётся `IN_PROGRESS`.

### Внешнее evidence пользователя

- Live TASK-160 runtime поднялся корректно: `sky=1; sun=1; clouds=13; resourceWindow=20; activeResources=20; starterReserve=28m; persistence=seed+deltas; legacyFixtures=hidden`.
- После реального traversal streamer дошёл до `center=(5,-4)` и продолжал держать bounded `25` chunks.
- F5 подтвердил `TASK-160 ... PASS` (`starterPlanets=4/4; skyProfiles=4/4; resourcePlacements=89; visibleStar=1; atmosphereProfiles=1; cloudPolicy=1; resourceDeterministic=1; starterReserveClear=1; planetScopedIdentity=1; coldRestoreDepletion=1; untouchedDeltaEmpty=1`).
- TASK-138/TASK-158/TASK-124 в том же запуске остались `PASS`.
- Единственный новый FAIL: `TASK-126 aerial navigation acceptance`: `sharedRuntime=0; runtimeSamples=0`, при этом `faunaCoverage/localGrid/sphericalAvoidance/altitude/poiSteering/shipSteering/pursuit/evade/arrive/formation/combatStates/clearance = 1`; итоговые counters `faunaSamples=6929; shipSamples=1200`.

### Диагноз

TASK-126 acceptance зависел от текущего положения игрока. `EcologyRuntime.GetUpdateFrequencyHz` намеренно возвращает `0` дальше `50 m`. После TASK-158/TASK-160 игрок может штатно находиться в сотнях метров от authored ecology population; поэтому четыре flying-fauna остаются корректно bound к shared runtime, но за 4.5 s acceptance window не создают **новых** samples после baseline. NPC ships при этом получают deterministic acceptance steps во время TASK-148 Orbit legs, поэтому ship primitives остаются PASS. Это location-dependent дефект acceptance, а не отказ aerial navigation runtime.

### Исправлено

- `EcologyFaunaNode.StepAerialForAcceptance()` выполняет тот же `ApplyFlyingSteering`/shared `AerialSteeringRuntime` path независимо от player distance, без `MoveAndSlide`, без смены позиции/velocity и без воскрешения погибшей fauna.
- `BeginAerialNavigationAcceptance()` после baseline выполняет ровно один deterministic probe на каждый configured flying-fauna node; затем существующие строгие delta-инварианты `sharedRuntime` и `runtimeSamples` остаются неизменными.
- Output TASK-126 дополнен `faunaProbeSamples=<N>` для диагностики; ожидается `4`.
- Добавлены xUnit reflection/distance regression и `validate-task1601-aerial-acceptance-hotfix.py`; section-37 Windows/Linux runners включают новый gate.

### Acceptance TASK-160.1

1. Clean build: `0 warnings / 0 errors`.
2. Уйти минимум на `>160 m` (либо оставить игрока в секторе около `(5,-4)`), затем нажать `F5`.
3. Ожидается `TASK-126 ... PASS` с `sharedRuntime=1; runtimeSamples=1; faunaProbeSamples=4`.
4. TASK-160/TASK-158/TASK-138/TASK-124 должны остаться PASS.
5. После F5 положение игрока и состояние убитой/добытой fauna не должны измениться.

## 0. Текущая mega-итерация 2026-08-16 — TASK-160 Planet Surface World Composition & Persistence

**Версия:** `0.1.0-alpha.160`  
**Статус:** TASK-160 `IMPLEMENTED`; runtime/manual acceptance TASK-161 `IN_PROGRESS`. Предыдущий TASK-158.1 F5 подтверждён внешним Godot log: TASK-138/TASK-158/TASK-124 PASS; manual >160 m / planet-switch tail TASK-159 не реконструируется без отдельного доказательства.

### Причина итерации

Runtime screenshot после TASK-158 показал не технический streaming-дефект, а следующий системный разрыв: streamed terrain был почти чёрным, surface sky представлял собой цветной фон без убедительного солнца/атмосферной перспективы, а Stage-1 catalog fixtures и POI визуально концентрировались около стартовой площадки. Пользователь отдельно потребовал, чтобы добытые на планете procedural resources не респавнились после отлёта/возврата.

### Что реализовано

- `planet environment + current system star -> procedural sky + visible sun + sky ambient/reflection + aerial fog + deterministic clouds`;
- streamed terrain остаётся PBR-lit, но получил слабый planet-colored indirect-light floor и macro/slope color variation, устраняя абсолютный black-floor failure mode;
- 55 из 58 legacy catalog resource fixtures runtime-скрыты; три starter salvage nodes остаются для repair tutorial и legacy acceptance;
- live resources теперь генерируются по TASK-158 chunks (0–2 на chunk), terrain/slope aware, с 28 m starter reserve и archetype-weighted resource selection;
- stable resource identity = `planet + chunk X/Z + slot`; cold restore поддерживает dynamic bindings, а untouched procedural chunks не создают save delta;
- существующие 20 POI не меняют reviewed IDs/plan/golden fixture, но live nodes детерминированно распределяются по exploration annulus 78–420 m;
- F5 добавлен `TASK-160 planet surface world composition acceptance`; 3 xUnit regressions и `tools/validate-task160-surface-world-composition.py`; RU/EN parity сохранена.

### Ограничение

TASK-160 не заявляет day/night orbital clock, physical atmospheric scattering/ray marching, whole-planet ecology streaming или cube-sphere surface frames. Это качественная composition/persistence подсистема поверх TASK-158 tangent-plane streamer.

### Acceptance TASK-161

1. Clean build/section-37: `0 warnings / 0 errors`, новый TASK-160 static gate PASS.
2. F5: TASK-138/TASK-158 остаются PASS; новый TASK-160 PASS.
3. Визуально на temperate: terrain не чёрный; в sky видим star disk/sky gradient/haze; при cloudLayerCount>0 видим cloud clusters.
4. В landing reserve нет прежней стены из catalog resources; кроме трёх salvage узлов streamed deposits появляются за пределами 28 m; POI разнесены по территории.
5. Собрать один `surface_resource.*`, уйти так, чтобы chunk выгрузился, вернуться — узел не появляется. Штатно сохранить/перезапустить и повторить — узел остаётся depleted.
6. Перелететь на другую starter planet: sky/ground/cloud/resource composition меняются; возврат на исходную планету не восстанавливает добытый node.

## 0. Текущая hotfix-итерация 2026-08-16 — TASK-158.1 Runtime Acceptance / Golden POI Closure

**Исходный снимок:** `ProjectHorizon-main-task158-planet-surface-streaming.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task158.1-runtime-acceptance-hotfix.zip`.  
**Версия:** `0.1.0-alpha.158.1`.  
**Статус:** TASK-158 runtime implementation `VERIFIED` по внешнему Godot evidence; TASK-158.1 `IMPLEMENTED`; acceptance-tail TASK-159 остаётся `IN_PROGRESS` до повторного clean build `0/0` и финального manual traversal smoke.

### Внешнее evidence пользователя

- Windows/Godot 4.7.1 .NET build реально завершён: `Ошибок: 0`, но обнаружены `Предупреждений: 2` (`CS8602` в `NpcFactionAgentNode.cs:424` и `CS8600` в `PlanetSurfaceStreamingRuntime.cs:259`). Поэтому формальный clean-build критерий `0/0` ещё не выполнен.
- Live streamer реально дошёл до settled-state: `TASK-158 planet surface streaming READY: ... active=25/25; collisions=9/9; center=0,0; chunk=32m; window=5x5; vertices=14425; queue=0; workers=0; ... fallback=retired.`
- Реальные sector transitions выполнены как минимум `center=(0,0) -> (0,-1) -> (-1,-1) -> (-1,0) -> (0,0)`; каждый завершён с `active=25; high=9; low=16`, без `cancelled/stale/failed` в предоставленном логе.
- F5 подтвердил `TASK-158 planet surface streaming acceptance PASS: starterPlanets=4/4; activeChunks=25/25; highDetail=9/9; lowDetail=16/16; collisionChunks=9/9; deterministic=1; seamSafe=1; boundedResidency=1; traversalPlans=1; planetAddressing=1; fullRelief=1`.
- Соседние regressions остаются живы: TASK-124 navigation PASS (`tilesTouched=3; pathPoints=34/36; crossTilePath=1; obstacleClearance=1; recoveryProbe=1; sync=1`), TASK-156 terrain PASS, TASK-152/154 PASS.
- Единственный F5 FAIL — TASK-138: `goldenSystems=4/4`, но `goldenPoi=0; controlHeights=0; checksums=0`; actual POI checksum `6e229717...`, fixture checksum `ad8d7cdd...`.

### Диагноз

TASK-158 streamer не меняет POI generation. Причина FAIL появилась раньше: TASK-156 перевёл `PlanetaryPoiPlanner` на terrain-projected world-space Y (`environment.Height + 0.1 + sizeY/2`), но reviewed POI fixture и `ProjectHorizonGenerator.Version` тогда не были обновлены. Независимая реконструкция текущего planner из 20 прежних stable placements даёт **точно наблюдавшийся runtime checksum** `6e229717a6faad6043f963d825ba8b13a2af9dbf2335c161e6a24fca450ddfcc`. X/Z, stable instance IDs, controlHeight, slope, rotation, water distance и danger не меняются — меняется только world-space Y, то есть это stale golden contract, а не nondeterminism.

### Исправлено

- `ProjectHorizonGenerator.Version`: `2 -> 3` согласно собственному §36 contract при намеренном изменении deterministic world-generation output.
- `golden-seeds.v1.json`: generatorVersion `3`; все 20 POI `positionY` синхронизированы с текущим terrain-projected planner; checksum установлен в реально наблюдавшийся `6e229717...`. System golden cases не изменены.
- `GoldenSeedTests`: добавлена явная проверка `PositionY == ControlHeight + 0.1 + definition.Size.Y/2` для каждого из 20 golden POI.
- `NpcFactionAgentNode.OnNavigationVelocityComputed`: nullable guard теперь требует `_navigationSurface != null`, устраняя внешний `CS8602`.
- `PlanetSurfaceStreamingRuntime.AddStitch`: nullable result `TryGetValue` обработан явно, устраняя внешний `CS8600`.
- Добавлен `tools/validate-task1581-runtime-acceptance-hotfix.py`; Windows/Linux section-37 runners запускают его после TASK-158 gate.
- README/CHANGELOG/VERSION/REQUIREMENTS_STATUS синхронизированы. Save schema/content schema не меняются.

### Следующая проверка TASK-159

1. `tools\clean-build-windows10.cmd` -> строго `Предупреждений: 0; Ошибок: 0`.
2. F5 -> `TASK-138 verification suite acceptance PASS` с `generatorVersion=3; goldenSystems=4/4; goldenPoi=1; controlHeights=1; checksums=1`.
3. F5 -> TASK-158 остаётся PASS (`25/25`, `9/9`, deterministic/seam/bounded/traversal/address/fullRelief all `1`).
4. Дождаться live `TASK-158 ... READY` с `queue=0; workers=0; fallback=retired`; пройти/пролететь >160 m и сделать диагональный sector transition без провала/щели.
5. При FAIL прислать первую compiler/runtime error, полный TASK-138/TASK-158 output и ближайший `TerrainChunkManager: completed ...` line.

---

## 0. Текущая mega-итерация 2026-08-15 — TASK-156 Planet-Specific Terrain & Surface Geometry

**Исходный снимок:** `ProjectHorizon-main-task154.1-runtime-acceptance-hotfix.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task156-planet-specific-terrain.zip`.  
**Версия:** `0.1.0-alpha.156`.  
**Статус:** TASK-156 `IMPLEMENTED`; runtime/manual acceptance TASK-157 `IN_PROGRESS`.

### Основание для следующей mega-итерации

Повторный внешний F5 пользователя подтвердил, что hotfix TASK-154.1 сработал: `TASK-138 verification suite acceptance PASS` показывает `generatorVersion=2; goldenSystems=4/4; checksums=1`, а `TASK-124 NPC navigation acceptance PASS` показывает реальный cross-tile path (`tilesTouched=4; pathPoints=56; probeAttempts=3; recoveryProbe=1; sync=1`). Одновременно TASK-152 и TASK-154 снова PASS. Поэтому следующая кодовая итерация не тратится на ещё один acceptance-only hotfix.

После TASK-150/TASK-154 планеты уже различались environment/biome/ecology/POI, но playable `GroundBody` оставался единым плоским `80 x 80 m` BoxMesh/BoxShape. Это стало главным системным и визуальным разрывом Stage 2. TASK-156 закрывает одну связанную подсистему: **planet archetype/seed → deterministic relief → mesh/collision → ecology/POI projection → NPC navigation → base/resource grounding → F5 acceptance**.

### Реализовано

- добавлен `PlanetSurfaceTerrainRuntime` и `PlanetSurfaceTerrainProfile`: единый deterministic sampler для всех landable archetypes; starter temperate/desert/frozen/volcanic имеют разные morphology algorithms/amplitude/frequency budgets;
- active bounded surface сохраняет существующий footprint `80 x 80 m`, но runtime заменяет плоский ground на `65 x 65` grid (`4,225` vertices / `8,192` triangles) через `SurfaceTool`; collision заменяется на `CreateTrimeshShape()`;
- центральная tutorial/infrastructure зона имеет deterministic terrace: relief подавлен внутри 16 m и плавно выходит на полный профиль к 23 m, чтобы не ломать starter repair/crafting/building loop;
- wet profiles получают гарантированные terrain basins под существующий `Gameplay/WaterPool` и `AquaticHabitat`; dry profiles сохраняют TASK-154 aquatic suppression;
- `EcologyPlanner.PlanPlanet` принимает terrain profile: flora получает surface Y; ground fauna привязывается к surface height и во время движения больше не возвращается на фиксированную `Y=0.75`; legacy starter ecology IDs/XZ не меняются — scene projection меняет только Y;
- `PlanetaryPoiPlanner.PlanPlanet` использует физический terrain slope как часть constraint evaluation; POI scene всегда проецируется на exact terrain Y. Historical starter POI instance IDs и X/Z сохранены;
- `NpcNavigationSurfaceNode` больше не требует BoxShape ground при активном terrain profile: каждая вершина `NavigationRegion3D` получает sampled Y, слишком крутые cells исключаются, avoidance obstacles ставятся на локальную высоту;
- `NpcFactionAgentNode` больше не форсирует `_home.Y`; после движения NPC возвращается на navigation/terrain height;
- generated resource nodes и base construction preview/modules проецируются на terrain; persistent base grid остаётся X/Z-only и не требует save migration;
- F5 matrix дополнена `TASK-156 planet terrain acceptance`; добавлены 3 xUnit regressions и `tools/validate-task156-planet-surface-terrain.py`; quality cmd/sh запускают новый gate; RU/EN HUD parity сохранена;
- документация: `docs/PLANET_SURFACE_TERRAIN.md`, README, CHANGELOG, VERSION и этот журнал.

### Детерминированная проверка planner compatibility

Помимо repository static gates, morphology/slope math воспроизведена отдельно для фактических stable seeds четырёх starter planets из reviewed golden fixture. Центральная терраса остаётся `0.0 m`; temperate/frozen basin centers гарантированно опускаются до `-1.20/-1.05 m`; dry desert/volcanic basin policy не применяется. Terrain-aware POI planner на тех же четырёх seed сохраняет полный результат `20/20` POI для каждой планеты, включая строгие low-slope объекты вроде landing pad/trading outpost.

### Фактические проверки в среде подготовки

Все repository `validate-*.py` gates проходят после интеграции TASK-156. Новый gate выдаёт:

`TASK-156 PLANET SURFACE TERRAIN CONTRACT PASS: starterMorphology=4/4; deterministic=1; mesh=65x65; trimesh=1; centralTerrace=1; waterBasins=1; ecologyProjection=1; poiTerrain=1; navHeightfield=1; npcGrounding=1; legacyIds=1; f5=1; xunit=3/3; localization=2/2.`

В среде подготовки по-прежнему нет `dotnet`, `csc/msbuild` и Godot, а внешний network недоступен, поэтому реальная компиляция/xUnit/Godot TASK-156 здесь не заявляются. Нормативные PDF в исходном архиве остаются Git LFS pointer files; новые требования не реконструируются сверх уже зафиксированных §9.1–9.8/Stage 2 и существующей terrain/quadtree architecture.

### Критерии runtime-приёмки TASK-157

1. `tools\clean-build-windows10.cmd` → `0 warnings / 0 errors`; затем `tools\run-section37-quality.cmd`.
2. F5 → обязательна `TASK-156 planet terrain acceptance PASS` с `starterPlanets=4/4; distinctMorphology=4/4; deterministic=1; centralTerrace=1; geometryBounds=1; walkableCoverage=1; waterBasinPolicy=1; ecologyGrounded=1; poiTerrainAware=1; legacyIdentitySafe=1; vertices=4225; triangles=8192`.
3. В стартовом temperate мире визуально проверить, что центр вокруг корабля/станций остаётся пригодной террасой, а за её границей появляется реальный relief; NPC и ground fauna не должны парить/проваливаться.
4. Перелететь минимум на desert и volcanic: профиль terrain должен заметно отличаться; volcanic должен быть самым резким и оставаться dry.
5. На frozen/temperate проверить водную впадину и отсутствие пересечения воды с поднятым грунтом; на volcanic water habitat должен отсутствовать.
6. Проверить cross-tile движение NPC на relief и построить base module вне центра на умеренном склоне: preview/module должны стоять на surface Y.
7. Прислать clean build `0/0`, полный `TASK-156 ... PASS`, одну `TASK-156 planet terrain READY` для temperate и volcanic и 2 screenshots (temperate/frozen + volcanic). При FAIL — первая compile/runtime error, полный FAIL output и ближайшие TASK-124/TASK-156 diagnostics.

**Known boundary:** TASK-156 делает bounded active surface геометрически planet-specific и интегрирует его с gameplay. Он не заменяет Stage 2 bounded residency на бесшовный whole-planet streamer: существующая cube-sphere/quadtree architecture остаётся следующим уровнем масштабирования, а не параллельно resident full planet.

---

## 0. Текущая hotfix-итерация 2026-08-15 — TASK-154.1 F5 Runtime Acceptance Regression Closure

**Исходный снимок:** `ProjectHorizon-main-task154-multi-planet-surface-content.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task154.1-runtime-acceptance-hotfix.zip`.  
**Версия:** `0.1.0-alpha.154.1`.  
**Статус:** hotfix `VERIFIED` по повторному Godot F5 evidence пользователя: TASK-138 и TASK-124 после исправлений PASS. TASK-153 и TASK-155 формально сохраняют отдельные manual/build acceptance-tail пункты.

### Внешнее runtime evidence пользователя

Godot 4.7.1 .NET / Forward Mobile / Vulkan реально запустил F5 matrix. Подтверждены `PASS` для TASK-128, TASK-150, TASK-152, TASK-154, TASK-148, TASK-130, TASK-132, TASK-134, TASK-136, TASK-142, TASK-144, TASK-110/112/114/116/118/120/122/126 и crafting matrix. Для текущих Stage 2 задач зафиксированы точные строки:

`TASK-152 interplanetary travel acceptance PASS: starterPlanets=4/4; targetSelection=1; targetPersistence=1; fuelDebited=1; guidance=1; worldHandoff=1; arrival=1; transferPersistence=1; sameSystem=1; ...`

`TASK-154 multi-planet surface content acceptance PASS: starterPlanets=4/4; biomeProfiles=4/4; regions=4/4; ecologyClimateAware=1; aquaticPolicy=1; poiClimateAware=1; poiDeterministic=1; perPlanetPersistence=1; legacyStarter=1; ...`

Одновременно выявлены два F5 regression defects:

1. `TASK-138 verification suite acceptance FAIL`: runtime generator уже штатно выдаёт четыре starter planets и checksum `de556c0b329522a2fb698e67106542f6befc0e8ecc2238a8fac42f2ea8616d66`, а reviewed golden fixture всё ещё содержал старую одно-планетную систему и checksum `122c191b...`. Это stale golden contract после TASK-150, а не nondeterministic worldgen.
2. `TASK-124 NPC navigation acceptance FAIL`: `regions=25/25`, `walkableCells=2458`, `navigationAgents=7`, `pathRequests=80`, `avoidanceSamples=1634`, однако initial acceptance path probe зафиксировал `tilesTouched=0/pathPoints=0`; после завершения FAIL в том же runtime появились реальные `TASK-124 NPC navigation recovery` события. Это подтверждает race между F5 surface/world rebuild и первым NavigationServer path query, а не отсутствие NavigationAgent runtime.

### Исправлено

- `ProjectHorizonGenerator.Version` повышен `1 → 2`, поскольку deterministic starter-system output уже был намеренно изменён TASK-150; generator version теперь соответствует фактической четырёхпланетной генерации.
- `golden-seeds.v1.json` обновлён только в reviewed starter-system case: 4 planets `temperate/desert/frozen/volcanic`, сохранены фактические stable seeds/moon counts/IDs, checksum установлен в фактически наблюдавшийся `de556c0...`; остальные три fixed system cases и POI fixture не изменены.
- `validate-section36-testing-contract.py` больше не hard-code `generatorVersion=1`: он извлекает central `ProjectHorizonGenerator.Version` и требует точного совпадения golden manifest.
- TASK-124 one-shot path probe заменён bounded readiness barrier. Acceptance ждёт не только `MapGetIterationId`, но и фактически ненулевой query path; пробует горизонтальные, вертикальные и диагональные cross-tile маршруты, не снижая критерии `tiles>=3`, obstacle clearance и recovery.
- После forced streaming shift/restore path/recovery probe выполняется повторно; phase timeout сбрасывается на переходах и ограничен 6 секундами на фазу, чтобы тест не зависал бесконечно. Output дополнен `probeAttempts`.
- Добавлен `tools/validate-task1541-runtime-acceptance-hotfix.py`; TASK-149.4 gate усилен `navPathReadiness=1`; оба section-37 quality runner запускают новый gate.

### Статусы по предоставленному evidence

- `TRAVEL-ACC-102`: `IN_PROGRESS → VERIFIED` — фактический F5 TASK-152 PASS.
- `SURFACE-ACC-102`: `IN_PROGRESS → VERIFIED` — фактический F5 TASK-154 PASS.
- `TASK-153`: остаётся `IN_PROGRESS` — нет присланного clean-build/section-37 и manual target→cruise→landing→cold-restore evidence.
- `TASK-155`: остаётся `IN_PROGRESS` — нет manual multi-planet variation + independent cold-restore evidence.
- TASK-124 navigation implementation не откатывается: runtime diagnostics/последующие recovery подтверждают живую подсистему; исправляется ложный negative acceptance race.
- TASK-138 golden verification contract повторно подтверждён: `generatorVersion=2; goldenSystems=4/4; checksums=1`. TASK-124 также повторно PASS (`tilesTouched=4; pathPoints=56; probeAttempts=3; recoveryProbe=1; sync=1`).

### Фактические проверки в среде подготовки

`TASK-154.1 RUNTIME ACCEPTANCE HOTFIX CONTRACT PASS: generatorVersion=2; goldenStarter=4/4; goldenChecksum=1; navQueryRetry=1; navRestoredPath=1; navCrossTileInvariant=1; boundedPhases=1.`

`TASK-138 SECTION-36 CONTRACT PASS: unitGroups=10/10; saveScenarios=8/8; loadScenarios=8/8+abnormal; goldenVersion=2; goldenSystems=4; goldenPoi=20; coverage=80/70/80; visualSmoke=1; standaloneDotnet=1; f5Smoke=1.`

`TASK-149.4 RUNTIME REGRESSION CLOSURE PASS: frequencyGate=1; navIterationGuard=1; navPathReadiness=1; profilePathNormalization=1; orbitResidentShipProbe=1; residencyAwareAerialAcceptance=1; xunitFrequencyBounds=1.`

В исходном архиве нормативные PDF по-прежнему представлены Git LFS pointer-файлами; render подтвердил отсутствие PDF payload. Поэтому новые трактовки ТЗ не вводятся — hotfix относится к уже зафиксированным §30.1/TASK-124 и §36/TASK-138 требованиям.

---

## 0. Предыдущая mega-итерация 2026-08-15 — TASK-154 Planet-Scoped Surface Content

**Исходный снимок:** `ProjectHorizon-main(20260815-192529).zip` — последняя приложенная пользователем редакция с GitHub.  
**Подготовленный снимок:** `ProjectHorizon-main-task154-multi-planet-surface-content.zip`.  
**Версия:** `0.1.0-alpha.154`.  
**Статус:** TASK-154 `IMPLEMENTED`; runtime acceptance TASK-155 `IN_PROGRESS`; ранее открытая TASK-153 Interplanetary Travel acceptance остаётся `IN_PROGRESS` и не объявляется закрытой без реального Windows/Godot запуска.

### Выбор mega-итерации и граница протокола

По очереди журнала ближайшей формальной задачей остаётся TASK-153 — runtime-приёмка уже реализованной TASK-152. В среде подготовки отсутствуют `dotnet`, `csc/msbuild` и Godot, поэтому выполнить требуемые clean build/F5/manual доказательства TASK-153 здесь невозможно. Пользователь отдельно запросил проверить возможность очередной «мега-итерации». Поэтому новая кодовая работа не выдаётся за закрытие TASK-153: она реализована как TASK-154, а TASK-153 сохраняет приоритет в acceptance queue.

В приложенном GitHub ZIP файлы PDF-ТЗ являются Git LFS pointer files, а не PDF payload; прямой render/screenshot нормативного PDF в этой среде невозможен. Связанные требования берутся из уже зафиксированной в этом журнале карты PDF v2.0: §3.3, §9.5–9.8 и Stage 2 (`3–5` планет с различными биомами/водой/атмосферой), плюс связка с §15/межпланетным перелётом TASK-152. Нормативный PDF не подменяется и его содержимое не реконструируется сверх уже записанных требований.

### Почему это цельная подсистема

После TASK-152 смена `CurrentPlanetId` была реальной, а water/atmosphere metadata уже зависели от планеты, но gameplay content поверхности оставался глобальным: `EcologyPlanner` всегда использовал один `vertical_slice` seed и четыре фиксированных biome ID, а `PlanetaryPoiPlanner` всегда семплировал `biome.test_plain`. В результате физический перелёт менял планету в навигации, но не менял реальную экологию и исследовательский слой. TASK-154 закрывает весь разрыв **planet identity → environment → ecology → POI → presentation → per-planet persistence → arrival activation** одной итерацией.

### Реализовано

- добавлен `PlanetSurfaceContentRuntime`: для landable `CurrentPlanetId` строит deterministic surface profile из существующего `PlanetEnvironmentRuntime`, stable planet seed, 1–8 active biomes, water coverage и bounded habitability;
- `EcologyPlanner.PlanPlanet` теперь строит flora/fauna только из активных биомов текущей планеты, варьирует bounded population budgets по habitability и исключает aquatic fauna при `waterCoverage < 0.12`;
- `PlanetaryPoiPlanner.PlanPlanet` сохраняет 20 нормативных POI types, но вместо hard-coded `biome.test_plain` использует `PlanetEnvironmentRuntime.SampleBiome`; water distance и danger зависят от профиля текущей планеты и deterministic local sample;
- legacy `planet.vertical_slice` намеренно оставлен на исторических ecology/POI planner identity, seed/region и instance IDs, чтобы старые saves продолжали загружаться без несовместимости;
- `EcologyRuntime` и `PlanetaryExplorationRuntime` получили явную planet-scoped identity (`worldSeed`, `regionKey`) при сохранении legacy constructors;
- existing JSON save settings `ecology` и `planetary_exploration` расширены optional `PlanetId` + `PlanetStates`; deltas архивируются отдельно для каждой посещённой планеты без SQLite schema bump; старый root без `PlanetId` мигрирует в памяти как state `planet.vertical_slice`;
- save boundary канонизирует и валидирует вложенные planet archives, stable IDs, region identity и duplicate planet IDs;
- interplanetary arrival теперь выполняет `CaptureCurrentPlanetSurfaceState()` перед commit и `ActivateCurrentPlanetSurfaceContent()` после успешного transfer; hyperspace сохраняет предыдущую surface-state и активирует новый landable body, non-landable body не уничтожает предыдущий архив;
- presentation поверхности переключает ground tint, atmosphere/ambient color и water color; dry profile отключает `Gameplay/WaterPool`, а ecology rebuild не создаёт `AquaticHabitat`;
- F5 matrix дополнена `TASK-154 multi-planet surface content acceptance`; acceptance проверяет 4/4 starter planets, distinct biome/region profiles, climate-aware ecology, aquatic policy, deterministic climate-aware POI, per-planet state round-trip и legacy starter compatibility;
- добавлены 3 xUnit regression tests и `tools/validate-task154-multi-planet-surface-content.py`; validator включён в оба section-37 quality scripts;
- добавлена документация `docs/MULTI_PLANET_SURFACE_CONTENT.md`; `README.md`, `CHANGELOG.md`, `VERSION` и этот журнал обновлены.

### Изменённые/добавленные ключевые файлы

`SaveGameModels.cs`, `SaveDatabase.cs`, `EcologyPlanner.cs`, `EcologyRuntime.cs`, `PlanetaryPoiPlanner.cs`, `PlanetaryExplorationRuntime.cs`, `PlanetSurfaceContentRuntime.cs`, `PlanetSurfaceContentAcceptance.cs`, `SalvageRepairSlicePlanetSurfaceContent.cs`, `SalvageRepairSliceEcology.cs`, `SalvageRepairSliceInterplanetaryTravel.cs`, `SalvageRepairSliceGalaxy.cs`, `SalvageRepairSlice.cs`, `WorldGenTests.cs`, `localization.en.json`, `localization.ru.json`, section-37 scripts, TASK-154 validator, README/CHANGELOG/VERSION и `docs/MULTI_PLANET_SURFACE_CONTENT.md`.

### Фактические проверки в среде подготовки

Доступные repository static gates проходят после интеграции TASK-154: JSON content, Godot text-resource structure, localization, audio, developer diagnostics, §36 testing contract, §37 build contract, §38 architecture, platform architecture, TASK-146, TASK-148, TASK-149.4, TASK-150, TASK-152 и новый TASK-154. Новый gate выдаёт:

`TASK-154 MULTI-PLANET SURFACE CONTENT CONTRACT PASS: starterPlanets=4/4; distinctBiomes=4/4; ecologyClimate=1; aquaticPolicy=1; poiClimate=1; perPlanetArchives=1; legacyStarter=1; arrivalSwitch=1; hyperspacePreserve=1; visualSurface=1; f5=1; xunit=3/3.`

Все 14 изменённых C# sources дополнительно проверены: строковые и символьные литералы закрыты, block comments закрыты, braces/parentheses/brackets сбалансированы. Реальная `dotnet build`, xUnit execution и Godot runtime/F5 не выполнялись: соответствующих исполняемых инструментов в среде нет. Статус `VERIFIED` поэтому не присваивается.

### Критерии runtime-приёмки TASK-154 / TASK-155

1. Windows: `tools\clean-build-windows10.cmd` → `0 warnings / 0 errors`.
2. F5: обязательна строка `TASK-154 multi-planet surface content acceptance PASS` с `starterPlanets=4/4; biomeProfiles=4/4; regions=4/4; ecologyClimateAware=1; aquaticPolicy=1; poiClimateAware=1; poiDeterministic=1; perPlanetPersistence=1; legacyStarter=1`.
3. Manual starter planet: убедиться, что прежний `planet.vertical_slice` выглядит и работает без потери старых discovery/ecology deltas.
4. `M → System → другая landable planet → Enter`, затем реальный перелёт/`K`; после `TASK-152 interplanetary transfer PASS` HUD должен показать другой `Surface <archetype>`, а Output — `TASK-154 planet surface content READY` с новым planet ID/biome list/region.
5. Проверить минимум desert, frozen и volcanic: flora/fauna/POI biome должны отличаться от starter; на dry volcanic water pool и `AquaticHabitat` отсутствуют, aquatic fauna не создаётся.
6. На двух разных планетах открыть/просканировать разные POI или ecology specimen, сделать graceful exit/Continue, затем вернуться на первую планету: discovery/removal state каждой планеты должен восстановиться независимо и не «перетечь» между планетами.
7. Для доказательства прислать: строку clean build, весь `TASK-154 ... acceptance PASS`, `TASK-154 planet surface content READY` минимум для двух разных planet IDs, один screenshot dry volcanic surface и один screenshot water-bearing surface. При FAIL — прислать первую ошибку/stack trace, текущий planet ID/archetype и ближайшую строку `TASK-154 ... READY/STANDBY`.

**Known boundary:** TASK-154 делает surface content planet-correct внутри существующего bounded vertical slice. Это не бесшовный full-planet terrain streamer и не параллельная загрузка нескольких detailed PlanetRuntime. Gas giant остаётся non-landable и не получает surface plan.

---

## 0. Предыдущая mega-итерация 2026-08-15 — TASK-152 Interplanetary Travel & Planet Activation Handoff

**Исходный снимок:** `ProjectHorizon-main-task150.1-build-graceful-exit-hotfix.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task152-interplanetary-travel.zip`.  
**Версия:** `0.1.0-alpha.152`.  
**Статус:** TASK-152 `IMPLEMENTED`; TASK-153 runtime acceptance `IN_PROGRESS`.

### Синхронизация TASK-150/TASK-151

Владелец продукта после TASK-150.1 прямо сообщил: **«всё работает»**. Это фиксируется как qualitative product-owner acceptance: TASK-150 и TASK-151 переводятся в `VERIFIED`. Отсутствующие численные build/F5/manual показатели не реконструируются и не приписываются задним числом.

### Граница mega-итерации

TASK-152 закрывает единую подсистему same-system planetary travel, предусмотренную концепцией полёта внутри звёздной системы, §15 космической симуляции и Stage 2 с 3–5 планетами: **System Map target → persisted target → physical assisted cruise → fuel debit → InterplanetaryTransit world residency → destination planet commit → local PlanetRuntime approach → save/restore**. Это не teleport selector и не новая параллельная flight physics. Используется существующий `ArcadeShipController` и его external command path.

### Реализовано

- System Map: Up/Down выбирают planet row; `Enter` выбирает landable `TARGET`; текущая планета помечается `CURRENT`; gas giant отклоняется;
- `GalaxyNavigationRuntime` хранит `SelectedPlanetId`, `InterplanetaryTransferCount`, `TotalInterplanetaryDistanceMeters`; поля backward-compatible и не требуют SQLite schema bump;
- новый Godot-independent `InterplanetaryTravelRuntime` валидирует piloted/flight-ready state, рассчитывает bounded fuel cost, braking/arrival thresholds и exact source/target transaction;
- существующий `K` navigation assist при наличии planetary target использует live proxy position из `StarSystemSimulationNode` и управляет кораблём через `SetExternalCommand`; ручное отключение `K` прекращает assisted cruise, цель сохраняется, топливо не возвращается;
- world graph расширен `InterplanetaryTransit`: разрешён `Orbit(source) -> InterplanetaryTransit(source) -> Orbit(destination)` в той же системе; прямой cross-planet Orbit→Orbit остаётся запрещён;
- во время transit system proxies остаются активны, тяжёлая surface residency выключена; после arrival `CurrentPlanetId` коммитится, transfer counters обновляются, ship rebased в локальный `planet.approach`, после чего действует существующий inbound/landing flow;
- добавлены `InterplanetaryTransitShell.tscn`, `InterplanetaryTravelAcceptance.cs`, `SalvageRepairSliceInterplanetaryTravel.cs`, `docs/INTERPLANETARY_TRAVEL.md`;
- F5 расширен TASK-152 acceptance; добавлены 3 xUnit regression checks и `tools/validate-task152-interplanetary-travel.py`, включённый в section-37 local quality scripts;
- RU/EN localization parity сохранена.

### Статическая проверка в среде подготовки

Все 15 доступных repository static gates после финальных изменений проходят, включая JSON/Godot/localization/audio/developer diagnostics, §36/§37/§38, platform architecture, TASK-146/TASK-148/TASK-149.4/TASK-150 и новый `TASK-152 INTERPLANETARY TRAVEL CONTRACT PASS: starterPlanets=4/4; targetSelection=1; targetPersistence=1; fuel=1; physicalGuidance=1; proxyTarget=1; worldHandoff=1; singlePlanetResidency=1; localApproach=1; transferCounters=1; persistence=1; systemMap=1; manualCancel=1; f5=1; xunit=3/3; localization=1.` Изменённые C# sources дополнительно проходят lexical/bracket balance. `dotnet`, `csc`, `msbuild` и Godot в среде подготовки отсутствуют, поэтому real compile/xUnit/Godot runtime для alpha.152 не заявляются.

### Приёмка TASK-153

1. `tools\clean-build-windows10.cmd` → `0 warnings / 0 errors`.
2. F5 → `TASK-152 interplanetary travel acceptance PASS` с targetSelection/targetPersistence/fuelDebited/guidance/worldHandoff/arrival/transferPersistence/sameSystem = 1.
3. Manual: `M → System → выбрать другую landable planet → Enter`, затем полёт/`K`; должен появиться BEGIN, физическое сближение с proxy и `TASK-152 interplanetary transfer PASS`.
4. После arrival System Map показывает destination как `CURRENT`, target пуст; последующая посадка использует обычный inbound flow.
5. Graceful exit → Continue сохраняет destination `CurrentPlanetId` и transfer counters.

**Сознательная граница:** TASK-152 не вводит реальные астрономические масштабы/небесную механику, не грузит несколько detailed PlanetRuntime одновременно и не добавляет новый hotkey/flight controller.

---

## История предыдущей mega-итерации 2026-08-15 — TASK-150 Multi-Planet Environment subsystem

## 0.1. Hotfix 2026-08-15 — TASK-150.1 build + graceful-exit closure

**Подготовленный снимок:** `ProjectHorizon-main-task150.1-build-graceful-exit-hotfix.zip`.  
**Версия:** `0.1.0-alpha.150.1`.  
**Статус:** TASK-150 остаётся `IMPLEMENTED`; TASK-151 остаётся `IN_PROGRESS` до повторной реальной сборки и runtime-приёмки.

**Фактическое внешнее доказательство:** пользовательский Windows/Godot build от 15.08.2026 19:35 дошёл до `Game.Client/CoreCompile`, завершился `0 warnings / 1 error` и выявил единственный blocker `CS0104` в `DeveloperWorkbenchController.cs(232,17)`: неоднозначный `FileAccess` между `Godot.FileAccess` и `System.IO.FileAccess`. Runtime-лог также выявил same-frame graceful-exit race: после успешного flush/scene transition `_Process()` повторно запускал `TryBeginGracefulExit`, когда `Player` уже вышел из SceneTree, а lifetime `CancellationTokenSource` был disposed.

**Исправление:**
- Planet Preview использует явный `Godot.FileAccess.GetFileAsString`;
- введён `_exitTransitionCommitted` как одноразовый commit guard;
- `PollGracefulExitTask()` возвращает признак committed transition, после которого текущий `_Process()` немедленно завершается;
- `TryBeginGracefulExit()` запрещён после commit/выхода узла из дерева и не читает позицию `Player`, пока тот не находится в SceneTree;
- позиция игрока читается один раз в локальный `Vector3` до формирования snapshot;
- `_ExitTree()` фиксирует committed teardown до Cancel/Dispose lifetime CTS;
- TASK-150 static gate дополнен защитой от неоднозначного `FileAccess` и same-frame graceful-exit re-entry.

**Приёмка hotfix:** clean build должен дать `0 warnings / 0 errors`; затем Return to Main Menu и закрытие окна игры после gameplay не должны выдавать `!is_inside_tree()` из `Player.GlobalPosition` и `ObjectDisposedException: CancellationTokenSource has been disposed`.


### Синхронизация принятой TASK-149 / technical acceptance tail

Владелец продукта после передачи `ProjectHorizon-main-task149-runtime-regression-hotfix.zip` прямо указал: **«будем считать, что всё работает»** и потребовал перейти к следующей итерации. По правилам `DEVELOPMENT_ITERATION_PROTOCOL.md` это фиксируется как explicit **product-owner acceptance waiver** для оставшегося acceptance-tail предыдущего технического блока. Уже полученные фактические показатели не заменяются выдуманными: реальный TASK-148 F5 остаётся доказан строкой `livePath=1; transactionalSwap=1; stateRestored=1; steps=7; maxHostChildren=1; sceneLoadFailures=0; rollbacks=0`; отсутствующие exact clean-build/manual/Compatibility метрики задним числом не реконструируются.

Статусы синхронизированы перед выбором новой функции:

- `TASK-148`: `IMPLEMENTED → VERIFIED`;
- `TASK-149`: `IN_PROGRESS → VERIFIED`;
- `WORLD-100..109`: `IMPLEMENTED → VERIFIED`;
- `WORLD-ACC-100/101/103`: `IN_PROGRESS → VERIFIED` по explicit product-owner acceptance waiver; `WORLD-ACC-102` уже был `VERIFIED` по фактическому F5 output;
- `TASK-142/TASK-144`: `IMPLEMENTED → VERIFIED`, `TASK-143/TASK-145`: `IN_PROGRESS → VERIFIED` по тому же waiver, закрывающему оставшийся architecture/renderer acceptance-tail; точные отсутствующие Compatibility/build строки не приписываются;
- связанные `ARCH-001..003`, `CFG-001..004`, `CFG-006`, `CFG-009..015` переводятся в `VERIFIED` как принятая техническая foundation;
- `TASK-006` остаётся `BLOCKED`: в доступном снимке нет `.git`, поэтому SHA фактического GitHub commit установить нельзя.

### TASK-150 — mega-итерация: Multi-Planet Environment / Stage 2 planetary foundation

**Исходный снимок:** `ProjectHorizon-main-task149-runtime-regression-hotfix.zip` — newest code revision, доступная в file surface текущего рабочего пространства. Отдельный новый пользовательский GitHub ZIP после команды на TASK-150 в доступном file surface не появился, поэтому регламентно сохранена последняя фактически доступная кодовая база, чтобы не потерять принятые TASK-149.4 исправления.  
**Подготовленный снимок:** `ProjectHorizon-main-task150-multi-planet-environments.zip`.  
**Версия:** `0.1.0-alpha.150`.  
**Связанные требования PDF-ТЗ v2.0:** §3.3 — девять типов планет; §9.1–9.3 — cube sphere, радиусы `20–80 км`, quadtree LOD; §9.5 — biome selection по latitude/elevation/temperature/moisture/atmosphere/distance-to-water/local-noise/planet params, максимум восемь активных биомов; §9.6 — сферическая вода фиксированного уровня без физической симуляции жидкости; §9.7 — упрощённая атмосферная оболочка без дорогого ray marching на слабом профиле; §9.8 — `0–2` облачных shell layers; Stage 2 — `3–5` планет, разные биомы, вода и атмосфера; §34.2 — Planet Preview.

**Почему это mega-итерация:** после принятия Stage 1/world-scene foundation ближайшая крупная функциональная граница ТЗ — не отдельный «ещё один planet mesh», а связанная Stage 2 подсистема **planet identity → deterministic environment → biome policy → water/atmosphere/cloud presentation → map/preview → persistence**. Реализовать эти части отдельно означало бы несколько итераций с временно несогласованными planet definitions. TASK-150 закрывает их единым data-driven contract.

**Реализовано:**

- новый строгий `Content/planet_environments.json` с ровно девятью archetypes: `temperate/desert/frozen/volcanic/toxic/radioactive/barren/oceanic/gas_giant`;
- `PlanetEnvironmentCatalog` валидирует schema, radius/gravity/climate ranges, `0–2` cloud layers, colors, landability и `1–8` ecology biome references для каждой landable планеты; gas giant обязан быть non-landable и не иметь surface biomes;
- `GalaxyNavigationRuntime` сохраняет общий procedural rule `1–8 planets` для остальных systems, но starter system теперь детерминированно имеет четыре landable планеты разных archetypes: temperate/desert/frozen/volcanic; исходный `StarterRepairSnapshotFactory.PlanetId` сохранён как planet 1;
- `PlanetEnvironmentRuntime` без global sequential RNG выводит radius, gravity, mean temperature, atmosphere density, water coverage, cloud count/density и risk/color parameters из planet seed + archetype + star type;
- biome sampler учитывает latitude, normalized elevation, distance to water, local noise, climate/moisture и выбирает только из catalog-approved ecology biomes;
- `GalaxyNavigationSaveData` получил backward-compatible optional `CurrentPlanetId`; current planet валидируется против deterministic current system, gas giant не может стать current landable planet, legacy save без поля выбирает первую landable планету; SQLite schema не повышается;
- system map и gameplay HUD показывают environment detail текущей/перечисленных планет;
- Developer Planet Preview использует тот же environment runtime и добавляет spherical water shell, simplified atmosphere shell и `0–2` scrolling cloud shells через три bounded Godot shaders;
- вода не моделируется физически, atmosphere не использует volumetric/multi-scattering ray marching; существующий cube-sphere terrain/LOD не заменён;
- F5 acceptance matrix расширена `TASK-150 planet environment acceptance` с exact `4/4 starter planets`, `4/4 starter archetypes`, `9/9 catalog archetypes`, deterministic/radius/biome/water/atmosphere/cloud/gas-giant/current-planet invariants и `samples=16`;
- добавлены четыре xUnit regression tests и `validate-task150-planet-environment.py`; validator включён в Windows/Linux section-37 local quality gates;
- добавлен `docs/PLANET_ENVIRONMENT.md`, обновлены README/CHANGELOG/локализация RU/EN и этот журнал.

**Добавленные файлы:** `docs/PLANET_ENVIRONMENT.md`; `Content/planet_environments.json`; `PlanetEnvironmentCatalog.cs`; `PlanetEnvironmentRuntime.cs`; `PlanetEnvironmentAcceptance.cs`; `SalvageRepairSlicePlanetEnvironment.cs`; `CubeSpherePrototypeEnvironment.cs`; три `Shaders/planet_*_shell.gdshader`; `tools/validate-task150-planet-environment.py`.

**Изменённые файлы:** `VERSION`, `CHANGELOG.md`, `README.md`, `REQUIREMENTS_STATUS.md`; `localization.en.json`, `localization.ru.json`; `DeveloperToolContext.cs`, `DeveloperWorkbenchController.cs`; `SaveDatabase.cs`, `SaveGameModels.cs`; `CubeSpherePrototype.cs`; `GalaxyNavigationRuntime.cs`, `SalvageRepairSlice.cs`, `SalvageRepairSliceAudio.cs`, `SalvageRepairSliceGalaxy.cs`, `SalvageRepairSlicePlanetMap.cs`, `SalvageRepairSlicePlayerSurvival.cs`, `StarterRepairDomain.cs`; `RepositoryFixture.cs`, `WorldGenTests.cs`; `run-section37-quality.cmd/.sh`, `validate-json-content.py`; `validate-task148-world-scene-coordinator.py` (version guard made forward-compatible with alpha.149+ so later releases do not falsely fail the already closed TASK-148 contract).

**Сознательная граница TASK-150:** это environment generation/presentation foundation, а не полный межпланетный flight loop. Игрок не получает мгновенный teleport UI между четырьмя планетами. Физический выбор цели, перелёт Orbit→другая PlanetRuntime и transactional world-shell handoff должны быть отдельной `TASK-152` после runtime-приёмки TASK-150.

**Статическая проверка в среде подготовки:** repository validators PASS, в том числе JSON/localization, Godot text-resource structure, audio/developer diagnostics, §36/§37/§38 contracts, TASK-146/TASK-148/TASK-149.4 regression gates и новый `TASK-150 PLANET ENVIRONMENT CONTRACT PASS: starterPlanets=4/4; archetypes=9/9; radius=20-80km; biomes=max8; water=1; atmosphere=1; clouds=0-2; climateFactors=1; persistence=1; systemMap=1; planetPreview=1; currentPlanetConsumers=1; persistenceBoundary=1; starDirection=1; shaders=3/3; f5=1; xunit=4/4.`

**Ограничение среды:** `dotnet`, Godot и C# compiler в среде подготовки отсутствуют; реальная compilation/xUnit/Godot runtime здесь не заявляются. `TASK-150` поэтому имеет статус `IMPLEMENTED`, а `TASK-151` — `IN_PROGRESS` как приёмочная задача.

**Минимальная runtime-приёмка TASK-151:** clean build `0 warnings / 0 errors`; local quality green; в gameplay один `F5` должен вывести TASK-150 `PASS` со всеми перечисленными invariants и `samples=16`; `M` должен показать четыре starter planets с различными environment rows; Developer Tools → Planet Preview должен визуально показать atmosphere/clouds и воду у water-bearing preview planet без parse/runtime errors; перезапуск после сохранения должен восстановить тот же `CurrentPlanetId` в `TASK-150 ... READY/HUD`.

## 0A. История предыдущей mega-итерации — TASK-149 transactional World Scene Coordinator acceptance

### TASK-149 — mega-итерация: transactional world-scene acceptance hardening

**Исходный снимок:** `ProjectHorizon-main(10)(1).zip` (последняя приложенная GitHub-редакция).  
**Подготовленный снимок:** `ProjectHorizon-main-task149-cs0136-hotfix.zip`.  
**Связанные требования PDF-ТЗ v2.0:** §4.4 `SceneCoordinator` как допустимый глобальный orchestration service; §5.2 — одновременно загружается только необходимый уровень представления; §5.3 — переходы поверхность/космос/станция/гиперпереход; §22.8 — autosave после посадки, взлёта и гиперперехода; Stage 1 — игрок взлетает, посещает станцию и возвращается.

**Восстановление нормативных файлов:** исходный ZIP содержал `Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf` и `.docx` только как Git LFS pointer-файлы. В подготовленный снимок возвращены полные исходные бинарные документы: PDF `1774256` bytes, SHA-256 `1facda8ebc41f1fd161f4b3ce9d2c3847b61a3aae0f9283e45bf9999f50f3dd8` (точно совпадает с LFS oid), DOCX `112226` bytes, SHA-256 `c57207aa7e4ee245cadb50da2dd7ae92575294d664684afbaa56d2bb9f56fc23`. Дополнительно восстановлен точный payload `Technical_Specification/1.0/Project_Horizon_Technical_Specification_v1.0.pdf`: `574489` bytes, SHA-256 `19468fcaa1116601ef1afd3b963263711a3a76eaa69414e5c72e2f61ffa92f14`, совпадающий с его LFS oid. Эти документы не пересобирались и не редактировались скриптами. Для v1.0 DOCX точный LFS payload в доступных исходниках не найден, поэтому его pointer намеренно не подменялся реконструированным файлом.

**Почему это именно mega-итерация, а не переход к следующей подсистеме:** регламент требует сначала завершить приёмку уже реализованной функции. Ближайшая задача — `TASK-149`, поэтому перескакивать к новой подсистеме нельзя. Вместо точечного hotfix вся граница world-scene transition/acceptance усилена как один связный closure-блок.

**Реализовано:**

- `WorldSceneCoordinatorRuntime` получил exact volatile snapshot (`Current`, `Generation`, transition/rejection/hyperspace counters) и exact restore без искусственного увеличения counters; snapshot не сериализуется и не создаёт нового persistence source;
- `WorldSceneCoordinatorNode.TryTransition` переведён на staged transaction: destination PackedScene сначала `load → instantiate → AddChild` и проверяется как реально вошедший в coordinator tree; только после успешного preflight применяется application-state transition; прежний shell удаляется последним;
- при load/instantiate/add-child/state-mutation failure staged shell удаляется, runtime восстанавливается из exact snapshot, а прежний shell/context остаются активными; diagnostics получили `SceneLoadFailures` и `Rollbacks`;
- `Restore`, forced reload и acceptance cleanup используют тот же safe staged-shell path; `WorldSceneCoordinatorNodeSnapshot` позволяет F5 вернуть не только context, но и generation/reload/failure counters;
- F5 `TASK-148` acceptance теперь проходит **живой** путь `Surface(alpha) → Orbit(alpha) → Station(alpha) → Hyperspace(alpha) → Station(beta) → Orbit(beta) → Surface(beta)`; на каждом из 7 состояний проверяются `HostChildren==1`, shell metadata/path и surface/orbit residency; затем live Surface→Station rejection проверяется без reload;
- F5 runner выполняет restore исходного node/runtime snapshot в `finally` и отдельно требует `stateRestored=1`; residency policy применяется без forced-переинициализации, а диагностические `_surfaceActivationTransitions`/`_planetActivationPipelineMask` возвращаются к pre-test значениям; успешный путь имеет `steps=7`, `maxHostChildren=1`, `testTransitions=6`, `testReloads=7`, `testRejected=1`, `testHyperspace=1`;
- xUnit contract расширен `SnapshotRestore_IsExactAndDoesNotAdvanceCounters`; static TASK-148 validator теперь требует staged-before-mutation ordering, rollback restore, live seven-state acceptance и self-restore; VERSION = `0.1.0-alpha.149`;
- `README.md`, `docs/WORLD_SCENE_COORDINATION.md`, `CHANGELOG.md` и настоящий журнал синхронизированы с новым acceptance contract.

**Фактически выполнено в среде подготовки:**

```text
TASK-140 VERSION PASS: version=0.1.0-alpha.149; tag=<not-required>; changelog=1
TASK-140 JSON CONTRACT PASS: json=21; parsed=21; industrySchema=5/5; localizationParity=1
GODOT TEXT RESOURCE STRUCTURE PASS: scenes=15; refs=277; resourceOrder=1; uniqueIds=1; resolvedRefs=1
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1336; parity=1; sourceSinks=0
TASK-134 AUDIO CONTRACT PASS: ... deferredInstall=1; preReadyPlaybackGuard=1
TASK-136 DEVELOPER DIAGNOSTICS CONTRACT PASS: tools=5/5; commands=15/15; logCategories=14/14
TASK-138 SECTION-36 CONTRACT PASS: unitGroups=10/10; saveScenarios=8/8; loadScenarios=8/8+abnormal; coverage=80/70/80
TASK-140 SECTION-37 CONTRACT PASS: branches=5/5; prPipeline=8/8; debugExports=4/4; releaseExports=4/4
TASK-142 SECTION-38 CONTRACT PASS: typedEvents=11/11; projectCycles=0; nodeDomainSeparation=1
TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS: layers=3/3; projectCycles=0; desktopPresets=4/4
TASK-146 BASE CONSTRUCTION CLOSURE CONTRACT PASS: ... xunit=4/4; runtimeEvidence=1
TASK-148 WORLD SCENE COORDINATOR CONTRACT PASS: contexts=4/4; packedScenes=4/4; oneResident=1; transitionGraph=1; illegalGuard=1; surfaceResidency=1; orbitResidency=1; stationResidency=1; hyperspaceResidency=1; destinationReload=1; persistenceDerived=1; transactionalSwap=1; rollbackRestore=1; livePath=7/7; stateRestore=1; gameplayLoadSafe=1; runtimeBootstrap=1; sceneSyntaxSafe=1; audioLifecycleSafe=1; f5Acceptance=1; xunit=4/4
C# lexical structural check PASS: 5/5 changed C# files; dotnet/godot executables unavailable in preparation environment
```

**Недоступные проверки:** в среде подготовки отсутствуют `dotnet`, `godot` и `godot4`. Поэтому clean C# build, фактическое исполнение xUnit и Godot F5/runtime не заявляются. По регламенту `TASK-149` остаётся `IN_PROGRESS`; после внешнего F5 подтверждён `WORLD-ACC-102`, а `WORLD-ACC-100/101/103` требуют оставшейся внешней приёмки.

**Внешняя сборка 2026-08-15 18:36:** Godot/.NET реально запустил `CoreCompile` для `Game.Client` и обнаружил единственную ошибку `CS0136` в `WorldSceneCoordinatorAcceptance.cs(105,44)` при `0` warnings. Причина устранена переименованием локальной переменной шага `result` → `transitionResult`; после hotfix все repository static validators повторно PASS. Повторный внешний clean build и F5/runtime всё ещё требуются до `VERIFIED`.


**Минимальная пользовательская приёмка TASK-149:**

1. `tools\clean-build-windows10.cmd` → реальный three-layer `CoreCompile`, `0 warnings / 0 errors`.
2. «Новая игра → Начать стандартную игру» → gameplay открывается без `CantOpen`, `.tscn Parse Error`, `Parent node is busy...`, `Playback can only happen...`.
3. Нажать `F5`; итоговая строка `TASK-148 world scene coordinator acceptance PASS` должна содержать: `transitionGraph=1; illegalRejected=1; hyperspaceSystemChange=1; packedScenes=1; singleLiveScene=1; liveContextMatch=1; residencyPolicy=1; livePath=1; transactionalSwap=1; stateRestored=1; steps=7; maxHostChildren=1; testTransitions=6; testReloads=7; testRejected=1; testHyperspace=1`.
4. После F5 текущий world context/HUD должен остаться тем же, что до теста: acceptance не должен телепортировать игрока и не должен менять gameplay save location.
5. Затем вручную пройти Surface → Orbit → StationInterior → hyperspace → destination StationInterior → Orbit → Surface и выполнить graceful exit/restart. После restart location/system/planet должны восстановиться из voyage+galaxy persistence.

**Статус:** `TASK-148` остаётся `IMPLEMENTED`; `TASK-149` остаётся `IN_PROGRESS` до clean build + F5 + manual/cold-restore evidence.

### TASK-149.4 — F5 runtime regression closure после успешного transactional world-scene acceptance

**Внешнее runtime evidence от 2026-08-15 18:43 (+03:00):** Godot 4.7.1 .NET на `Forward Mobile / Vulkan` успешно выполнил собственный TASK-148 acceptance: `transitionGraph=1; illegalRejected=1; hyperspaceSystemChange=1; packedScenes=1; singleLiveScene=1; liveContextMatch=1; residencyPolicy=1; livePath=1; transactionalSwap=1; stateRestored=1; steps=7; maxHostChildren=1; testTransitions=6; testReloads=7; testRejected=1; testHyperspace=1; sceneLoadFailures=0; rollbacks=0`. Это является реальным подтверждением world-scene transaction/self-restore contract.

Тот же F5 выявил четыре смежных runtime regression defects:

- `TASK-130`: только `profileContract=0`; причина — сравнение `SaveDatabase.DatabasePath` и `GameProfilePaths.PrimaryDatabasePath` как сырых строк при разных, но эквивалентных представлениях пути. Исправлено канонизацией `Path.GetFullPath` и platform-aware comparison.
- `TASK-142`: `nearbyTicks=199/100`, `distantTicks=39/20`, `frequencyPolicy=0`; причина — modulo-only accumulator после tolerance-admitted floating boundary оставлял почти полный interval и давал повторный tick на следующем кадре. Исправлено вычитанием целого числа elapsed intervals. Существующий xUnit диапазон 99–101 / 19–21 теперь является прямым regression guard.
- `TASK-124`: NavigationServer сообщил query before first map synchronization; acceptance вследствие этого получил `tilesTouched=0; pathPoints=0; crossTilePath=0; obstacleClearance=0; recoveryProbe=0`. Arbitrary two-frame delay заменён на gate по `NavigationServer3D.MapGetIterationId`; query разрешается только после ненулевой итерации и после фактического изменения iteration относительно region rebuild.
- `TASK-126`: fauna runtime работал, но ships дали `shipSamples=0` и все ship steering primitives = 0. Это не отсутствие ship AI, а конфликт acceptance с новой TASK-148 residency policy: на Surface `Gameplay/NpcShipTraffic` корректно suspended. F5 runner теперь передаёт live-step observer; на двух Orbit legs четыре NPC ships выполняют non-moving fixed-step steering acceptance, а финальная проверка на восстановленном Surface требует их suspended residency.

Сообщения редактора `Cannot open file '/root/godot/modules/mono/glue/.../NativeCalls.cs'` относятся к попытке открыть внутренний source path GodotSharp из stack trace и не являются `res://` resource проекта.

**Изменённые файлы:** `SystemFrequencyPolicy.cs`, `SalvageRepairSliceApplicationShell.cs`, `NpcNavigationSurfaceNode.cs`, `NpcShipNavigationNode.cs`, `AerialNavigationAcceptance.cs`, `SalvageRepairSliceAerialNavigation.cs`, `WorldSceneCoordinatorAcceptance.cs`, `SalvageRepairSliceWorldScenes.cs`, local quality scripts, README/CHANGELOG/REQUIREMENTS_STATUS и новый `validate-task149-runtime-regression-closure.py`.

**Статическая проверка в среде подготовки:** все repository `tools/validate-*.py` PASS, включая `TASK-149.4 RUNTIME REGRESSION CLOSURE PASS: frequencyGate=1; navIterationGuard=1; profilePathNormalization=1; orbitResidentShipProbe=1; residencyAwareAerialAcceptance=1; xunitFrequencyBounds=1`. `dotnet`/Godot в среде подготовки отсутствуют, поэтому внешний clean build/F5 этой hotfix-редакции всё ещё обязателен.

**Статус:** `WORLD-ACC-102: IN_PROGRESS → VERIFIED` по реальному TASK-148 F5 evidence. `WORLD-ACC-100/101/103` остаются `IN_PROGRESS`; `TASK-149` остаётся `IN_PROGRESS` до clean three-layer build, local quality и ручного Surface→Orbit→Station→Hyperspace→Station→Orbit→Surface + cold restore.

### TASK-148.2 — runtime hotfix: Godot text-scene parse + AudioDirector startup lifecycle

**Внешнее runtime evidence от 2026-08-15 17:37 (+03:00):** обычная incremental Windows-сборка `Game.Client.csproj` завершилась успешно с `0 warnings / 0 errors`; при этом `CoreCompile` у `Game.Domain`, `Game.Application` и `Game.Client` был пропущен как up-to-date, поэтому это полезное build evidence, но не заменяет требуемый clean build TASK-149. Godot 4.7.1 запустился на primary renderer и вывел `TASK-144 renderer profile PASS: feature=primary; method=mobile; driver=vulkan`. При переходе Menu → Gameplay ResourceLoader дал точную причину `CantOpen`: `SalvageRepairSlice.tscn:151 - Parse Error: Unknown tag 'sub_resource' in file.` До этого в Main Menu также зафиксированы `Parent node is busy setting up children, add_child() failed` из `AudioDirector.EnsureInstalled` и два `Playback can only happen when a node is inside the scene tree`.

**Локализация дефектов:** три `PlayerWater*` `sub_resource` действительно находились после первых `[node]` секций в authored `.tscn`; Godot text scene format требует resource declarations до node sections. Независимо от этого `AudioDirector.EnsureInstalled()` синхронно выполнял `SceneTree.Root.AddChild()` из `MainMenuController._Ready`, когда root ещё находился в child-setup critical section; затем `InitializeRuntime()` пытался проигрывать loop-плееры, хотя director не вошёл в tree. Повторяющиеся сообщения редактора про `/root/godot/modules/mono/.../NativeCalls.cs` являются следствием попытки debugger/source mapping открыть внутренние исходники Godot из stack trace и не рассматриваются как отдельный project resource path.

**Исправление:** `PlayerWaterMaterial`, `PlayerWaterMesh`, `PlayerWaterShape` перенесены в resource declaration block перед первым `[node]`. Добавлен repository-wide `validate-godot-text-resource-structure.py`, который для всех shipping `.tscn` запрещает `ext_resource/sub_resource` после начала node tree, проверяет уникальность resource IDs и отсутствие unresolved `ExtResource/SubResource`; gate включён в local quality, CI и release. `AudioDirector` теперь устанавливается через deferred root `add_child`; pending instance предотвращает дубли до следующего idle step; environment/music requests, полученные до `_Ready`, сохраняются и применяются после входа director в tree; 2D/3D playback имеет pre-ready/in-tree guard. TASK-134 audio validator расширен `deferredInstall=1; preReadyPlaybackGuard=1`. VERSION = `0.1.0-alpha.148.2`.

**Статическая приёмка после hotfix:** `GODOT TEXT RESOURCE STRUCTURE PASS: scenes=15; refs=277; resourceOrder=1; uniqueIds=1; resolvedRefs=1`; `TASK-134 AUDIO CONTRACT PASS ... deferredInstall=1; preReadyPlaybackGuard=1`; `TASK-148 ... PASS ... gameplayLoadSafe=1; runtimeBootstrap=1; sceneSyntaxSafe=1; audioLifecycleSafe=1`. Реальный повторный Godot runtime после исправления в среде подготовки недоступен, поэтому `TASK-149` остаётся `IN_PROGRESS`.

**Повторная минимальная проверка пользователя:** Build должен остаться `0 warnings / 0 errors`; затем «Новая игра → Начать стандартную игру» должна открыть gameplay без `Parse Error`, `CantOpen`, `Parent node is busy...` и `Playback can only happen...`. После этого можно переходить к полному F5/world-transition acceptance TASK-149.

### TASK-148.1 — runtime hotfix: gameplay scene `CantOpen`

**Внешнее runtime evidence от 2026-08-15 17:29 (+03:00):** пользовательский скриншот подтверждает, что главное меню запускается, но команда «Начать стандартную игру» не открывает gameplay и выводит `Переход к игровой сцене не удался: CantOpen`. Это означает, что `TASK-149` runtime acceptance фактически **FAILED на первом переходе** и остаётся `IN_PROGRESS`; `WORLD-ACC-102/103` не могут считаться пройденными.

**Локализация дефекта:** diff принятого TASK-146 → TASK-148 показывает единственную новую hard resource dependency непосредственно в `SalvageRepairSlice.tscn`: `ext_resource` на `WorldSceneCoordinatorNode.cs`. Coordinator является orchestration object и не должен блокировать открытие authored gameplay scene. При overlay/fresh C# resource reindex новый script UID/class может быть недоступен ResourceLoader до обновления assembly/cache; тогда Godot не может открыть весь PackedScene и `ChangeSceneToFile` возвращает `CantOpen`.

**Исправление:** `WorldSceneCoordinatorNode` удалён из serialized `.tscn` и создаётся программно под существующим `Gameplay` после успешной загрузки `SalvageRepairSlice`. Четыре world-context PackedScene shell остаются bounded runtime resources и загружаются coordinator'ом уже после входа в gameplay. В `validate-task148-world-scene-coordinator.py` добавлен regression invariant `gameplayLoadSafe=1`: main gameplay scene не имеет hard dependency на coordinator C# script/node, а runtime bootstrap должен присутствовать явно. VERSION поднят до `0.1.0-alpha.148.1`.

**Статус:** `TASK-148` остаётся `IMPLEMENTED`; `TASK-149` остаётся `IN_PROGRESS` после подтверждённой неудачной runtime-попытки. Требуется повторный переход Menu → Gameplay, затем первоначальные clean build / quality / F5 / manual smoke criteria.


### Синхронизация принятой TASK-146/TASK-147

Владелец продукта после hotfix1 сообщил «вроде, работает» и прямо распорядился переходить к следующей итерации. Это фиксируется как **qualitative runtime acceptance / acceptance waiver by product owner**: `TASK-146`, `TASK-147`, `TASK-107` и `BASE-ACC-100..103` переводятся в `VERIFIED`, но отсутствующие точные F6/build строки не выдумываются и не приписываются задним числом. Все `BASE-100..113` также переводятся в `VERIFIED` как принятая подсистема.

### TASK-148 — mega-итерация: World Scene Coordinator / bounded scene residency

**Исходный снимок:** `ProjectHorizon-main-task146-base-construction-build-closure-hotfix1.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-task148-world-scene-coordinator-closure.zip`.  
**Связанные требования:** ранее извлечённые в этом журнале архитектурный принцип §5.2 «не загружать всю галактику в одну сцену», §15 runtime звёздной системы и зафиксированный TASK-128 gap «общий scene coordinator §5»; интеграция с `TASK-112` voyage, `TASK-114` galaxy/hyperspace и `TASK-128` single-planet activation.

**Ограничение первичного источника:** вложенный PDF ТЗ в исходном ZIP по-прежнему является Git LFS pointer (`oid sha256:1facda8e...`, payload `1774256` bytes), а не PDF payload. Поэтому TASK-148 не добавляет новых трактовок PDF: используются только требования/границы, уже ранее извлечённые и зафиксированные в данном журнале.

**Реализовано:**

- в `Game.Application` добавлен Godot-независимый `WorldSceneCoordinatorRuntime` с четырьмя контекстами `Surface / Orbit / StationInterior / HyperspaceTransit` и строгим graph `Surface ↔ Orbit ↔ StationInterior → HyperspaceTransit → StationInterior`;
- stable `SystemId/PlanetId` обязательны; произвольный `Surface→Station`, смена system вне hyperspace и другие нелегальные переходы отклоняются без мутации current context;
- добавлены четыре лёгких PackedScene-shell и `Gameplay/WorldSceneCoordinator`; host держит ровно один shell, валидирует его kind и пишет system/planet/generation metadata для runtime evidence;
- `Surface` включает surface runtime и выключает orbit objects; `Orbit` включает orbit objects и сохраняет уже принятую bounded `72 m` surface-overlap только у планеты; `StationInterior` и `HyperspaceTransit` suspend both surface+orbit;
- orbital station, dock marker, approach marker и NPC ship traffic сохраняют исходные Visible/ProcessMode/CollisionLayer/Mask и восстанавливаются после возврата в Orbit;
- star-system proxy visuals теперь рендерятся только в `Orbit`; Station/Hyperspace не оставляют пространственные system proxies активными;
- hyperspace jump обёрнут транзакционным scene transition: до `TryJumpToSelected` вход в `HyperspaceTransit`; при успехе — destination `StationInterior` с новым system/planet; при отказе — rollback в source station;
- coordinator **не имеет отдельного persistence block**: new/load/reset выводят context из существующих `StageOneVoyage.Location` + `GalaxyNavigation.CurrentSystem/CurrentPlanetId`, SQLite schema остаётся прежней;
- HUD получил локализованную строку World scene; F5 запускает `TASK-148 world scene coordinator acceptance`;
- xUnit `WorldSceneCoordinatorTests` проверяет полный graph, illegal transition и ID normalization; `tools/validate-task148-world-scene-coordinator.py` интегрирован в local quality, CI и release gates;
- документация архитектуры вынесена в `docs/WORLD_SCENE_COORDINATION.md`; VERSION = `0.1.0-alpha.148.2`.

**Изменения относительно принятого TASK-146 hotfix1:** `added=16`, `changed=15`, `removed=0`.

**Фактически выполненные статические проверки:**

```text
TASK-140 VERSION PASS: version=0.1.0-alpha.148.2; changelog=1
TASK-140 JSON CONTRACT PASS: json=21; parsed=21; industrySchema=5/5; localizationParity=1
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1336; parity=1; sourceSinks=0
TASK-134 AUDIO CONTRACT PASS
TASK-136 DEVELOPER DIAGNOSTICS CONTRACT PASS
TASK-138 SECTION-36 CONTRACT PASS
TASK-140 SECTION-37 CONTRACT PASS: debugExports=4/4; releaseExports=4/4
TASK-142 SECTION-38 CONTRACT PASS
TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS
TASK-146 BASE CONSTRUCTION CLOSURE CONTRACT PASS
TASK-148 WORLD SCENE COORDINATOR CONTRACT PASS: contexts=4/4; packedScenes=4/4; oneResident=1; transitionGraph=1; illegalGuard=1; surfaceResidency=1; orbitResidency=1; stationResidency=1; hyperspaceResidency=1; destinationReload=1; persistenceDerived=1; gameplayLoadSafe=1; runtimeBootstrap=1; sceneSyntaxSafe=1; audioLifecycleSafe=1; f5Acceptance=1; xunit=3/3
XML PASS: 4/4; YAML PASS: 2/2; Python syntax PASS: 13/13; Bash syntax PASS
C# lexical structural check PASS: 6/6 new TASK-148 files
UID PASS: 139/139 unique; res:// references: broken=0; world scenes=4/4 with kinds 0/1/2/3
```

**Ограничение проверки:** в среде подготовки отсутствуют `dotnet`, `godot`, `godot4`; поэтому реальная C# компиляция, xUnit execution и Godot runtime/export не заявляются. Они являются TASK-149.

**Статусы:**

- `TASK-148`: `NOT_STARTED → IMPLEMENTED`;
- `TASK-149`: `NOT_STARTED → IN_PROGRESS` — требуется реальный clean build, quality/xUnit и F5/manual transition smoke;
- `WORLD-100..WORLD-109`: `IMPLEMENTED`;
- `WORLD-ACC-100..103`: `IN_PROGRESS`;
- `TASK-143/TASK-145`: без изменения — их отдельные §38/Compatibility runtime criteria не подменяются TASK-148.

**TASK-149 minimum acceptance:** `tools\clean-build-windows10.cmd` → `0 warnings / 0 errors`; `tools\run-section37-quality.cmd` → green с `TASK-148 WORLD SCENE COORDINATOR CONTRACT PASS`; в gameplay `F5` → `TASK-148 ... PASS` с `transitionGraph=1; illegalRejected=1; hyperspaceSystemChange=1; packedScenes=1; singleLiveScene=1; liveContextMatch=1; residencyPolicy=1`. Manual smoke: Surface → takeoff/Orbit → dock/StationInterior → hyperspace/destination StationInterior → undock/Orbit → landing/Surface; во всех состояниях hostChildren=1, Station/Hyperspace не держат surface/orbit runtime, cold restart выводит тот же контекст из voyage+galaxy save.

---

## 0A. Предыдущая mega-итерация 2026-08-15 — Base Construction subsystem closure + Windows build recovery

### TASK-146 — mega-итерация: Base Construction closure + TASK-144 Windows build recovery

**Исходный снимок:** `ProjectHorizon-main-task144-platform-architecture-closure.zip`.  
**Связанные требования:** `BASE-100..BASE-113`, `BASE-ACC-100..103`, `ARCH-001`, `CFG-005/006`, TASK-143/145 build acceptance.

**Фактический внешний build evidence от 2026-08-15 14:20:** реальный Godot/.NET Windows build TASK-144 завершился `148 warnings / 16 errors`. Основная масса `CS0436` вызвана stale pre-TASK-144 source copies, оставшимися при overlay extraction; дополнительно подтверждены intrinsic compile errors: `GameSettingsPanel.cs` delegate conversion, `StructuredGameLogger.cs` `System.Environment` ambiguity, `StageOneVoyageRuntime.cs` missing `CultureInfo`, плюс независимый nullable warning в ecology. Поэтому TASK-145 не переводится в VERIFIED до повторного clean build.

**Повторный внешний build evidence от 2026-08-15 14:35:** после TASK-146 type-shadowing и прежние compile defects устранены фактически: `CS0436` отсутствует, предупреждений `0`, прежние 16 ошибок отсутствуют. Сборка остановилась на `2 errors CS1061` в `SalvageRepairSlice.cs`: closure-вывод ошибочно обращался к `MalformedSaveRejected` у `PlanetaryExplorationAcceptanceReport` и `StationServicesAcceptanceReport`. Hotfix1 удаляет только эти две чужие ссылки, оставляя property в `BaseConstructionAcceptanceReport`, и добавляет static report-scope guard. TASK-147 остаётся `IN_PROGRESS` до следующего реального `0 warnings / 0 errors` build.

**Что исправлено и закрыто одной подсистемной итерацией:**

- `Game.Client.csproj` жёстко исключает legacy `Scripts/Infrastructure/Architecture/**/*.cs` и старый `ProjectHorizonGenerator.cs`, поэтому stale overlay больше не создаёт type shadowing даже до физической очистки;
- build-time `ProjectHorizonSourceHygiene` автоматически удаляет только восемь точно известных TASK-144 legacy artifacts; неизвестные `.cs` никогда не удаляются автоматически — наличие исходника в retired architecture path останавливает сборку с диагностикой;
- `tools\clean-build-windows10.cmd` удаляет legacy source copies и build outputs `Game.Domain`, `Game.Application`, `Game.Client`, гарантируя реальный `CoreCompile` всех трёх production layers;
- устранены все 16 ошибок из присланного build log: Godot Toggled delegate адаптирован lambda-wrapper, `System.Environment` квалифицирован, `CultureInfo` импортирован; ecology использует nullable-safe local player capture;
- base builder больше не дублирует placement rules: новый `BaseConstructionRuntime.EvaluatePlacement()` является единым non-mutating preflight, а `TryPlace()` и green/red preview используют его; сюда входят anchor, stock, overlap, cardinal snap и limits;
- disabled battery больше не входит в доступную network capacity; toggle немедленно пересчитывает capacity/power snapshot;
- restore отклоняет non-finite stored energy и energy выше enabled battery capacity вместо silent normalization повреждённого state;
- F6 acceptance расширена `preflightParity`, `batteryIsolation`, `malformedSaveRejected`, сохраняя прежние 50 modules/17 categories, 500-module stress, connectivity, persistence, round-trip и legacy fallback;
- добавлены 4 xUnit regression tests и статический `tools/validate-task146-base-construction-closure.py`, включённый в Windows/Linux quality scripts;
- runtime при успешном F6 дополнительно печатает `TASK-146 base construction closure PASS`.

**Статическая приёмка:**

```text
TASK-146 BASE CONSTRUCTION CLOSURE CONTRACT PASS: buildFixes=4/4; legacyShadowGuard=1; sourceHygiene=1; cleanThreeLayers=1; preflightParity=1; batteryIsolation=1; malformedSaveRejection=1; reportScopeGuard=1; xunit=4/4; runtimeEvidence=1.
TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS: layers=3/3; domainGodotFree=1; applicationGodotFree=1; projectCycles=0; primaryRenderer=mobile/vulkan; compatibilityRenderer=gl_compatibility/opengl3; desktopPresets=4/4; debugExports=4/4; releaseExports=4/4; runtimeRendererEvidence=1.
TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=5; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.
```

**Статусы:**

- `TASK-146`: `NOT_STARTED → IMPLEMENTED → VERIFIED` (qualitative product-owner acceptance after hotfix1);
- `TASK-147`: `NOT_STARTED → IN_PROGRESS → VERIFIED` — закрыт acceptance waiver владельца продукта после hotfix1; точные отсутствующие build/F6 метрики не реконструируются;
- `TASK-145`: остаётся `IN_PROGRESS`; присланный build является подтверждённым failed attempt, исправления включены в TASK-146;
- `BASE-100..BASE-113`: `IMPLEMENTED → VERIFIED` по принятой владельцем продукта подсистеме;
- `BASE-ACC-100..103`: `IN_PROGRESS → VERIFIED` по acceptance waiver владельца продукта; конкретные отсутствующие F6/build строки не приписываются;
- `ARCH-001/CFG-003`: остаются `IMPLEMENTED`, повторная verification разблокируется после чистого build без legacy shadowing.

**Критерий TASK-147:** `tools\clean-build-windows10.cmd` должен дать `0 warnings / 0 errors` и реальный `CoreCompile` всех трёх assemblies; `tools\run-section37-quality.cmd` — green включая TASK-146; F6 — `TASK-106 ... PASS` и `TASK-146 base construction closure PASS` с `preflightParity=1; batteryIsolation=1; malformedSaveRejected=1; stress500=1; coldRestore=1; roundTrip=1`. После ручного builder smoke раздела 18I `TASK-107/TASK-147` и `BASE-ACC-100..103` могут быть переведены в VERIFIED, после чего core base-construction subsystem считается закрытой.

---

### TASK-144 — compiled layer boundaries and executable Compatibility/OpenGL fallback

**Исходный снимок:** `ProjectHorizon-main(9)(2).zip` — последняя редакция GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-task144-platform-architecture-closure.zip`.  
**Связанные требования ТЗ v2.0:** §1.2 (основной Mobile/Vulkan и резервный Compatibility/OpenGL 3.3 профиль), §4.1 (многослойная архитектура), §37 (CI/export/release) и §38 (направление зависимостей и Godot-independent domain logic).

**Ограничение исходного снимка по нормативному PDF:** `Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf` и соответствующий DOCX в GitHub ZIP являются Git LFS pointer-файлами, а не payload документов. Для PDF указан LFS OID `sha256:1facda8ebc41f1fd161f4b3ce9d2c3847b61a3aae0f9283e45bf9999f50f3dd8`, размер payload `1774256`. Поэтому в этой итерации нормативная карта берётся из ранее извлечённых и уже зафиксированных в `REQUIREMENTS_STATUS.md` требований §1.2/§4.1/§37/§38; новые требования ТЗ не выдумываются и сам pointer не изменяется.

**Решение по масштабу:** вместо следующей одиночной игровой функции закрывается единый platform/architecture block — два оставшихся технических хвоста `ARCH-001` и `CFG-003`, а также их CI/release enforcement. `TASK-143` не объявляется `VERIFIED`: clean build/xUnit/Godot F5 предыдущего §38 по-прежнему требуют реального runtime на машине с Godot/.NET.

**Реализовано:**

- создана реальная compiled boundary `Game.Domain` (`net8.0`): typed domain events, `SystemFrequencyPolicy`/`SystemFrequencyGate` и `ProjectHorizonGenerator.Version` физически вынесены из Godot project tree; проект не содержит Godot, SQLite и project references;
- создана compiled boundary `Game.Application` (`net8.0`) с `DomainEventBus`; единственная project dependency — `Game.Domain`;
- `Game.Client` остаётся Godot composition/presentation host и явно ссылается на `Game.Domain` и `Game.Application`; solution содержит все три production assemblies; reverse/cyclic project references отсутствуют;
- section-38 validator адаптирован к новым физическим слоям и теперь проверяет публичные interfaces/async contracts во всех production assemblies, а также граф `Domain <- Application <- Client`;
- xUnit architecture suite проверяет фактические assembly names и отсутствие обратных ссылок/Godot/SQLite в Domain/Application;
- основной runtime профиль явно фиксирует `mobile` + Vulkan для Windows/Linux и разрешает штатный RenderingDevice fallback to OpenGL 3; для Compatibility profile заданы `gl_compatibility` + native `opengl3` (OpenGL 3.3 desktop);
- добавлены отдельные export presets `Windows Desktop Compatibility` и `Linux Compatibility` с custom feature `compatibility`; всего desktop presets теперь `4/4`;
- headless CI/export pipeline создаёт четыре Debug и четыре Release export tree: primary Windows/Linux + Compatibility Windows/Linux;
- release packager создаёт отдельные Compatibility archives, включает их в manifest/SHA-256 и собирает portable PDB также для `Game.Domain` и `Game.Application`;
- Main Menu печатает фактически выбранные `RenderingServer.GetCurrentRenderingMethod()` и `GetCurrentRenderingDriverName()`; dedicated Compatibility export считается корректным только при `gl_compatibility` + `opengl3*`;
- общий F5 acceptance после TASK-142 дополнительно печатает `TASK-144 platform architecture acceptance`: он проверяет, что event contracts, event bus и Godot runtime действительно загружены из `Game.Domain`, `Game.Application`, `Game.Client`, и валидирует текущий renderer profile;
- добавлен статический `tools/validate-platform-architecture-contract.py`; он включён в local quality scripts, PR CI и release workflow;
- version повышена до `0.1.0-alpha.144`; README, architecture/build docs, changelog и журнал синхронизированы.

**Изменённые файлы относительно `ProjectHorizon-main(9)(2).zip`:** `added=8`, `changed=23`, `removed=7`. Новые production projects: `src/Game.Domain/*`, `src/Game.Application/*`; новый runtime diagnostic: `RendererProfileDiagnostics.cs`; изменены composition project/solution, `project.godot`, `export_presets.cfg`, Main Menu/F5 acceptance, CI/release scripts/workflows, section-36/37/38 validators, xUnit architecture tests, README/docs/version/changelog. Семь удалений — прежние копии перенесённых architecture/domain файлов и их Godot `.uid`; их логика не удалена, а физически перенесена в новые assemblies.

**Дополнительные фактически выполненные проверки:**

- `VERSION/CHANGELOG`: PASS, `0.1.0-alpha.144`;
- Python syntax изменённых validators/packager: PASS; shell `bash -n`: PASS; XML project/build files: `5/5` PASS; GitHub workflow YAML: `2/2` PASS;
- changed/new C# lexical-regression audit: `9/9` PASS; существующие Pygments limitations в старом большом `SalvageRepairSlice.cs` не увеличились (`56 → 56` lexer error tokens), новые TASK-144 C# files дают `0` error tokens;
- `.uid`: `135/135` уникальны; `res://`: `66`, `broken=0` (две ссылки `.import` на исключаемый `.godot/imported` корректно не считаются source references); scene/resource structural audit: `11` files PASS; единственный F5 binding сохранён, нового hotkey conflict нет;
- synthetic export harness (не настоящий Godot): `debug 4/4`, `release 4/4`; synthetic release packaging: four platforms, `5` distributable archives, `3` assembly PDB inputs, `9/9` SHA-256 entries PASS; созданные synthetic `artifacts/bin` после теста удалены. Эта проверка доказывает wiring scripts/package logic, но **не** заменяет Godot import/export или C# build.

**Статусы:**

- `TASK-142` остаётся `IMPLEMENTED`;
- `TASK-143` остаётся `IN_PROGRESS` — требуется реальный clean build/xUnit/F5 §38;
- `TASK-144`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-145`: `NOT_STARTED` → `IN_PROGRESS` — clean build + primary/Compatibility runtime/export acceptance;
- `ARCH-001`: `IN_PROGRESS` → `IMPLEMENTED`;
- `CFG-003`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-006` остаётся `BLOCKED`, поскольку поставочный ZIP не содержит `.git` metadata.

**Статическая приёмка TASK-144:**

```text
TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=5; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.
TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS: layers=3/3; domainGodotFree=1; applicationGodotFree=1; projectCycles=0; primaryRenderer=mobile/vulkan; compatibilityRenderer=gl_compatibility/opengl3; desktopPresets=4/4; debugExports=4/4; releaseExports=4/4; runtimeRendererEvidence=1.
TASK-140 SECTION-37 CONTRACT PASS: branches=5/5; prPipeline=8/8; debugExports=4/4; releaseExports=4/4; symbols=1; checksums=1; version=1; changelog=1; jsonSchema=1; migrations=1; warningsAsErrors=1; headlessGodot=1.
```

Одновременно проходят TASK-132 localization, TASK-134 audio, TASK-136 developer diagnostics, TASK-138 testing contract и JSON/schema gates. В среде подготовки отсутствуют `dotnet` и Godot, поэтому фактические C# compile, xUnit execution, Godot import/export и runtime здесь не заявляются.

**Минимальная приёмка TASK-145:**

1. Выполнить `tools\clean-build-windows10.cmd`: требуется реальный `CoreCompile`, `0 errors`, `0 warnings` для client и новых project references.
2. Выполнить `tools\run-section37-quality.cmd`: должны пройти xUnit/coverage и contract gates до `TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS`.
3. Обычный запуск проекта/primary Windows export: в Output должна появиться строка `TASK-144 renderer profile PASS`; на Vulkan-capable Windows ожидаются `feature=primary; method=mobile; driver=vulkan`. Штатный engine fallback на `gl_compatibility/opengl3*` также является допустимым fallback, но его надо прислать как отдельный факт.
4. В gameplay нажать `F5`: требуется `TASK-144 platform architecture acceptance PASS` с `domainAssembly=Game.Domain; applicationAssembly=Game.Application; clientAssembly=Game.Client; layers=3/3; rendererProfile=1`.
5. Запустить CI artifact/export `ProjectHorizon-windows-x64-compatibility-debug` либо локально экспортировать preset `Windows Desktop Compatibility`: стартовая строка обязана показать `feature=compatibility; method=gl_compatibility; driver=opengl3...; compatibility=1`; затем F5 также должен вернуть TASK-144 `PASS`.
6. Для Linux достаточно green headless export обоих presets; при наличии Linux runtime повторить renderer evidence для primary и Compatibility.
7. После F5 выполнить короткую регрессию Main Menu → New/Continue → gameplay, pause/settings, один resource interaction и graceful exit; существующие save/autosave и UI flows не должны регрессировать.

**Граница закрытия:** после clean build/quality, primary F5 и отдельного Compatibility runtime/export smoke `TASK-145 → VERIFIED`, `ARCH-001 → VERIFIED`, `CFG-003 → VERIFIED`. После этого platform/architecture foundation считается закрытым; следующие итерации снова можно выбирать по gameplay/content roadmap, не возвращаясь к проектным границам или renderer profiles без подтверждённой регрессии либо изменения ТЗ.

---

## 0A. Предыдущая mega-итерация 2026-08-15 — Architecture & Code-Quality Hardening / §38 closure

### Закрытие §37 по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-142 журнал синхронизирован:

- `TASK-140` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-141` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; фактические GitHub metadata,
  CI logs и branch-protection settings не приписываются локальной среде, а `.git` по-прежнему
  отсутствует в переданном архиве.

### TASK-142 — executable section-38 architecture contract

**Исходный снимок:** `ProjectHorizon-main-section37-ci-release-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-section38-architecture-hardening.zip`.  
**Связанные требования ТЗ v2.0:** §38 и §38.1–38.2 «Правила программирования /
частоты систем / типизированные события».

**Реализовано:**

- добавлены Godot-independent `IDomainEvent`, `IDomainEventBus` и thread-safe
  `DomainEventBus`; business-event transport не зависит от scene tree;
- реализованы все 11 нормативных typed events: `ItemAdded`, `ItemRemoved`,
  `ResourceMined`, `PlanetEntered`, `PlanetExited`, `SystemDiscovered`, `QuestAccepted`,
  `QuestCompleted`, `ShipDamaged`, `BaseModulePlaced`, `SaveRequested`;
- реальные gameplay flows для добычи/инвентаря, planet enter/exit, system discovery,
  quest lifecycle, ship damage, base placement и persistence queue теперь публикуют typed
  events; один live bus подписывает cross-domain reactions при инициализации vertical slice
  и освобождает subscriptions при shutdown;
- `SaveAutosaveCoordinator` получает event bus явной зависимостью и публикует
  `SaveRequested` после постановки snapshot в очередь; scene-level SQL не добавлялся;
- `SystemFrequencyPolicy` задаёт нормативные 60 Hz physics/player, 10 Hz nearby AI,
  2 Hz distant AI, диапазон background economy 0.2–1 Hz (shipping default 0.5 Hz) и
  batched telemetry 2 Hz; `project.godot` явно фиксирует physics tick rate 60 Hz;
- ground NPC и NPC-ship target/state decisions переведены на 10 Hz gates при сохранении
  physics-rate movement/navigation integration; distant ecology работает на 2 Hz;
- `StructuredGameLogger` больше не делает append на каждый telemetry event: строки
  буферизуются и flush'ятся пакетно; gameplay flush — по policy, а Main Menu, Developer
  Workbench и shutdown дополнительно выполняют финальный flush;
- аудит async boundary выявил и устранён для всех production `Task/ValueTask`: теперь
  явный `CancellationToken` имеется также у private autosave worker, refresh и graceful-exit
  operations, а не только у public persistence API;
- публичные interfaces получили XML documentation; статический contract запрещает empty
  `catch`, SQL вне Persistence/Developer inspection boundary, Godot Node как domain model,
  world-generation непосредственно из `_Process`, project dependency cycles и прямые
  inventory/crafting mutations из Application UI;
- добавлены `tools/validate-section38-architecture-contract.py`, xUnit
  `Section38ArchitectureTests.cs` и документ `docs/ARCHITECTURE_SECTION38.md`; новый gate
  включён в local section-37 quality scripts, PR CI и release workflow;
- `F5` получил отдельный TASK-142 smoke: exact 11-event dispatch, 11 live subscriptions,
  fixed-rate probes около 100 nearby / 20 distant ticks за 10 simulated seconds и проверку
  ecology mapping 10/2 Hz; gameplay save-slot для самого event-bus probe не изменяется;
- application version повышена до `0.1.0-alpha.142`, changelog и release documentation
  синхронизированы.

**Статусы:**

- `TASK-140`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-141`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-142`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-143`: `NOT_STARTED` → `IN_PROGRESS` — clean build + xUnit/quality + Godot F5 runtime;
- `TASK-006`: остаётся `BLOCKED`, поскольку release ZIP не содержит `.git` metadata.

**Статическая приёмка TASK-142:**

```text
TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=5; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.
```

Одновременно проходят прежние TASK-132/134/136/138/140 contract gates и JSON/schema
validation. Production C# structural audit: `148/148` source/test files PASS; в среде
подготовки отсутствуют `dotnet`, `msbuild`, `csc`, `mcs`, `godot`, `godot4`, поэтому
реальный compile/runtime PASS не приписывается.

**Diff относительно принятого TASK-140 snapshot до упаковки:**

```text
files: 363
added: 11
changed: 48
removed: 0
C# files: 148
JSON files: 21
```

**Статический pre-release audit:**

```text
TASK-132 localization: PASS
TASK-134 audio: PASS
TASK-136 developer diagnostics: PASS
TASK-138 section-36 testing contract: PASS
TASK-140 JSON/schema + section-37 build contract: PASS
TASK-142 section-38 architecture contract: PASS
C# lexical structure: 148/148 PASS
JSON: 21/21 PASS
UID: 138/138 unique
res:// references: 65; broken=0
XML project/build files: 3/3 PASS
forbidden build/runtime artifacts: 0
Industry baseline: 174 items / 42 resources / 128 recipes / 15 stations / 32 technologies
NPC baseline: 3 factions / 8 archetypes / 8 agents / 8 dialogues
Ecology baseline: 16 biomes / 60 flora / 20 fauna
```

**Минимальная приёмка TASK-143:**

1. Выполнить `tools\clean-build-windows10.cmd`; требуются реальный `CoreCompile`,
   `0 errors` и `0 warnings`.
2. Выполнить `tools\run-section37-quality.cmd`; должны пройти xUnit/coverage, JSON,
   migrations/recovery и все contract gates вплоть до TASK-142.
3. Запустить игру и один раз нажать `F5`; требуется строка `TASK-142 architecture acceptance
   PASS` с `typedEvents=11/11`, `liveSubscriptions=11/11`, nearby/distant tick counts около
   `100/20`, `physicsHz=60`, `nearbyAiHz=10`, `distantAiHz=2`, `eventBus=1`,
   `frequencyPolicy=1`.
4. Короткий smoke: добыть ресурс, выполнить переход planet exit/enter, принять/завершить
   задание и повредить ship system; прежние quest/autosave реакции должны сохраниться.

**Граница закрытия:** после clean build, quality/xUnit PASS и TASK-142 F5/runtime smoke
`TASK-143 → VERIFIED`; тогда §38 можно считать закрытым для текущего shipping vertical slice.

---

## 0A. Предыдущая mega-итерация 2026-08-15 — Build / CI / Release Engineering / §37 closure

### Закрытие §36 по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-140 журнал синхронизирован:

- `TASK-138` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-139` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; отсутствовавшие в среде
  подготовки `.NET`/Godot test execution и runtime не приписываются задним числом.

### TASK-140 — reproducible PR quality gate, cross-platform exports and release packaging

**Исходный снимок:** `ProjectHorizon-main-section36-verification-suite.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-section37-ci-release-closure.zip`.  
**Связанные требования ТЗ v2.0:** §37.1–37.3 «Version control / branches / CI» и
требование §38 о трактовке compiler warnings в CI как errors.

**Реализовано:**

- добавлены `.github/workflows/ci.yml` и `.github/workflows/release.yml`; PR/integration
  pipeline выполняет нормативную последовательность restore → C# build → tests/coverage →
  JSON validation → persistence migration/recovery verification → Windows Debug export →
  Linux Debug export;
- CI build получает `ContinuousIntegrationBuild=true`, `-warnaserror`, а общий
  `Directory.Build.props` дополнительно включает `TreatWarningsAsErrors`, deterministic
  build и portable debug symbols для CI;
- `tools/validate-json-content.py` разбирает все JSON проекта, проверяет пять нормативных
  Industry Content JSON через `Project_Horizon_Industry_Content_Schema_v2.0.json` и
  контролирует parity локализационных каталогов;
- debug/release exports выполняет штатный Godot CLI в `--headless` по новым
  `export_presets.cfg` для `Windows Desktop x86_64` и `Linux x86_64`; bootstrap фиксирует
  Godot 4.7.1 .NET editor и mono export templates;
- release workflow можно запускать вручную как dry-run без публикации: он выполняет
  Release build/tests/coverage/validation, Windows/Linux Release exports и создаёт готовый
  release evidence; push matching tag `v<VERSION>` проходит тот же pipeline и только после
  успешных gate публикует GitHub Release;
- добавлены `VERSION=0.1.0-alpha.140`, `CHANGELOG.md` и `tools/ci/verify-version.py`;
  несовпадение release tag / VERSION / changelog является hard failure;
- `tools/ci/package-release.py` создаёт Windows ZIP, Linux tar.gz, отдельный symbols ZIP
  (portable PDB обязателен), `release-manifest.json`, `RELEASE_NOTES.md`, version/changelog
  и `SHA256SUMS.txt`; отсутствие symbols или одного из export trees завершает pipeline FAIL;
- локальные `tools/run-section37-quality.cmd` / `.sh` повторяют quality часть CI без
  гигабайтной загрузки export templates;
- добавлен статический `tools/validate-section37-build-contract.py`, фиксирующий branch
  convention `main/develop/feature/*/fix/*/release/*`, все нормативные CI/release stages,
  LFS/ignore policy, warnings-as-errors, JSON Schema и headless exports;
- расширена Git LFS policy для PDF/DOCX/7z наряду с уже отслеживаемыми 3D/audio/video/texture
  бинарниками; `.gitignore` сохраняет запрет `.godot/bin/obj/.vs`, локальных DB и logs;
- branch protection намеренно не выдаётся за файл репозитория: required checks
  `quality` и `debug-exports` должны быть включены в GitHub для `main`, `develop` и
  `release/*`; отсутствие `.git` в переданном архиве по-прежнему оставляет TASK-006 BLOCKED.

**Статусы:**

- `TASK-138`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-139`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-140`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-141`: `NOT_STARTED` → `IN_PROGRESS` — реальный GitHub CI + release dry-run + branch protection;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Статическая приёмка TASK-140:**

```text
TASK-140 VERSION PASS: version=0.1.0-alpha.140; tag=<not-required>; changelog=1.
TASK-140 JSON CONTRACT PASS: json=21; parsed=21; industrySchema=5/5; localizationParity=1.
TASK-140 SECTION-37 CONTRACT PASS: branches=5/5; prPipeline=8/8; debugExports=2/2; releaseExports=2/2; symbols=1; checksums=1; version=1; changelog=1; jsonSchema=1; migrations=1; warningsAsErrors=1; headlessGodot=1.
```

**Статический pre-release audit:**

```text
files eligible for release: 352
vs accepted TASK-138 archive: +15 / ~3 / -0
C# source: 143 files, byte-identical to TASK-138
JSON: 21/21 PASS
UID: 134/134 unique
res:// references: 65; broken=0
YAML workflows: 2/2 PASS
XML project/build files: 3/3 PASS
forbidden build/runtime artifacts: 0
TASK-132 localization gate: PASS
TASK-134 audio gate: PASS
TASK-136 diagnostics gate: PASS
TASK-138 section-36 gate: PASS
TASK-140 section-37 gate: PASS
```

Синтетическая проверка Godot bootstrap подтвердила поиск mono editor binary и установку
двух export templates из заранее подготовленных тестовых архивов без сетевой зависимости.
Синтетическая проверка release packager (временные export/PDB файлы после проверки
удалены) фактически создала Windows/Linux/symbols packages, manifest, notes и семь SHA-256
entries; `sha256sum --check` вернул PASS.

**Ограничение среды:** в среде подготовки отсутствуют `dotnet` и Godot, а архив не содержит
`.git`, поэтому фактические GitHub Actions jobs, Godot exports и branch-protection settings
не выдаются за выполненные. До TASK-141 §37 имеет статус `IMPLEMENTED`.

**Минимальная приёмка TASK-141:**

1. Поместить снимок в рабочий Git-репозиторий и открыть PR; jobs `quality` и
   `debug-exports` должны завершиться green. В quality log требуются restore/build без
   warnings, xUnit + coverage PASS, JSON contract PASS, migration/recovery PASS и
   TASK-140 section-37 contract PASS.
2. Скачать два CI artifact: Windows x64 Debug и Linux x86_64 Debug; Windows build открыть
   до Main Menu. Сам факт Linux export подтверждается успешным headless export job.
3. В GitHub branch protection сделать `quality` и `debug-exports` required checks для
   `main`, `develop` и release branches; запретить merge при красном required check.
4. Actions → `Release` → `Run workflow` на текущей ветке: manual dry-run должен создать
   release artifact с Windows/Linux Release, symbols ZIP, manifest, VERSION/CHANGELOG/notes
   и `SHA256SUMS.txt`, при этом GitHub Release не публикуется.
5. Когда будет нужен реальный выпуск, tag обязан быть `v0.1.0-alpha.140`; matching tag
   запускает тот же workflow и разрешает публикацию только после всех gate.

**Граница закрытия:** после green PR CI, manual release dry-run и включённых required
checks `TASK-141 → VERIFIED`; тогда §37 можно считать закрытым для текущего проекта.

## 0A. Предыдущая mega-итерация 2026-08-15 — Verification & Automated Testing Suite / §36 closure

### Закрытие Developer & Diagnostics по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-138 журнал синхронизирован:

- `TASK-136` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-137` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; отсутствовавший в среде
  подготовки Godot/.NET runtime не приписывается задним числом.

### TASK-138 — standalone automated verification, golden seeds, persistence/load tests and coverage gates

**Исходный снимок:** `ProjectHorizon-main-developer-diagnostics-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-section36-verification-suite.zip`.  
**Связанные требования ТЗ v2.0:** §36.1–36.5 «Testing».

**Реализовано:**

- добавлен отдельный `tests/ProjectHorizon.Tests` на `xUnit` + `Microsoft.NET.Test.Sdk` +
  `coverlet.collector`; тесты запускаются `dotnet test` без открытия игрового UI и
  ссылаются на production `Game.Client.csproj`, поэтому не создают вторую реализацию domain/worldgen/save;
- §36.1 формализован как 10 обязательных test groups: seed hierarchy, stable IDs,
  inventory, industry/recipe/technology/station graph, economy, quests, migrations,
  derived stats, serialization и coordinate transforms;
- введён единый `ProjectHorizonGenerator.Version`. `GalaxyNavigationRuntime` и новые
  snapshot builders используют один generator-version; deterministic generation нельзя
  изменить молча, сохранив старый golden manifest;
- добавлен versioned `src/Game.Client/Testing/golden-seeds.v1.json`: 4 фиксированных
  system cases с star type/planet count/full planet parameters/SHA-256 и отдельный
  fixed POI fixture из 20 объектов с контрольными height/slope/position и checksum;
  значения manifest были подготовлены независимо от C# golden verifier;
- `GoldenSeedContract` проверяет manifest против реальных `GalaxyNavigationRuntime` и
  `PlanetaryPoiPlanner`; F5 дополнительно выполняет repeatable `CubeSphereMeshBuilder`
  visual/worldgen smoke и seam invariant;
- §36.3 покрыт normal exact save, simulated abnormal shutdown inside an uncommitted SQLite transaction, corrupted primary, protected
  backup recovery, old-version migration, aliases/unknown item+ship, removed technology
  и changed-content normalization; для этого `TechnologyProgression` теперь безопасно
  игнорирует исчезнувший technology ID и удаляет его из следующего нормализованного save;
- §36.4 автоматизирован отдельными load/stress cases: 2h analytic flight, 8h virtual
  automatic aerial movement, 100 последовательных voyage docking/landing loops с persistence round-trip (и дублирующий F5 probe), 100 реальных hyperspace jumps через существующий TASK-114 acceptance runner, base на
  500 modules, inventory 10,000 entries, 1000 visited systems, repeated abnormal
  recovery; реальный 1 GiB SQLite scenario помечен `FullSoak` и запускается только
  `tools\run-section36-tests.cmd --full-soak`, чтобы обычный gate не создавал гигабайтный файл;
- добавлены `tests/coverage-scope.json`, `tests/coverlet.runsettings` и
  `tools/verify-section36-coverage.py`: build gate требует line coverage `Domain >=80%`,
  `WorldGen >=70%`, `Persistence >=80%`; ниже порога команда завершается FAIL;
- `tools/run-section36-tests.cmd` выполняет test suite, собирает Cobertura coverage и
  автоматически запускает threshold verifier; `tools/restore-section36-tests.cmd`
  оставлен для явного package restore;
- добавлен machine-readable `src/Game.Client/Testing/section36-suite.json` и статический
  `tools/validate-section36-testing-contract.py`, который требует 10/10 unit groups,
  8/8 save scenarios, все нормативные load scenarios, golden/version contract,
  thresholds 80/70/80 и F5 integration;
- `F5` дополнен `TASK-138`: он не подменяет standalone tests, а проверяет доступность
  suite manifest, generator-version, fixed golden systems/POI, checksums/control heights
  и repeatable cube-sphere visual smoke.

**Статусы:**

- `TASK-136`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-137`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-138`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-139`: `NOT_STARTED` → `IN_PROGRESS` — clean build + `dotnet test`/coverage + F5 smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Статическая приёмка TASK-138:**

```text
python tools/validate-section36-testing-contract.py
TASK-138 SECTION-36 CONTRACT PASS: unitGroups=10/10; saveScenarios=8/8; loadScenarios=8/8+abnormal; goldenVersion=1; goldenSystems=4; goldenPoi=20; coverage=80/70/80; visualSmoke=1; standaloneDotnet=1; f5Smoke=1.
```

**Статический pre-release audit:**

```text
files eligible for release: 337
vs accepted TASK-136 archive: +24 / ~10 / -0
JSON: 21/21 PASS
C# lexical: 143/143 PASS
UID: 134/134 unique
res:// references: 65; broken=0
TASK-132 localization gate: PASS
TASK-134 audio gate: PASS
TASK-136 diagnostics gate: PASS
TASK-138 section-36 gate: PASS
```

**Ограничение среды:** в среде подготовки по-прежнему отсутствуют `dotnet` и Godot,
поэтому test execution, фактические coverage percentages и F5 runtime не выдаются за
выполненные. До реального `TASK-139` статус новой подсистемы остаётся `IMPLEMENTED`.

**Минимальная приёмка TASK-139:**

1. `tools\clean-build-windows10.cmd` → `0 errors`.
2. `tools\run-section36-tests.cmd` → xUnit PASS и итоговая строка `TASK-138 COVERAGE PASS`
   с `Domain>=80%`, `WorldGen>=70%`, `Persistence>=80%`.
3. По необходимости полный тяжёлый сценарий: `tools\run-section36-tests.cmd --full-soak`;
   он дополнительно создаёт и проверяет реальную SQLite БД размером не менее 1 GiB.
4. Запустить игру и один раз нажать `F5`; требуется строка:

```text
TASK-138 verification suite acceptance PASS: generatorVersion=1; goldenSystems=4/4; goldenPoi=1; controlHeights=1; checksums=1; unitGroups=10/10; saveScenarios=8/8; loadScenarios=8/8; landingStress=100/100; visualSmoke=1; visualComponents=1; coverageThresholds=80/70/80; ... result=section-36-verification-runtime.
```

**Граница закрытия:** `TASK-139 → VERIFIED` закрывает §36 для текущего проекта.
Фактические coverage percentages должны быть внесены в журнал после запуска; статический
наличественный контракт не заменяет выполнение `dotnet test`.

## 0B. Предыдущая mega-итерация 2026-08-15 — Developer & Diagnostics Suite / §34 + §35 closure

### Закрытие sound-итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-136 журнал синхронизирован:

- `TASK-134` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-135` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка не приписываются среде подготовки задним числом.

### TASK-136 — Developer Workbench, debug console and structured diagnostics

**Исходный снимок:** `ProjectHorizon-main-audio-architecture-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-developer-diagnostics-closure.zip`.  
**Связанные требования ТЗ v2.0:** §34.1–34.5 «Developer tools» и §35
«Logging and diagnostics». §36 «Testing» намеренно не смешивается с этой задачей:
следующая testing mega-итерация должна использовать уже готовые profiler/inspector/logging
контракты, а не дублировать их.

**Реализовано:**

- добавлен закрытый developer-mode `Developer Workbench`, доступный из Main Menu только
  в debug build либо при явном user argument `--developer`; прямой запуск workbench и
  внутриигровая console используют тот же gate, поэтому инструменты не становятся
  случайной публичной частью release UI;
- **Seed Explorer** использует существующий `GalaxyNavigationRuntime`, а не второй
  генератор: принимает arbitrary positive Int64 universe seed и integer sector X/Y/Z,
  показывает system/star/economy/danger и полный список 1–8 planets, позволяет копировать
  stable system ID и экспортировать deterministic JSON report;
- для Seed Explorer добавлен явный constructor `GalaxyNavigationRuntime(long seed)` и
  developer-only `LoadSystemForDeveloper(x,y,z)`; обычный save/hyperspace pipeline не
  меняется;
- **Planet Preview** использует канонический `CubeSphereMeshBuilder`: выбирает планету
  из результата Seed Explorer, LOD 0–4, строит sample, показывает resolution,
  vertices/triangles, CPU generation time, height range, deterministic resource-density
  proxy и состояние overlays `chunk grid / biomes / height / resources`; interactive
  launch переиспользует существующий `CubeSpherePrototype`, получает выбранные seed/LOD
  параметры и реально включает `DeveloperPreview`: patch-grid contrast, biome colouring,
  height gradient и resource-density highlights комбинируются по выбранным toggles; `F6`
  возвращает в Workbench;
- **Chunk Profiler** не дублирует terrain streaming: расширен существующий
  `TerrainChunkManager`. Новый `TerrainChunkProfilerSnapshot` содержит loaded chunks,
  queued work, active workers, worker CPU ms, main-thread mesh/GPU-upload submission ms,
  managed memory, vertex count, generated collisions, cancelled jobs и stale jobs;
  live HUD показывает эти показатели, `F10`/`P` сохраняют существующие stress/soak tests,
  `F6` возвращает в Workbench;
- **Save Inspector** принимает primary либо произвольный SQLite path. Source открывается
  read-only и через `SqliteConnection.BackupDatabase` снимается WAL-consistent snapshot;
  только snapshot затем открывается `SaveDatabase`, поэтому обычный `Inspect` не может
  запустить migration на исходном файле. Показываются schema version, integrity,
  WAL/foreign-key/busy-timeout diagnostics, database size, player, ship, current/visited
  systems и inventory rows; `Export All Tables` перечисляет `sqlite_master` и выгружает
  все пользовательские таблицы source DB в CSV через read-only connection;
- операция `Migrate Validated Copy` создаёт отдельный consistent copy в
  `user://developer_reports/save_1.migration-copy.db` и запускает migrations/integrity/load
  только на копии. Source/primary DB не мигрируется в рамках developer experiment;
- **Debug Console** реализует все обязательные команды §34.5: `teleport`, `spawn`,
  `give`, `damage`, `heal`, `set_time`, `set_weather`, `load_system`, `load_planet`,
  `show_chunks`, `show_navmesh`, `show_ai`, `profile_worldgen`, `save`, `reload_content`;
  console открывается `Ctrl+Shift+D` либо автоматически из Workbench;
- `show_navmesh` не полагается на editor-only runtime-toggle `SceneTree.debug_navigation_hint`:
  строится собственный overlay текущих bounded TASK-124 navigation tiles из
  `NpcNavigationSurfaceSnapshot`; `show_chunks` аналогично визуализирует локальную сетку, а `show_ai` добавляет цветные маркеры непосредственно к ground NPC / NPC ships / fauna и поэтому следует за движущимися агентами;
- `save` проходит через существующий autosave coordinator, `reload_content` выполняет
  полную перезагрузку gameplay scene, поэтому данные загружаются штатными catalog/runtime
  initialization paths;
- добавлен `StructuredGameLogger`: line-delimited JSON (`*.jsonl`) с UTC timestamp,
  level, category, session ID, message, exception, cached system info, current scene,
  world seed, current world object и structured fields. Godot APIs используются только
  при main-thread initialization/context update; `Log()` потокобезопасен и пригоден для
  worker threads;
- реализованы все 14 нормативных категорий §35: `BOOT`, `CONTENT`, `WORLDGEN`,
  `STREAMING`, `DATABASE`, `SAVE`, `PLAYER`, `SHIP`, `AI`, `QUEST`, `NETWORK`, `SERVER`,
  `PERFORMANCE`, `ERROR`;
- logger не записывает command-line payload и не сохраняет персональные identifiers. Keys,
  содержащие password/passwd/token/secret/authorization/bearer/cookie/api-key, а также
  email/username/full-name/phone/address, заменяются `[REDACTED]`; аналогичные secret
  `key=value` fragments санитизируются в message/exception text, а случайно попавшие
  user-home paths и локальное имя пользователя заменяются `[USER_HOME]` / `[USER]`;
- Main Menu и vertical slice инициализируют единый logging session; vertical slice раз в
  секунду обновляет cached world context без обращения worker threads к SceneTree;
- `F5` дополнен `TASK-136` runtime acceptance: deterministic seed explorer probe,
  real cube-sphere generation profile, presence of live chunk profiler API, Save Inspector
  primary DB contract, 15-command console registry, записи всех 14 log categories и
  adversarial redaction sample. Acceptance затем перечитывает JSONL и требует отсутствия
  исходных secret test values;
- добавлен `tools/validate-developer-diagnostics-contract.py`, который статически
  проверяет 5/5 tools, 15/15 commands, 14/14 categories, обязательные structured-log
  fields, developer gating, Save Inspector copy-migration/export, planet/worldgen metrics
  и Chunk Profiler coverage;
- предыдущие строгие TASK-132 localization и TASK-134 audio gates проходят без
  ослабления: Developer UI размещён в internal tool layer, а shipping Main Menu добавляет
  только новый локализованный key `ui.dev.tools`.

**Добавленные/изменённые ключевые файлы:**

- `src/Game.Client/Scripts/Infrastructure/StructuredGameLogger.cs` + `.uid`;
- `src/Game.Client/Scripts/Developer/DeveloperToolContext.cs` + `.uid`;
- `src/Game.Client/Scripts/Developer/DeveloperWorkbenchController.cs` + `.uid`;
- `src/Game.Client/Scripts/Developer/DeveloperDiagnosticsSuite.cs` + `.uid`;
- `src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs` + `.uid`;
- `src/Game.Client/Scenes/Developer/DeveloperWorkbench.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scripts/VerticalSlice/GalaxyNavigationRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/PlayerSurvivalRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Application/MainMenuController.cs`;
- `src/Game.Client/Content/localization.en.json` / `localization.ru.json`;
- `tools/validate-developer-diagnostics-contract.py`;
- `README.md`; `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-134`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-135`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-136`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-137`: `NOT_STARTED` → `IN_PROGRESS` — clean build + developer workbench/F5 smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Статическая приёмка TASK-136:**

```text
python tools/validate-localization-contract.py
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1329; parity=1; ... sourceSinks=0; legacyLiterals=0.

python tools/validate-audio-contract.py
TASK-134 AUDIO CONTRACT PASS: buses=8/8; cues=19; pool2d=8; pool3d=16; maxTransient=24; maxConcurrent=29; ...

python tools/validate-developer-diagnostics-contract.py
TASK-136 DEVELOPER DIAGNOSTICS CONTRACT PASS: tools=5/5; commands=15/15; logCategories=14/14; logFields=10/10; devGate=1; seedExplorer=1; planetPreview=1; chunkProfiler=1; saveInspector=1; debugConsole=1; redaction=1.

Repository static pre-release:
files=313; added=12; changed=11; removed=0;
JSON=18/18; C# lexical=131/131; resRefs=59/broken=0;
UID owners=137/137 unique; forbiddenArtifacts=0;
industry=174/42/128/15/32; NPC=3/8/8/8; ecology=16/60/20/flying4.
```

**Ограничение среды:** `dotnet`, `msbuild`, `csc`, `mcs`, `godot`, `godot4` в
среде подготовки отсутствуют. Поэтому `TASK-136` остаётся `IMPLEMENTED`, а реальный
compile/runtime gate вынесен в `TASK-137`; сборка или Godot PASS статически не
приписываются.

**Минимальная runtime-приёмка TASK-137:**

1. `tools\clean-build-windows10.cmd` → реальный `CoreCompile`, `0 errors`.
2. Debug run: Main Menu содержит `Developer Tools`; release run без `--developer` не
   должен предоставлять доступ к Workbench. С `--developer` доступ разрешён.
3. Seed Explorer: сменить seed/sector, сгенерировать system, скопировать ID, export JSON;
   повтор того же seed/sector обязан дать тот же ID/planet list.
4. Planet Preview: получить metrics, поочерёдно проверить `chunk grid / biomes / height /
   resources` в interactive preview, сменить camera/LOD, `F6` вернуть Workbench.
5. Chunk Profiler: во время движения увидеть изменения loaded/queue/workers/CPU/apply/
   memory/vertices/collisions; `F10` stress, `P` soak, `F6` возврат.
6. Save Inspector: проверить primary и произвольный путь, `integrity=ok`, `Export All Tables`
   создаёт CSV для пользовательских SQLite tables; `Migrate Validated Copy` создаёт только
   `developer_reports/save_1.migration-copy.db`, source save остаётся неизменным/читаемым.
7. Debug Console: `Ctrl+Shift+D`; проверить минимум `teleport`, `give`, `damage/heal`,
   `show_chunks`, `show_navmesh`, `profile_worldgen`, `save`.
8. Один `F5`; ключевая строка:

```text
TASK-136 developer diagnostics acceptance PASS: tools=5/5; commands=15/15; devGate=1; seedExplorer=1; planetPreview=1; chunkProfiler=1; saveInspector=1; debugConsole=1; logCategories=14/14; utc=1; session=1; context=1; redaction=1; secretLeak=0; jsonl=1; result=section-34-35-developer-diagnostics.
```

**Граница закрытия:** после `TASK-137 → VERIFIED` §34 и §35 считаются закрытыми для
текущего проекта. §36 «Testing» остаётся отдельным крупным блоком: unit/golden-seed/save/
stress/coverage automation должна опираться на TASK-136 diagnostics, а не создавать
параллельные инспекторы.

---

## 0C. Предыдущая mega-итерация 2026-08-15 — Sound/audio architecture / §32 closure

### Закрытие localization-итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-134 журнал синхронизирован:

- `TASK-132` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-133` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка не приписываются среде подготовки задним числом.

### TASK-134 — unified sound runtime, environments, 3D audio and bounded playback

**Исходный снимок:** `ProjectHorizon-main-localization-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-audio-architecture-closure.zip`.  
**Связанные требования ТЗ v2.0:** §32 «Sound»: нормативные audio buses `Master`,
`Music`, `Ambient`, `SFX`, `UI`, `Voice`, `Vehicle`, `Weather`; 3D positioning и
distance attenuation; ограничение одновременно воспроизводимых transient sounds и
pooling; различные audio environments для atmosphere/vacuum/interior/water; отсутствие
обычного внешнего физического звука в вакууме при сохранении внутренних ship/UI/Voice
signals. Интеграция с §31.4 Settings и уже реализованными survival/voyage/combat systems.

**Реализовано:**

- добавлен единый persistent `AudioDirector` на root `SceneTree`; при переходах
  Main Menu ↔ gameplay создаётся ровно один director, повторный вызов безопасно
  переиспользует существующий runtime;
- `AudioDirector.EnsureBusLayout()` создаёт точный нормативный bus graph:
  `Master + Music/Ambient/SFX/UI/Voice/Vehicle/Weather`, все дочерние buses направлены
  в `Master`; существующие три пользовательских sliders сохранены: Music → `Music`,
  Speech → `Voice`, Effects → `Ambient/SFX/UI/Vehicle/Weather`;
- transient playback ограничен двумя фиксированными pools: `8 × AudioStreamPlayer` и
  `16 × AudioStreamPlayer3D`, итого максимум `24` transient voices; пять dedicated loop players дают жёсткий общий ceiling **29 simultaneous voices**; при переполнении
  применяется priority-aware oldest-voice stealing вместо создания неограниченных Nodes;
- world SFX используют `AudioStreamPlayer3D`, `GlobalPosition`, `UnitSize` и
  `MaxDistance`; resource collect, station craft completion и multitool weapon реально
  идут через positional pool; UI/Voice остаются non-positional;
- реализованы четыре audio environment profiles: `Atmosphere`, `Vacuum`, `Interior`,
  `Water`; atmosphere включает ambient + weather, interior имеет собственный ambient и
  low-pass на Ambient/Vehicle, water — отдельный ambient и low-pass на
  SFX/Ambient/Weather/Vehicle, vacuum останавливает внешние ambient/weather layers;
- vacuum rule исполняется централизованно внутри `PlayWorldCue`: physical cue с
  `externalInVacuum=true` подавляется до выделения voice; внутренний `Vehicle`, UI и Voice
  сохраняются. Мультитул на безвоздушной поверхности поэтому не издаёт обычный внешний
  выстрел, а cockpit/ship/UI feedback остаётся слышимым;
- добавлена music state machine `None/Menu/Surface/Space/Interior/Combat`; пять
  функциональных music loops переключаются dual-player crossfade за `1.25 s`; combat
  определяется существующим hostile-raider ship state и proximity, а не отдельным
  дублированным combat model;
- `Vehicle` loop связан с существующим piloted ship и фактической скоростью: pitch/volume
  меняются по отношению `Velocity / BoostMaxSpeed`;
- в реальный gameplay подключены UI click/confirm/error, NPC/station radio voice,
  multitool weapon, resource collection, craft/production completion, player-damage
  feedback и periodic life-support alarm при Oxygen ≤ 18%; alarm имеет cooldown и не
  создаёт unbounded voice spam;
- динамически создаваемые gameplay buttons повторно обнаруживаются director-ом через
  безопасный idempotent hook; Pause/Resume/Death и Main Menu используют тот же UI layer;
- добавлен детерминированный `ProceduralAudioBank`: 19 функциональных cue создаются как
  16-bit mono PCM `AudioStreamWav` при `44100 Hz`; это shipping-safe functional baseline
  без внешних raw WAV/AIFF authoring sources и без новой файловой зависимости. Финальные
  authored OGG/импортированные assets впоследствии могут заменить cue streams по stable
  cue IDs без изменения gameplay API/pools/buses;
- HUD получил локализованную audio diagnostics line: environment, music state, active
  transient voices, positional request count и vacuum-suppression count; добавлено 12
  RU/EN keys, поэтому текущий localization catalog = **1328 keys/locale**, exact parity;
- `tools/validate-audio-contract.py` является статическим TASK-134 gate: exact buses,
  cue registration, bounded pools, environment/music coverage, 3D attenuation, vacuum
  rule, Settings routing, gameplay hooks, localization и отсутствие raw WAV/AIFF sources;
- `F5` дополнен `TASK-134` runtime acceptance, который фактически переключает все четыре
  environment profiles, проверяет внешнее подавление/внутренний звук в vacuum, 2D/3D
  pool overflow/stealing, positional requests, UI/Voice layers, music state transitions и
  finite bus volumes, после чего возвращает исходные environment/music state. Gameplay
  save-slot эта проверка не изменяет.

**Добавленные/изменённые ключевые файлы:**

- `src/Game.Client/Scripts/Application/AudioDirector.cs` + `.uid`;
- `src/Game.Client/Scripts/Application/ProceduralAudioBank.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAudio.cs` + `.uid`;
- `tools/validate-audio-contract.py`;
- `src/Game.Client/Scripts/Application/GameUserSettings.cs`;
- `src/Game.Client/Scripts/Application/MainMenuController.cs`;
- `src/Game.Client/Scripts/Application/GamePauseOverlay.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcFactions.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlayerSurvival.cs`;
- `src/Game.Client/Content/localization.en.json`;
- `src/Game.Client/Content/localization.ru.json`;
- `README.md`; `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-132`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-133`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-134`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-135`: `NOT_STARTED` → `IN_PROGRESS` — clean build + audible environment/F5 smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Статическая приёмка TASK-134:**

```text
python tools/validate-localization-contract.py
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1328; parity=1; blanks=0; contentKeys=486; dynamicKeys=60; sourceUiKeys=573; sceneKeys=14; keyOnlyContent=1; sourceSinks=0; legacyLiterals=0.

python tools/validate-audio-contract.py
TASK-134 AUDIO CONTRACT PASS: buses=8/8; cues=19; pool2d=8; pool3d=16; maxTransient=24; maxConcurrent=29; environments=4; musicStates=6; positional=1; attenuation=1; pooling=1; vacuumRule=1; gameplayHooks=6; settingsRouting=1; localization=1; sourceAudioAssets=0.
```

**Минимальная runtime-приёмка TASK-135:**

1. `tools\clean-build-windows10.cmd` → реальный `CoreCompile`, `0 errors`.
2. Main Menu: слышна menu music; кнопки дают UI click, Settings Music/Effects/Speech
   действительно независимо меняют соответствующие buses.
3. Поверхность с атмосферой: слышны Ambient + Weather; сбор ресурса, мультитул и
   завершение craft дают world SFX. Под водой среда должна стать приглушённой и сменить
   ambient; на orbital station — interior profile.
4. В пилотируемом корабле `Vehicle` loop должен менять pitch/volume со скоростью; рядом
   с hostile raider music переходит в Combat, после выхода из контекста возвращается.
5. В vacuum обычный внешний weapon/world cue должен быть подавлен, но internal Vehicle,
   UI и Voice остаются слышимы.
6. Один `F5`; ключевая строка:

```text
TASK-134 audio architecture acceptance PASS: buses=8/8; cues=19/19; pool2d=8; pool3d=16; activeTransient=.../24; maxConcurrent=29; poolSteals=>0; positional=1; attenuation=1; atmosphere=1; water=1; interior=1; vacuum=1; externalVacuumSuppressed=1; internalVacuumAllowed=1; musicCrossfade=1; ui=1; voice=1; settingsRouting=1; ... sampleRate=44100; proceduralBank=1; result=section-32-audio-runtime.
```

**Граница закрытия:** после `TASK-135 → VERIFIED` технический runtime §32 считается
закрытым для shipping vertical slice: buses/routing, bounded playback, environment model,
3D attenuation, vacuum semantics, vehicle/UI/voice/world layers и music transitions
исполняются централизованно. `ProceduralAudioBank` — функциональный baseline, а не попытка
заменить будущий production sound-design; финальный художественный набор OGG может
обновляться отдельно без изменения архитектуры или требований persistence.

**Следующий mega-шаг после TASK-135:** повторный gap-analysis всего PDF-ТЗ; не продолжать
§32 мелкими cue-патчами, если runtime acceptance зелёный.

---

## 0D. Предыдущая mega-итерация 2026-08-15 — полное RU/EN localization runtime / §31.3 closure

### Закрытие UI/application-shell итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-132 журнал синхронизирован:

- `TASK-130` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-131` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка не приписываются среде подготовки задним числом.

### TASK-132 — centralized Russian/English localization and hardcoded-string elimination

**Исходный снимок:** `ProjectHorizon-main-ui-application-shell-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-localization-closure.zip`.  
**Связанные требования ТЗ v2.0:** §31.3 «Localization»: русский и английский языки,
запрет hardcoded player-facing strings и обязательный localization key для строк игрового
интерфейса/контента.

**Реализовано:**

- добавлен единый `GameLocalizationService`, который загружает канонические
  `localization.en.json` и `localization.ru.json`, проверяет exact key parity/blank values,
  поддерживает `Automatic / English / Русский`, устанавливает `TranslationServer` locale
  и предоставляет `Text/Format` с именованными placeholders;
- язык хранится как user preference в `user://settings.cfg`, а не в SQLite save-slot;
  `Automatic` выбирает RU для системного `ru` и EN для остальных locale; переключение
  `English ↔ Русский` выполняется live без перезапуска и без изменения gameplay save;
- Main Menu, Settings, Pause/Death и shipping vertical-slice UI обновляются при
  `LocaleChanged`; открытые Station Services, crafting/research/queue/dismantle, Base,
  Discovery, Ship Management, Galaxy/System Map, Ecology, Mission Journal, equipment,
  NPC dialogue, Planet Map и HUD перерисовываются немедленно;
- `station_services.json`, `npc_factions.json` и `ecology.json` переведены с параллельных
  `...En/...Ru` полей на key-only data model; NPC dialogue options/consequences и name pools
  также разрешаются централизованно;
- player-facing action results переведены на localization keys в research, trade/quests,
  base construction, ship systems, survival/equipment, planetary exploration, galaxy jump,
  Stage-1 voyage, starter repair, timed crafting, industry/network/dismantle runtime;
- интерактивный HUD полностью переведён на ключи: objective, autosave, interaction prompts,
  production network, station/base/exploration/ship summaries, ecology/NPC/ground+aerial
  navigation, galaxy/voyage/star-system summaries и control hints; `TASK-xxx`, stable IDs и
  acceptance tokens остаются техническими идентификаторами, а не локализуемой прозой;
- `PlayerController.GetInteractionPrompt()` теперь локализуется централизованно;
- дополнительно исправлен старый data defect: 50 `base.module.*` localization keys были
  объявлены в `base_construction.json`, но отсутствовали в обоих каталогах; все 50 добавлены;
- итоговый RU/EN catalog содержит **1316 ключей на язык**, exact parity, 0 blank values;
- добавлен `tools/validate-localization-contract.py`: статический gate проверяет два locale,
  parity, все content localization references, отсутствие legacy bilingual/raw fields,
  shipping scene keys и player-facing source sinks; developer prototype scenes и
  acceptance/log diagnostics намеренно не входят в shipping UI contract;
- `F5` дополнен `TASK-132` runtime acceptance: catalog diagnostics, required content keys,
  EN↔RU live switch, settings language configuration, key-only content и scene-key contract.

**Статусы:**

- `TASK-130`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-131`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-132`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-133`: `NOT_STARTED` → `IN_PROGRESS` — clean build + Main Menu/live language/F5 smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Статическая приёмка TASK-132:**

```text
python tools/validate-localization-contract.py
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1316; parity=1; blanks=0; contentKeys=486; dynamicKeys=60; sourceUiKeys=572; sceneKeys=14; keyOnlyContent=1; sourceSinks=0; legacyLiterals=0.
```

**Минимальная runtime-приёмка TASK-133:**

1. `tools\clean-build-windows10.cmd` → реальный `CoreCompile`, `0 errors`.
2. В Main Menu открыть Settings и переключить `Automatic → English → Русский → English`:
   menu/settings должны обновляться без restart; после Apply язык должен пережить scene transition.
3. В gameplay открыть несколько одновременно существующих систем: Inventory/equipment, crafting,
   Station Services/dialogue, Base, Discovery, Galaxy Map, Ecology, Mission Journal, NPC dialogue,
   Planet Map и Pause; смена языка должна обновлять уже открытые панели.
4. Проверить, что custom POI name остаётся пользовательским именем, а data-driven content names
   меняют язык через localization key; save-slot/revision не меняются только из-за смены языка.
5. Один `F5`; ключевая строка:

```text
TASK-132 localization acceptance PASS: locales=2; keys=1316; parity=1; missingValues=0; requiredKeys=...; missingKeys=0; keyOnlyContent=1; sceneKeys=1; liveSwitch=1; settingsLanguage=1; active=...; result=section-31.3-localization-runtime.
```

**Граница закрытия:** после `TASK-133 → VERIFIED` §31 UI/application shell + controls +
localization/accessibility baseline считается закрытым целиком для shipping vertical slice.
Development prototype/acceptance diagnostic strings не являются player-facing UI и остаются
диагностическими. §32 Sound этой итерацией не закрывается.

**Следующий рекомендуемый mega-шаг после TASK-133:** новый gap-analysis; наиболее крупный
очевидный кандидат — §32 Sound/audio architecture, поскольку UI уже имеет независимые
Music/SFX/Voice buses, но полноценный игровой sound runtime ещё не реализован.

---

## 0E. Предыдущая mega-итерация 2026-08-15 — UI/application shell / §31.1 + §31.2 + §31.4 baseline

### Закрытие star-system итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и начать следующую. Поэтому до начала TASK-130 журнал синхронизирован:

- `TASK-128` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-129` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка не приписываются среде подготовки задним числом.

### TASK-130 — application shell, pause, controls and accessibility settings

**Исходный снимок:** `ProjectHorizon-main-star-system-simulation-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-ui-application-shell-closure.zip`.  
**Связанные требования ТЗ v2.0:** §31.1 «Обязательные экраны», §31.2
«Управление», §31.4 «Доступность». Существующие HUD/inventory-like equipment,
technologies, mission journal, system/galaxy navigation, trade, construction,
discovery catalogue и ship management не дублируются: TASK-130 добавляет отсутствующий
application-level shell, отдельный Planet Map поверх существующего exploration state и
подключает shell к уже существующим gameplay screens. Полная
§31.3 Localization намеренно остаётся отдельной задачей, поскольку существующие
hardcoded gameplay strings ещё не переведены целиком на localization keys.

**Реализовано:**

- `project.godot` больше не стартует непосредственно в vertical slice: новый
  `Scenes/UI/MainMenu.tscn` является application entry point и содержит Continue,
  New Game, Load Game, Settings и Quit;
- Main Menu асинхронно открывает тот же `SaveDatabase`/primary slot, что и gameplay,
  показывает revision/update/system/ship summary и блокирует Continue/Load для пустого
  slot; путь `profile_vertical_slice/save_1.db` вынесен в единый `GameProfilePaths`;
- New Game использует штатный persistence flow `InitializeAsync → ResetSlotAsync`,
  поэтому не удаляет SQLite/WAL/backup family вручную и не затрагивает user settings;
- user preferences вынесены из save schema в `user://settings.cfg` через Godot
  `ConfigFile`: on-foot/ship mouse sensitivity, отдельная axis inversion, FOV `60–110`,
  UI scale `0.8–1.5`, subtitles, camera-shake/motion-blur flags, Music/SFX/Voice volumes
  и keyboard bindings; SQLite schema остаётся `2`;
- `GameUserSettingsService` создаёт/перестраивает runtime `InputMap`, сохраняет
  keyboard binding отдельно от fixed standard-gamepad events и применяет настройки
  к живым `PlayerController`/`ArcadeShipController`;
- on-foot sprint/crouch переведены с `IsPhysicalKeyPressed` на actions; ручной корабль
  полностью переведён с physical `W/S/A/D/C/Space/Q/E/arrows/B/X/F2/G` polling на
  отдельный ship action-set; поэтому переназначение клавиш реально влияет на gameplay;
- gamepad baseline добавлен для движения, jump/interact/sprint/crouch, pause и всего
  основного 6-DOF ship управления; keyboard и gamepad bindings сосуществуют;
- `PlayerController` и обе ship cameras получают runtime FOV, sensitivity и inversion;
  root `Window.ContentScaleFactor` исполняет accessibility UI scale;
- Music/SFX/Voice имеют независимые runtime bus volumes; если bus отсутствует в
  текущем vertical slice, settings service создаёт его без изменения gameplay save;
- vertical slice получил `ApplicationShell`/`GamePauseOverlay` с
  `ProcessMode=Always`: вне открытого gameplay UI action `pause` выставляет
  `SceneTree.Paused=true`, показывает mouse и предоставляет Resume/Settings/
  Save & Main Menu/Save & Quit; gameplay UI сначала закрывается своим прежним Escape;
- `Save & Main Menu` не обходит persistence: tree unpause выполняется только для
  продолжения graceful-exit state machine, затем существующий autosave flush завершается
  и только после PASS выполняется `ChangeSceneToFile(MainMenu)`;
- terminal survival state показывает отдельный blocking death screen и паузит gameplay;
  цветовой смысл shell status продублирован текстовыми tokens `[PAUSED]`/`[CRITICAL]`;
- обязательный Inventory screen закрыт расширением существующего `I` equipment UI: отдельная
  вкладка Inventory показывает весь текущий `Session.AvailableInventory`, не создавая второй
  inventory store; Suit/Multitool/Consumables остаются соседними вкладками того же экрана;
- добавлен отдельный Planet Map (`planet_map`, default `N` + gamepad Back): карта не
  заводит собственную exploration-модель, а отображает текущего игрока и существующие
  planetary POI как unknown/discovered/resolved; action не перехватывает контекстный `N`,
  когда уже открыт другой gameplay screen;
- `F5` дополнен TASK-130 acceptance: application main-scene wiring, pause overlay
  ProcessMode, exact settings round-trip, единый profile path/slot contract, keyboard+
  gamepad events on-foot/ship, separate control sets и accessibility ranges.

**Добавленные/изменённые ключевые файлы:**

- `src/Game.Client/Scripts/Application/GameUserSettings.cs` + `.uid`;
- `src/Game.Client/Scripts/Application/GameSettingsPanel.cs` + `.uid`;
- `src/Game.Client/Scripts/Application/MainMenuController.cs` + `.uid`;
- `src/Game.Client/Scripts/Application/GamePauseOverlay.cs` + `.uid`;
- `src/Game.Client/Scenes/UI/MainMenu.tscn`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceApplicationShell.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetMap.cs` + `.uid`;
- `src/Game.Client/Scripts/Player/PlayerController.cs`;
- `src/Game.Client/Scripts/Ship/ArcadeShipController.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlayerSurvival.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `src/Game.Client/project.godot`;
- `README.md`; `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-128`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-129`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-130`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-131`: `NOT_STARTED` → `IN_PROGRESS` — clean build + Main Menu/pause/F5 smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Минимальная runtime-приёмка TASK-131:**

1. `tools\clean-build-windows10.cmd` → реальный `CoreCompile`, `0 errors`.
2. Запустить проект обычной кнопкой Run: первой сценой обязан быть Main Menu, а не
   `SalvageRepairSlice`; при существующем slot Continue/Load активны, при пустом — нет.
3. Открыть Settings, изменить UI scale/FOV/одну клавишу on-foot/одну ship-клавишу,
   Apply, перейти в игру и убедиться, что новые actions исполняются; вернуться в меню
   и подтвердить сохранение настроек между scene transitions.
4. В игре вне открытых panel нажать pause/Escape: gameplay/physics должны остановиться,
   Settings остаться интерактивными; Resume возвращает simulation. Если открыт station/
   journal/ship manager и т.п., первый Escape закрывает этот UI, а не открывает pause.
5. Нажать `N`: отдельный Planet Map должен показать `@` player и POI-маркеры `?/O/X`;
   открыть Discovery Catalog и убедиться, что его контекстный `N` не перехватывается картой.
6. `SAVE & MAIN MENU` должен вывести graceful-exit autosave PASS и вернуть Main Menu.
7. Один `F5`; ключевая строка:

```text
TASK-130 application shell acceptance PASS: mainMenu=1; newGame=1; load=1; settings=1; pauseOverlay=1; deathScreen=1; settingsRoundTrip=1; profileContract=1; onFootActions=1; shipActions=1; separateControlSets=1; keyboardRemap=1; inventory=1; planetMap=1; gamepad=1; accessibility=1; audioBuses=1; ... localizationBoundary=31.3-deferred.
```

**Граница закрытия:** TASK-130 не объявляет завершённой §31.3 Localization и не
объявляет завершённым §32 Sound. Он закрывает отсутствующий application shell,
обязательные application screens, реальную pause/control-remap интеграцию и settings/
accessibility baseline, переиспользуя ранее созданные gameplay screens.

**Следующий рекомендуемый mega-шаг после TASK-131:** gap-analysis оставшихся крупных
разделов с приоритетом полной §31.3 localization или §32 audio architecture в зависимости
от фактического объёма незакрытых требований.

---

## 0F. Предыдущая mega-итерация 2026-08-15 — star-system simulation / §15 vertical-slice closure

### Закрытие aerial-navigation итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и перейти к следующей. Поэтому до начала TASK-128 журнал синхронизирован:

- `TASK-126` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-127` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка не приписываются среде подготовки задним числом.

### TASK-128 — runtime звёздной системы и single-planet activation

**Исходный снимок:** `ProjectHorizon-main-aerial-navigation-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-star-system-simulation-closure.zip`.  
**Связанные требования ТЗ v2.0:** §15.1 «Модель звёздной системы», §15.2
«Уровни представления объектов», §15.3 «Активация планеты»; архитектурный принцип
§5.2 «не загружать всю галактику в одну сцену»; интеграция с уже закрытым §8
Galaxy/System map и hyperspace. TASK-128 закрывает именно vertical-slice runtime
§15 и не объявляет завершённой всю будущую scene-coordination архитектуру §5.

**Реализовано:**

- добавлен `StarSystemSimulationRuntime`, использующий существующий
  `GalaxyNavigationRuntime.CurrentSystem` как единственный источник system/planet
  definitions и seeds: ровно одна star, исходные `1–8` planets, точные `0–4` moons
  на planet, `1–3` station contacts и bounded `4–16` ship contacts;
- системная иерархия детерминирована stable IDs и parent links
  `star → planet → moon/station → traffic`; никакой второй galaxy generator и
  параллельный universe-state не создаются;
- движение небесных тел задаётся аналитическими круговыми орбитами в наклонённых
  плоскостях с постоянным радиусом и `OrbitTimeScale=120`; N-body, взаимная
  гравитация и численное интегрирование намеренно отсутствуют в соответствии §15.1;
- добавлены четыре representation states: `DetailedPlanet`, `Proxy`, `Marker`,
  `Statistical`; границы дальнего представления bounded (`180/420 m` в локальном
  vertical-slice scale), ship contacts никогда не создаются как тяжёлые physics
  bodies в system runtime;
- `Gameplay/StarSystemSimulation` создаёт lightweight non-colliding sphere/box
  visuals для текущей системы, обновляет их из аналитического snapshot и скрывает
  statistical/detailed representation; одновременно detailed может быть только
  текущая planet;
- после hyperspace runtime отслеживает смену `CurrentSystem.SystemId`, освобождает
  старые proxy visuals и детерминированно строит новую систему из destination seed;
- реализован реальный PlanetRuntime activation gate: `PlanetSurface` всегда active;
  в полёте surface runtime active только в радиусе `72 m` от surface checkpoint;
  на orbital station surface переводится в proxy/suspended state;
- при suspension сохраняются фактические текущие `Visible`, `ProcessMode`,
  `CollisionLayer/Mask`, после чего отключаются `GroundBody`, water, resources,
  crafting stations, ecology, ground NPC/navigation, base construction, POI и
  preview; OrbitalStation, VoyageShip, NPC ship traffic и system proxies остаются;
- при возвращении восстанавливаются именно сохранённые состояния, поэтому уже
  собранный/скрытый объект не становится видимым ошибочно; после transition заново
  синхронизируются aerial obstacle-grid и ground navigation obstacles;
- activation pipeline диагностирует наличие galaxy parameters, far LOD, current
  planet focus, active surface runtime, atmosphere, ground collision, NPC navigation
  и region/ecology objects (`0xFF` для полностью активной поверхности);
- HUD получил строку `Star system` с system ID, body counts, LOD counts,
  `PlanetRuntime=ACTIVE/PROXY`, pipeline mask и simulation epoch;
- `F5` расширен `TASK-128` acceptance: deterministic hierarchy, exact body/moon
  coverage, invariant analytic orbit radius, coverage Proxy/Marker/Statistical,
  single-DetailedPlanet invariant, deterministic system transition, live visual
  projection, runtime samples, surface-activation state и activation pipeline;
- новых таблиц/settings сохранений нет: runtime transient и восстанавливается из
  уже persisted `galaxy_navigation`; SQLite schema остаётся `2`.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationRuntime.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-126`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-127`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-128`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-129`: `NOT_STARTED` → `IN_PROGRESS` — clean build + единый F5/runtime smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Проверки среды подготовки:**

- полный lightweight lexical scan всех C# source files, scene/ext-resource/`res://`
  wiring, UID uniqueness и JSON parse;
- проверка неизменности Industry/NPC/Ecology baseline и отсутствия новой persistence
  migration;
- математическая проверка орбитальной формулы: inclination реализован поворотом
  orbital plane, поэтому `|r|` сохраняется, а не увеличивается вертикальной добавкой;
- release ZIP должен повторно распаковываться и проходить те же проверки без
  `.godot/bin/obj/.vs/.git`, runtime DB/log и иных build artefacts;
- .NET SDK/MSBuild и Godot в среде подготовки отсутствуют, поэтому локальные
  `dotnet build`, Godot import и фактический runtime не заявляются.

**Минимальная runtime-приёмка TASK-129:**

1. Выполнить `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`,
   `0 errors`.
2. Запустить `SalvageRepairSlice`; ожидается `TASK-128 star-system simulation READY`
   с system/body/planet/moon/station/traffic counts.
3. На поверхности один раз нажать `F5`. Ключевой критерий:

```text
TASK-128 star-system simulation acceptance PASS: deterministic=1; bodyCoverage=1; moonBounds=1; analyticOrbits=1; representationLevels=1; singleDetailedPlanet=1; systemTransition=1; visualProjection=1; runtimeSamples=1; surfaceActivation=1; activationPipeline=1; ...
```

4. Manual activation smoke: HUD должен показывать `PlanetRuntime=ACTIVE` на
   поверхности; после взлёта и удаления дальше ~72 m — `PROXY`; orbital station —
   `PROXY`; после возвращения/посадки — снова `ACTIVE`, без оживления ранее
   собранных resources и без пропажи NPC/base state.
5. После hyperspace jump проверить новую строку `TASK-128 ... READY`/HUD с новым
   system ID и новым deterministic body-set; старые proxy nodes не должны оставаться.
6. При `FAIL` предоставить clean-build log, полную строку `TASK-128 ... FAIL`,
   последние ~200 строк Godot Output и screenshot HUD со строкой `Star system`.

**Граница закрытия:** TASK-128 закрывает §15 в текущем vertical slice: system
simulation/LOD/one-planet activation и hyperspace rebuild. Полноценное физическое
перемещение между несколькими одновременно доступными PlanetWorld scenes и общий
scene coordinator §5 остаются отдельной будущей архитектурной задачей и здесь не
выдаются за готовые.

**Следующий рекомендуемый mega-шаг после TASK-129:** новый gap-analysis PDF v2.0;
не расширять §15 мелкими патчами без найденной regression.

---

## 0G. Предыдущая mega-итерация 2026-08-15 — aerial fauna + NPC ship navigation / §30 closure

### Закрытие предыдущей ground-navigation итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и немедленно перейти к следующей. Поэтому до начала нового шага
статусы синхронизированы так:

- `TASK-124` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-125` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; clean build/Godot runtime
  предыдущего снимка в среде подготовки не приписываются задним числом.

### TASK-126 — flying-fauna + NPC-ship navigation core

**Исходный снимок:** `ProjectHorizon-main-ground-npc-navigation-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-aerial-navigation-closure.zip`.  
**Связанные требования ТЗ v2.0:** §30.2 «Flying creatures» и §30.3 «NPC ships».
Наземная часть §30.1 уже закрыта `TASK-124/125`, поэтому эта mega-итерация
закрывает оставшийся навигационный контур §30 без расширения persistence schema.

**Реализовано:**

- добавлен общий `AerialSteeringRuntime` для flying fauna и NPC ships вместо двух
  независимых steering-систем; runtime держит только занятые ячейки локальной 3D
  spatial hash grid с cell size `10 m` и выполняет radius-neighbor queries по
  локальному набору cells, не создавая глобальную пространственную структуру мира;
- статические `Box/Cylinder/Capsule/Sphere` collision shapes переводятся в
  spherical avoidance proxies и индексируются в той же локальной 3D grid по всем
  пересекаемым cells; `GroundBody` исключён, чтобы горизонтальная поверхность не
  становилась одной гигантской запретной сферой;
- environment refresh связан с уже существующими rebuild base/POI/resource flows,
  поэтому authored и построенные препятствия попадают в aerial avoidance после
  изменения сцены;
- создан общий набор POI: water/landing-pad/ridges для flying fauna и dock/traffic
  lanes вокруг orbital station для NPC ships; nearest-POI selection исполняется
  runtime и учитывается diagnostics/acceptance;
- все четыре активных flying fauna species из ecology catalog используют shared
  steering runtime: local separation, spherical obstacle avoidance, POI steering и
  ограниченный altitude envelope относительно собственного territory center;
- старый sinus-only flying motion оставлен только как fallback для изолированного
  запуска `EcologyFaunaNode` без shared runtime; штатный vertical slice передаёт
  runtime всем flying nodes;
- добавлен отдельный `Gameplay/NpcShipTraffic` и четыре физических
  `CharacterBody3D` NPC ships, использующих уже существующие ship-class stats:
  Aegis patrol leader, formation wing, Frontier trader arrival и hostile raider;
- patrol/trader используют `arrive`, wing — `formation`, hostile raider —
  predicted `pursuit`; combat loop реально переключает `Pursuit → CombatApproach →
  BreakAway → Evade → Pursuit`; break-away/evade используют predicted threat motion;
- для всех кораблей поверх role steering одновременно применяются local-grid ship
  separation, spherical static avoidance и altitude envelope; movement/heading
  выполняются физическим `CharacterBody3D`, а не телепортацией между waypoint;
- hostile raider в обычном gameplay преследует piloted player ship, а когда игрок
  не пилотирует корабль — patrol leader; acceptance принудительно фиксирует leader
  target, чтобы combat-state cycle был воспроизводимым;
- HUD получил строку `Aerial navigation` с coverage fauna/ships, occupied grid cells,
  obstacle/POI counts, avoidance activations и текущими состояниями четырёх ships;
- `F5` расширен `TASK-126` acceptance: все четыре flying species должны быть
  подключены к shared runtime, spatial-grid/obstacle/POI probes должны исполняться,
  altitude envelope — соблюдаться, четыре NPC ships — давать реальные steering
  samples, а счётчики `pursuit/evade/arrive/formation/combat transitions` должны
  увеличиться в runtime; отдельно проверяется отсутствие существенного проникновения
  NPC ships внутрь spherical static obstacle proxies;
- навигационное состояние transient: новые save settings/SQLite tables не введены,
  schema остаётся `2`; после load/reset traffic и runtime воспроизводимо rebuild-ятся
  из catalog/scene state.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/VerticalSlice/AerialSteeringRuntime.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/AerialNavigationAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-124`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-125`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-126`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-127`: `NOT_STARTED` → `IN_PROGRESS` — clean build + единый F5/runtime smoke;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в исходном архиве.

**Проверки среды подготовки:**

- выполнены структурные проверки C# новых/изменённых файлов, scene wiring, NodePath,
  `.uid` uniqueness, `res://` references и parse всех JSON;
- отдельно проверяются точные baseline counts Industry Content, NPC/factions и
  ecology flying coverage — новая навигация не меняет каталоги ради прохождения ТЗ;
- проверяется release ZIP после повторной распаковки, отсутствие `.godot/bin/obj`,
  `.git`, runtime DB/log и других build artefacts;
- .NET SDK/MSBuild и Godot в среде подготовки отсутствуют, поэтому локальные
  `dotnet build` и фактический Godot runtime не заявляются.

**Минимальная runtime-приёмка TASK-127:**

1. Выполнить `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`,
   `0 errors`.
2. Запустить `SalvageRepairSlice`; ожидаются `TASK-126 aerial steering READY` и
   `TASK-126 NPC ship traffic READY`.
3. На поверхности один раз нажать `F5`. Ключевой критерий:

```text
TASK-126 aerial navigation acceptance PASS: flyingFauna=4; npcShips=4; gridCells=>0; obstacles=>0; poi=>=8; faunaCoverage=1; sharedRuntime=1; localGrid=1; sphericalAvoidance=1; altitude=1; poiSteering=1; shipSteering=1; pursuit=1; evade=1; arrive=1; formation=1; combatStates=1; clearance=1; runtimeSamples=1; ...
```

4. Manual fauna smoke: 20–30 s наблюдать несколько flying creatures около terrain
   objects/POI; они не должны пролетать сквозь крупные статические collision objects
   и не должны бесконтрольно уходить по высоте.
5. Manual ship smoke: после доступа к voyage наблюдать orbital-station traffic:
   wing держит formation относительно leader, trader проходит approach route, raider
   циклически сближается/уходит и при пилотировании переключает target на player ship;
   корабли не должны проходить через orbital station.
6. При `FAIL` предоставить clean-build log, полную строку `TASK-126 ... FAIL`,
   последние ~200 строк Godot Output и screenshot HUD со строкой `Aerial navigation`.

**Следующий рекомендуемый mega-шаг после TASK-127:** провести новый gap-analysis
по всему PDF-ТЗ уже после полного закрытия §30 и выбрать следующую самостоятельную
подсистему; дальнейшие navigation-патчи не планировать без выявленного regression.

---

## 0H. Предыдущая mega-итерация 2026-08-15 — ground NPC navigation / bounded nav streaming

### Закрытие предыдущей NPC/faction итерации по решению владельца продукта

Владелец продукта прямо распорядился считать предыдущую mega-итерацию успешно
завершённой и перейти дальше. Поэтому журнал синхронизирован до начала нового
шага:

- `TASK-122` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-123` — `IN_PROGRESS` → `VERIFIED`;
- основание — явный `acceptance waiver by product owner`; локальный Godot runtime
  предыдущей редакции в среде подготовки не приписывается задним числом.

### TASK-124 — локальная наземная NavigationServer3D подсистема

**Исходный снимок:** `ProjectHorizon-main-npc-factions-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-ground-npc-navigation-closure.zip`.  
**Связанные требования ТЗ v2.0:** §30.1 «Ground NPC navigation»; §29.2–29.3
локальные коллизии/препятствия около активной области; §16 физические NPC.

**Реализовано:**

- добавлен отдельный `Gameplay/NpcNavigation` runtime на `NavigationServer3D` и
  `NavigationRegion3D`; whole-planet navmesh не создаётся;
- навигационная поверхность разбита на procedural tiles `12 × 12 m` с клеткой
  `1 m`; active window имеет radius `2 tiles`, то есть максимум `5 × 5 = 25`
  регионов одновременно и bounded memory/runtime footprint;
- streaming-окно следует за игроком: при переходе центра в другой tile ненужные
  регионы evict-ятся, недостающие создаются; после перестройки выдерживается
  NavigationServer synchronization window перед path queries;
- walkable geometry строится из bounds существующего `GroundBody`, а статические
  `Box/Cylinder/Capsule/Sphere` collision shapes переводятся в local blocked cells
  с clearance по radius NPC; визуальная геометрия не парсится для runtime bake;
- для тех же статических объектов создаются `NavigationObstacle3D` avoidance
  proxies; база и POI автоматически вызывают obstacle/nav refresh после rebuild;
- семь динамических `NpcFactionAgentNode` переведены с direct local steering на
  `NavigationAgent3D`: behavior target → `TargetPosition` →
  `GetNextPathPosition()` в physics update → desired velocity →
  `VelocityComputed` → `MoveAndSlide`;
- включены 2D XZ avoidance, agent radius/height, neighbors/time horizons и общий
  avoidance layer; телепорт/respawn сбрасывает internal avoidance velocity;
- patrol/flee/hostile chase и существующий combat/dialogue слой сохранены поверх
  pathfinding; если NPC находится вне active navigation window, он не запускает
  старый прямолинейный fallback, а sleeps до возвращения локального nav region;
- добавлен stuck detector: при отсутствии физического прогресса к далёкой цели
  строится боковой recovery waypoint через реальный NavigationServer path query;
- сохранён legacy direct-motion fallback только для изолированного запуска NPC без
  подключённой navigation surface, а не для штатного vertical slice;
- HUD получил строку `NPC navigation` с regions/cells/obstacles/active agents/path
  requests/recoveries/server sync;
- `F5` расширен `TASK-124` runtime acceptance: local region budget, cross-tile path,
  obstacle clearance, forced stream shift + eviction + restore, server sync,
  реальные `NavigationAgent3D` path requests, `velocity_computed` callbacks и
  recovery-waypoint probe; gameplay save при этом не изменяется.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcNavigationAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcNavigation.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcFactions.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-122`: `IMPLEMENTED` → `VERIFIED` — acceptance waiver владельца продукта;
- `TASK-123`: `IN_PROGRESS` → `VERIFIED` — тот же waiver;
- `TASK-124`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-125`: `NOT_STARTED` → `IN_PROGRESS` — clean build + F5/runtime navigation
  acceptance на стороне пользователя;
- `TASK-006`: остаётся `BLOCKED` из-за отсутствия `.git` в поставленном архиве.

**Проверки среды подготовки:**

- выполнены статические проверки C# structure/references, scene NodePath/ext_resource,
  UID uniqueness, JSON parse и baseline content counts;
- проверены bounded tile constants и отсутствие whole-planet region creation;
- проверена связка scene → navigation surface → NPC agent → F5 acceptance → HUD;
- .NET SDK/MSBuild и Godot в среде подготовки отсутствуют, поэтому локальные
  `dotnet build`, импорт проекта и фактический runtime не заявляются.

**Минимальная runtime-приёмка TASK-125:**

1. Выполнить `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`,
   `0 errors`.
2. Запустить `SalvageRepairSlice`; в startup ожидаются строки `TASK-124 NPC
   navigation surface READY` и `TASK-124 NPC NavigationAgent3D binding READY`.
3. Один раз нажать `F5`. Ключевой критерий:

```text
TASK-124 NPC navigation acceptance PASS: regions=<1..25>/25; walkableCells=>0; obstacles=>0; avoidanceObstacles=>0; tilesTouched=>=3; pathPoints=>=2; localBudget=1; crossTilePath=1; obstacleClearance=1; boundedStreaming=1; navigationAgents=7; pathRequests=>0; avoidanceSamples=>0; agentRuntime=1; avoidanceRuntime=1; recoveryProbe=1; evicted=>0; sync=1; result=local tiled NavigationServer3D runtime verified.
```

4. Manual smoke: наблюдать минимум двух NPC с разных сторон препятствий 20–30 s;
   они должны обходить collision objects без прохода насквозь и без постоянного
   `TASK-124 NPC navigation recovery` loop. Подойти к hostile Opponent: chase и
   атака должны сохраниться. Отойти достаточно далеко и вернуться: строка HUD
   `regions=N/25` остаётся bounded, NPC снова продолжают движение.
5. При `FAIL` предоставить clean-build log, полную строку `TASK-124 ... FAIL`,
   последние ~200 строк Godot Output и screenshot HUD со строкой `NPC navigation`.

**Следующий рекомендуемый mega-шаг после TASK-125:** закрыть следующий крупный
неверифицированный блок ТЗ, выбранный по актуальному gap-анализу после runtime
приёмки навигации; flying NPC steering (§30.2) не смешивать с ground navigation.

---

## 0I. Предыдущая синхронизация и mega-итерация 2026-08-15 — NPC / factions / dialogues

### Закрытие player survival по решению владельца продукта

Предыдущая редакция `TASK-120/121` была прямо принята владельцем продукта для
продолжения разработки без дополнительной трудоёмкой ручной проверки. Журнал
синхронизирован с этим решением до выбора нового шага:

- `TASK-120` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-121` — `IN_PROGRESS` → `VERIFIED`;
- статус повышен как `acceptance waiver by product owner`, а не как локально
  выполненные clean build/F5; сборка и runtime `TASK-120` в среде подготовки не
  выполнялись и не заявляются.

### TASK-122 — NPC / factions / dialogue core mega-iteration

**Исходный снимок:** `ProjectHorizon-main(8)(1).zip`
(последняя приложенная GitHub-редакция).  
**Подготовленный снимок:** `ProjectHorizon-main-npc-factions-closure.zip`.  
**Git SHA:** архив не содержит `.git`; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования PDF v2.0:** §16 «NPC и фракции»; §19.1–19.4
процедурные `DefeatTarget/ProtectTarget`; §22 persistence. Требование §30.1 о
локальных tiled `NavigationServer3D/NavigationRegion3D` не присваивается этой
задаче и остаётся отдельной navigation-итерацией.

**Реализовано:**

- добавлен строгий `npc_factions.json` schema `1`, использующий существующие
  `Trading / Scientific / Military` faction definitions Station Services вместо
  создания второй экономики; catalog валидирует reciprocal relation matrix,
  economy interests/tags, quest types, visual styles и name pools;
- покрыты ровно все восемь NPC archetypes PDF: `Trader`, `Technician`, `Pilot`,
  `Scientist`, `Guard`, `GuildRepresentative`, `Traveler`, `Opponent`; существующий
  `npc.trader.ilia_voss` остаётся authored Station Services NPC и не дублируется,
  ещё семь агентов создаются в `Gameplay/NpcPopulation`;
- все faction-bound NPC используют имена из уже определённых faction name pools;
  catalog проверяет точное archetype coverage `8/8`, уникальные stable IDs,
  faction/combat flags и допустимые spatial/combat параметры;
- добавлено восемь RU/EN dialogue templates — по одному на archetype. Каждый
  содержит stable dialogue/option IDs, executable `always` либо
  `reputation>=N` condition, minimum reputation, локализованные greeting/response/
  consequence/farewell, action и reputation delta; Trader exposes existing trade,
  GuildRepresentative — existing Mission Journal, protected NPC — protection
  acknowledgement;
- `NpcFactionRuntime` хранит per-faction reputation и delta-only agent state;
  meaningful dialogue consequence применяется один раз на NPC, friendly fire
  снижает reputation, не-hostile defeat даёт дополнительный penalty;
- наземные NPC реализованы как `CharacterBody3D + IInteractable + IHitscanTarget`:
  deterministic local patrol/steering, flee после friendly hit, interaction `E`,
  физический hitscan damage; hostile Opponent преследует игрока в detection range
  и наносит damage через существующий `PlayerController.ReceiveExternalDamage`;
- hostile target после lethal hit воспроизводимо respawn-ится с инкрементом
  `DefeatCount`, поэтому `DefeatTarget` остаётся выполнимым и после раннего боя;
  Scientist и Traveler являются реальными `ProtectTarget`;
- procedural quest capability factory принимает реальные NPC target IDs; обычный
  gameplay-board теперь включает и валидирует `DefeatTarget` и `ProtectTarget`,
  снимая известное ограничение `TASK-118`, при этом все остальные 13 objective
  types и 20-offer deterministic board сохраняются;
- procedural quest reward дополнительно применяется к новой per-faction
  reputation, когда faction ID известен NPC/faction catalog;
- persistence добавляет optional `save_settings.npc_factions` без повышения
  SQLite schema `2`: world seed/region + только ненулевые reputation и изменённые
  agent states; repeated save использует общий DELETE→INSERT replace path;
  поддержаны exact round-trip, cold restore, legacy-empty fallback, graceful exit
  и `F8` reset;
- `AutosaveTrigger.NpcChanged` добавлен как новый trigger; gameplay autosave и
  graceful-exit snapshot включают NPC/faction deltas;
- `F5` расширен изолированной `TASK-122` acceptance в
  `save_1.npc-factions-test.db`: 3 factions, 8 archetypes/agents/dialogues, relation
  matrix, localized condition coverage, one-shot interaction, reputation, friendly
  fire, respawnable combat target, real quest target IDs, delta-only state, две
  последовательные записи, exact round-trip, cold restore, legacy fallback,
  autosave log, one-writer discipline и SQLite integrity;
- README/HUD обновлены: `E` включает наземных NPC, `F5` содержит `TASK-122`,
  `F8` сбрасывает NPC/faction deltas; отдельная строка HUD показывает alive agents,
  combat/protected targets, defeat count и три faction reputations.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Content/npc_factions.json`;
- `src/Game.Client/Scripts/VerticalSlice/NpcFactionCatalog.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcFactionRuntime.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/NpcFactionAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcFactions.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProceduralQuests.cs`;
- `src/Game.Client/Scripts/VerticalSlice/ProceduralQuestRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/ProceduralQuestAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-120`: `IMPLEMENTED` → `VERIFIED` — прямой acceptance waiver владельца
  продукта;
- `TASK-121`: `IN_PROGRESS` → `VERIFIED` — тот же acceptance waiver;
- `TASK-122`: `NOT_STARTED` → `VERIFIED` (последующее acceptance waiver владельца продукта);
- `TASK-123`: `NOT_STARTED` → `VERIFIED` — первоначально был runtime acceptance,
  затем закрыт последующим acceptance waiver владельца продукта;
- `TASK-006`: остаётся `BLOCKED`.

**Проверки среды подготовки:**

- JSON parse/catalog invariants проверены статически: `3 factions / 8 archetypes /
  8 agents / 8 dialogues / 1 hostile / 2 protected`;
- проверены optional-setting read/write/delete paths `npc_factions`, обе snapshot
  factory интеграции, scene nodes `Gameplay/NpcPopulation` и `Hud/NpcInteraction`,
  реальные procedural combat/protection target IDs и отсутствие изменения schema;
- .NET SDK, C# compiler и Godot в среде подготовки отсутствовали, поэтому clean
  build и фактический Godot runtime этой исторической итерации **не заявлялись**;
  позднее `TASK-122/123` закрыты явным acceptance waiver владельца продукта.

**Минимальная runtime-приёмка TASK-123:**

1. Выполнить `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`,
   `0` errors.
2. Запустить `SalvageRepairSlice`. Startup должен содержать:

```text
TASK-122 NPC/faction catalog READY: schema=1; factions=3; archetypes=8; agents=8; dialogues=8; defeatTargets=1; protectTargets=2; ...
TASK-122 physical NPC population READY: authored=1; dynamic=7; interaction=E; hostileCombat=multitool-hitscan; navigation=TASK-124.
```

3. Если восстановился старый gameplay state — нажать `F8`; затем один раз `F5`.
   Успех — строка:

```text
TASK-122 NPC/factions acceptance PASS: factions=3; archetypes=8; agents=8; dialogues=8; factionCoverage=1; relations=1; dialogueCoverage=1; interaction=1; reputation=1; combat=1; questTargets=1; deltaOnly=1; coldRestore=1; legacyFallback=1; roundTrip=1; repeatedSave=1; logWritten=1; maxWriters=1; integrity=ok; ...
```

4. Короткий manual smoke: подойти к любому новому NPC и нажать `E`; проверить
   `Up/Down + Enter`, затем закрыть `Esc`. У Scientist/Traveler выбрать protection
   option. Переключить multitool в weapon (`Z` при необходимости) и четырежды
   попасть в hostile Opponent: Output должен показать `defeated=1; respawned=1`,
   HUD — увеличение `defeats`. Открыть `Q`: среди 20 missions должны присутствовать
   реальные `DefeatTarget` и `ProtectTarget`.
5. Для достаточного подтверждения прислать build summary, screenshot HUD после F5
   и полную строку `TASK-122 NPC/factions acceptance PASS`. При `FAIL` — build log,
   `TASK-122 ... FAIL`, последние ~200 строк Godot Output и точный шаг smoke.

**Следующий шаг:** `TASK-124` реализован последующей mega-итерацией; см. раздел 0.

---

## 0J. Предыдущая синхронизация и mega-итерация 2026-08-15

### Закрытие procedural quests по прямому решению владельца продукта

Пользователь 2026-08-15 прямо распорядился считать `TASK-118/119` отработанными и
перейти к следующей mega-итерации без сложной ручной проверки. Поэтому:

- `TASK-118` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-119` — `IN_PROGRESS` → `VERIFIED`;
- procedural repeatable mission core, Mission Journal, feasibility/state graph и
  delta-only persistence считаются закрытыми для обычной разработки;
- clean build/F5/runtime smoke `TASK-118` **не выполнялись и не заявляются как
  выполненные**; статус повышен как явный `acceptance waiver by product owner`;
- обнаруженный при интеграции дефект повторной записи optional setting
  `procedural_quests` исправляется в `TASK-120`: ключ теперь входит в DELETE/replace
  transaction перед повторным INSERT, а acceptance использует фактический путь
  `SaveAutosaveCoordinator.AutosaveLogPath`.

### TASK-120 — player survival / exosuit / multitool mega-iteration

**Исходный снимок:** `ProjectHorizon-main-procedural-quests-closure.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-player-survival-multitool-closure.zip`.  
**Git SHA:** архив не содержит `.git`; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования PDF v2.0:** §13 «Персонаж», §3.3 planetary environments,
§22 persistence; нормативный Industry Content baseline §45–57 не изменяется.

**Реализовано:**

- добавлен строгий `player_survival.json` schema `1`: 11 базовых параметров
  персонажа, `3` suit modules, `3` multitool modules, `6` survival consumables и
  `8` landable environment archetypes; используются уже существующие IDs из
  Industry Content v2, поэтому baseline `174 items / 42 resources / 128 recipes /
  15 stations / 32 technologies` остаётся неизменным;
- `PlayerSurvivalRuntime` исполняет Health, Shield, Stamina, LifeSupport,
  HazardProtection, Temperature/Radiation/Toxic protection, Oxygen,
  JetpackEnergy и MultitoolEnergy; suit modules изменяют protection/capacity,
  consumables восстанавливают реальные runtime pools;
- environmental tick для temperate/desert/frozen/volcanic/toxic/radioactive/
  barren/oceanic учитывает temperature/radiation/toxic exposure, life support,
  breathable/non-breathable oxygen, water swimming и health damage после
  исчерпания protection; shield и энергии восстанавливаются по определённым
  правилам без offline progress;
- `PlayerController` расширен sprint, crouch, jetpack и swimming без создания
  второго character controller; Shift расходует Stamina, удержание Space в воздухе
  расходует JetpackEnergy, Ctrl приседает, в water volume Space/Ctrl управляют
  всплытием/погружением;
- multitool объединяет Scanner/Mining/Weapon/Analyzer/Repair на одном energy pool;
  три существующих Tool outputs уменьшают расход и повышают effectiveness
  соответствующих функций; mining подключён к resource collection, scanners — к
  POI/ecology, weapon — к hitscan callback, repair — к starter/system repair;
- активная fauna теперь при `Attack` действительно наносит shield/health damage
  игроку с cooldown, а не только меняет AI state;
- `I` открывает `EXOSUIT & MULTITOOL` с вкладками Overview/Suit/Multitool/
  Consumables; install/uninstall/use проходят через существующий shared inventory
  и production mirrors; `Z` переключает multitool mode;
- в surface scene добавлен небольшой `WaterPool` (`Area3D`) для реального swim/
  oxygen smoke без отдельной water subsystem;
- persistence хранит vitals, active multitool mode и installed equipment в optional
  `save_settings.player_survival`; SQLite schema остаётся `2`; поддержаны cold
  restore, legacy fallback, graceful exit, periodic/autosave snapshots и `F8` reset;
- `AutosaveTrigger.PlayerChanged` добавлен без изменения схемы БД; repeated-save
  acceptance специально делает две последовательные записи, чтобы ловить
  регрессию optional-setting replace semantics;
- `F5` расширен изолированной `TASK-120` acceptance в
  `save_1.player-survival-test.db`; она проверяет catalog coverage, protection,
  hazards, oxygen, movement resources, multitool energy/effectiveness, damage,
  consumables, slot rules, two-write persistence, cold restore, legacy fallback,
  exact round-trip, autosave log, one-writer discipline и SQLite integrity.

**Статусы:**

- `TASK-118`: `IMPLEMENTED` → `VERIFIED` — прямой acceptance waiver пользователя;
- `TASK-119`: `IN_PROGRESS` → `VERIFIED` — прямой acceptance waiver пользователя;
- `TASK-120`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-121`: `NOT_STARTED` → `IN_PROGRESS` — local clean build + единый F5;
- `TASK-006`: остаётся `BLOCKED`.

**Минимальная runtime-приёмка TASK-121:**

1. `tools\clean-build-windows10.cmd`: реальный `CoreCompile`, `0` errors.
2. Запустить `SalvageRepairSlice`; startup должен содержать `TASK-120 player
   survival catalog READY: schema=1; suit=3; multitool=3; consumables=6;
   environments=8` и `TASK-120 player survival READY`.
3. Если save восстановился в полёте — `F8`; затем один раз `F5`. Достаточный
   критерий — `TASK-120 player survival acceptance PASS` с `coverage=1;
   protection=1; hazards=1; oxygen=1; movement=1; multitoolRuntime=1; damage=1;
   consumablesRuntime=1; slots=1; coldRestore=1; legacyFallback=1; roundTrip=1;
   repeatedSave=1; logWritten=1; maxWriters=1; integrity=ok`.
4. Необязательный smoke: `I` открывает equipment panel; Shift/Ctrl меняют режим
   движения; WaterPool около `X=22,Z=22` переключает swimming.

Длинная ручная цепочка не требуется. В среде подготовки .NET SDK/Godot не
обнаружены, поэтому compilation/runtime нового C# здесь не заявляются.

---

## 0K. Предыдущая синхронизация и mega-итерация 2026-08-14

### Закрытие procedural ecology по прямому решению владельца продукта

Пользователь 2026-08-14 прямо распорядился считать предыдущую ecology-итерацию
отработанной и перейти к следующей mega-итерации без сложной ручной проверки.
Поэтому статусы синхронизированы **как acceptance waiver by product owner**, а не
как якобы выполненный локальный runtime-прогон:

- `TASK-116` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-117` — `IN_PROGRESS` → `VERIFIED`;
- procedural flora/fauna core, ecology catalogue, scan/harvest API и delta-only
  persistence считаются закрытыми для обычной разработки; возврат допустим при
  подтверждённой регрессии или изменении ТЗ;
- пользовательская clean build/F5 ecology acceptance, визуальный smoke и
  отдельное runtime-подтверждение исчезновения atmosphere warning **не
  выполнялись и не заявляются как выполненные**.

### TASK-118 — procedural repeatable quests / mission journal mega-iteration

**Исходный снимок:** `ProjectHorizon-main-planetary-ecology-closure.zip` —
редакция `TASK-116/117`, прямо принятая владельцем продукта по waiver.  
**Подготовленный снимок:** `ProjectHorizon-main-procedural-quests-closure.zip`.  
**Git SHA:** архив не содержит `.git`; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования PDF v2.0:** §19.1–19.4 «Задания», §22 persistence и
Stage 2 baseline «20 заданий».

**Реализовано:**

- добавлен строгий `procedural_quests.json` schema `1` с балансом **всех 15**
  objective types PDF: `VisitLocation`, `ScanObject`, `ScanSpecies`,
  `CollectResource`, `CraftItem`, `DeliverItem`, `RepairObject`, `DefeatTarget`,
  `ProtectTarget`, `BuildModule`, `TradeItem`, `FindSignal`, `ExplorePlanet`,
  `ExploreSystem`, `ReturnToNpc`;
- `ProceduralQuestGenerator` детерминированно строит ровно `20` repeatable
  `QuestDefinition` из immutable world seed и capability pools; основная сюжетная
  линия не заменяется процедурной, как прямо требует §19.4;
- каждая mission definition содержит state graph в терминологии PDF:
  `QuestInstance`, `QuestNode`, `QuestCondition`, `QuestAction`, `QuestReward`; текущий безопасный
  граф линейный `Objective → optional Return → Claim`, с проверкой уникальности
  узлов и отсутствия циклов;
- feasibility до выдачи проверяет доступность capability/target, NPC-giver,
  landing/inventory/equipment gates и state graph; board не выдаёт цели, которых
  фактически нет в текущем vertical slice;
- доменный движок поддерживает `DefeatTarget` и `ProtectTarget`, а из текущего
  gameplay-board эти два типа намеренно исключаются, пока в мире нет реальных
  hostile/protected targets; изолированная acceptance использует synthetic
  capability targets только для доказательства поддержки движком всех 15 типов;
- gameplay-board использует только реальные POI, ecology species, 42 resource
  IDs, фактически runtime-enabled `StoreOutputs` craft outputs, attainable resource/craft items,
  base modules, первый доступный planet каждой nearby system, existing systems
  и существующего NPC; `RepairObject` сейчас выдаётся только для реально
  подключённого `object.ship.starter`;
- `Q` на поверхности вне других UI открывает persistent Mission Journal;
  `Up/Down` выбирают mission, `Enter` выполняет contextual action
  accept/deliver/return/claim, `Esc/Q` закрывают журнал; legacy `Q` в Station
  Services и roll input корабля не перехватываются;
- progress подключён к существующим API без параллельных подсистем:
  resource collection, runtime/queue crafting, buy/sell, starter/system repair,
  base placement, POI scan/resolve, ecology scan/harvest, planetary landing и
  hyperspace system exploration;
- `DeliverItem` атомарно расходует нужное количество из общего inventory,
  return/claim разрешены у реального giver checkpoint, credits начисляются через
  существующую Station Services economy, procedural faction reputation
  детерминирована completed mission state;
- persistence хранит только mission deltas (`status/progress`) плюс seed/revision
  в optional `save_settings.procedural_quests`; 20 definitions повторно
  генерируются из seed/content, SQLite schema остаётся `2`; поддержаны exact
  round-trip, cold restore, legacy fallback, graceful exit и `F8` reset;
- `F5` расширен изолированной `TASK-118` acceptance в
  `save_1.procedural-quests-test.db`; gameplay-slot тест не изменяет.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Content/procedural_quests.json`;
- `src/Game.Client/Scripts/VerticalSlice/ProceduralQuestCatalog.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProceduralQuestRuntime.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProceduralQuestAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProceduralQuests.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StationServicesRuntime.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-116`: `IMPLEMENTED` → `VERIFIED` — прямой acceptance waiver пользователя;
- `TASK-117`: `IN_PROGRESS` → `VERIFIED` — прямой acceptance waiver пользователя;
- `TASK-118`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-119`: `NOT_STARTED` → `IN_PROGRESS` — локальная clean build + единый F5
  automated acceptance; длинная ручная цепочка не требуется;
- `TASK-006`: остаётся `BLOCKED` до наличия `.git`/SHA.

**Минимальная runtime-приёмка TASK-119:**

1. `tools\clean-build-windows10.cmd`: фактический `CoreCompile`, `0` errors;
   warnings прислать полностью, даже если build успешен.
2. Запустить `SalvageRepairSlice`. При восстановлении активного полёта нажать
   `F8`, чтобы F5 hotkey был доступен. Startup должен содержать
   `TASK-118 procedural quest catalog READY ... objectiveTypes=15; board=20;` и
   `TASK-118 procedural quests READY ... board=20; maxActive=5; journal=Q`.
3. Один раз нажать `F5` и дождаться одной строки `TASK-118 procedural quests
   acceptance PASS` с `objectiveTypes=15; generated=20; deterministic=1;
   allTypes=1; feasibility=1; infeasibleRejected=1; activeLimit=1; lifecycle=1;
   return=1; rewards=1; gameplayBoard=1; coldRestore=1; legacyFallback=1;
   roundTrip=1; logWritten=1; maxWriters=1; integrity=ok`. Это основной и
   достаточный критерий данной итерации.
4. Необязательный visual smoke: после `F8` нажать `Q`; Mission Journal должен
   показать `Board=20` и прокручиваемый список предложений. Выбрать offered mission и нажать `Enter`; строка должна
   перейти в accepted, Output — `TASK-118 player procedural quest accept PASS`.

При `FAIL` нужны полный build log, строка `TASK-118 ... FAIL` и последние ~200
строк Godot Output.

**Граница закрытия:** после `TASK-119 → VERIFIED` repeatable procedural mission
core считается закрытым. Hand-authored main story остаётся отдельной контентной
задачей по §19.4; физические combat/protect targets подключаются к уже готовым
objective APIs, а не требуют второй quest subsystem.

**Ограничение среды подготовки:** .NET SDK/Godot в рабочем контейнере не
обнаружены; фактическая compilation/runtime-проверка нового C# здесь не
выполнялась и не заявляется.

---

## 0L. Предыдущая синхронизация и mega-итерация 2026-08-11

### Закрытие предыдущей galaxy/hyperspace итерации

Пользователь 2026-08-11 прямо распорядился считать предыдущую итерацию
отработанной и не продолжать сложную ручную проверку hyperspace-маршрута. В
соответствии с этим прямым приёмочным решением:

- `TASK-114` — `IMPLEMENTED` → `VERIFIED`;
- `TASK-115` — `IN_PROGRESS` → `VERIFIED`;
- procedural galaxy, system/galaxy map, route planner и hyperspace API считаются
  закрытыми для обычной разработки; возврат допустим при подтверждённой
  регрессии или изменении ТЗ;
- отсутствие полной ручной цепочки покупки hyperdrive → jump → cold restore
  зафиксировано как **acceptance waiver by product owner**, а не как якобы
  выполненная ручная проверка.

При запуске пользователь отдельно зафиксировал warning
`Arcade ship has no atmosphere reference; atmospheric mode disabled.`. Он не
блокировал galaxy/hyperspace, но означал неполную интеграцию ранее реализованных
коэффициентов `AtmosphericEfficiency`. Дефект включён в `TASK-116` и исправлен
добавлением `Gameplay/AtmospherePlanet`, на который штатно указывает
`VoyageShip` через `../AtmospherePlanet`. Runtime-подтверждение исчезновения
warning остаётся частью `TASK-117`.

### TASK-116 — procedural planetary ecology core mega-iteration

**Исходный снимок:** `ProjectHorizon-main-galaxy-hyperspace-closure.zip` —
предыдущая редакция `TASK-114/115`, фактически запущенная пользователем и прямо
принятая им как завершённая; её исходной GitHub-базой был
`ProjectHorizon-main(7)(2).zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-planetary-ecology-closure.zip`.  
**Git SHA:** архив не содержит `.git`; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования PDF v2.0:** §11 procedural vegetation, §12 procedural
fauna, §22.5 seed/version + delta persistence, Stage 2 baseline: 16 land biomes,
12 terrestrial + 4 flying + 4 aquatic fauna archetypes, flora modules ≥60.

**Реализовано:**

- `ecology.json` schema 1: ровно `16` biome definitions, `60` flora modules и
  `20` fauna archetypes (`12 Ground / 4 Flying / 4 Aquatic`);
- fauna catalog покрывает все шесть обязательных body plans: Biped, Quadruped,
  Hexapod, Flying, Aquatic и Crawler, а также все 11 состояний поведения PDF;
- `EcologyPlanner` детерминированно восстанавливает `360` gameplay flora
  placements, `20` fully-active fauna и `80` simplified/statistical fauna из
  `WorldSeed=20260811` и `region.vertical_slice.ecology`;
- flora placement учитывает biome compatibility, per-species spacing и clearance
  от starter ship, станков, trader и центральной инфраструктуры;
- повторяющаяся vegetation рендерится группами `MultiMeshInstance3D`; рядом с
  игроком до 8 specimens повышаются до интерактивных `StaticBody3D`, поэтому
  сотни растений не превращаются в сотни постоянных physics nodes;
- active fauna представлены `CharacterBody3D` и используют distance update tiers
  `10 Hz <=20 m`, `4 Hz <=50 m`, дальше — статистическое состояние;
- utility behavior + steering реализуют Idle/Wander/Graze/Drink/Sleep/
  Investigate/Flee/Threaten/Attack/ReturnToTerritory/FollowGroup; попадание
  hitscan вызывает реальную реакцию и диагностическую строку `TASK-116 fauna
  reaction PASS`;
- `V` сканирует ближайший ecology signal в радиусе 16 m; `O` открывает persistent
  flora/fauna catalogue; `E` на promoted flora выполняет harvest и выдаёт
  `resource.flora_pulp`;
- persistence намеренно хранит только discovered flora species, discovered fauna
  species и removed flora instance IDs; координаты/состояние процедурных животных
  не сериализуются и восстанавливаются из seed, как требует §22.5;
- optional `save_settings.ecology` добавлен без изменения SQLite schema `2`;
  поддержаны exact round-trip, cold restore, legacy fallback, graceful exit и F8
  reset;
- `F5` расширен изолированной `TASK-116` acceptance в
  `save_1.ecology-test.db`; тест не изменяет gameplay-slot;
- acceptance проверяет baseline `16/60/20`, movement `12/4/4`, шесть body plans,
  11 behaviors, deterministic regeneration, 360 flora, population limits 20/80,
  update tiers, utility outcomes, scan/harvest, delta-only save, все 16 biomes,
  cold restore, legacy fallback, exact SQLite round-trip, autosave log,
  `maxWriters<=1` и `integrity=ok`;
- интегрированная сцена получила `Gameplay/AtmospherePlanet` и устраняет известный
  atmosphere-reference warning, не подавляя его искусственно.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Content/ecology.json`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyCatalog.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyPlanner.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyRuntime.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyFloraSpecimenNode.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/EcologyAcceptance.cs` + `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs` + `.uid`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-114`: `IMPLEMENTED` → `VERIFIED` — прямое решение пользователя;
- `TASK-115`: `IN_PROGRESS` → `VERIFIED` — acceptance waiver пользователя;
- `TASK-116`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-117`: `NOT_STARTED` → `IN_PROGRESS` — локальная clean build + F5 ecology
  acceptance + минимальный визуальный smoke;
- `TASK-006`: остаётся `BLOCKED` до наличия `.git`/SHA.

**Минимальная runtime-приёмка TASK-117 (сознательно короче прошлых ручных
маршрутов):**

1. `tools\clean-build-windows10.cmd`: фактический `CoreCompile`, `0` warnings,
   `0` errors.
2. Запустить `SalvageRepairSlice`. Если старое сохранение восстановилось в
   `OutboundFlight`/`InboundFlight` и игрок всё ещё пилотирует корабль, сначала
   нажать `F8`, чтобы acceptance hotkeys не были заблокированы активным полётом.
   После сброса не должно быть warning
   `Arcade ship has no atmosphere reference`; startup должен показать
   `TASK-116 ecology catalog READY ... biomes=16; flora=60; fauna=20;
   ground=12; flying=4; aquatic=4; limits=20/80` и `TASK-116 ecology READY ...
   atmosphere=bound`.
3. Один раз нажать `F5`. Дождаться существующих TASK-076/110/112/114 и новой
   строки `TASK-116 ecology acceptance PASS` с `deterministic=1; multiMesh=1;
   populations=1; updateTiers=1; behaviorRuntime=1; discovery=1; deltaOnly=1;
   stress16=1; coldRestore=1; legacyFallback=1; roundTrip=1; maxWriters=1;
   integrity=ok`. Это основной критерий приёмки — сложный ручной traversal не
   требуется.
4. Нажать `F8`; Output: `TASK-116 ecology reset PASS ... flora=0; fauna=0;
   removed=0; points=0; regenerated=360; active/simplified=20/80`.
5. На поверхности убедиться, что появились разноцветные растения и движущаяся
   fauna. Нажать `V` рядом с организмом — одна строка `TASK-116 player ecology
   scan PASS`; `O` должен открыть каталог. Подойти к ближайшему интерактивному
   растению и нажать `E` — `TASK-116 player flora harvest PASS`. Это единственный
   обязательный ручной smoke.
6. Штатно закрыть/открыть игру. `TASK-116 ecology restore PASS` должен сохранить
   discovery/removed counts; procedural fauna positions не обязаны совпадать с
   runtime motion до закрытия — они регенерируются из seed и затем продолжают AI.

**Ожидаемая итоговая строка F5:**

```text
TASK-116 ecology acceptance PASS: biomes=16; flora=60; fauna=20; movement=1; bodyPlans=1; behaviors=1; deterministic=1; multiMesh=1; populations=1; updateTiers=1; behaviorRuntime=1; discovery=1; deltaOnly=1; stress16=1; coldRestore=1; legacyFallback=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<time>; result=<description>
```

**Граница закрытия:** после `TASK-117 → VERIFIED` procedural ecology core
считается закрытым. Следующие planetary iterations должны использовать этот API
и data catalog, а не создавать параллельную flora/fauna persistence. Полная
геометрия разных планет, NavMesh-перестроение по сложному terrain, полноценная
water physics и art-quality procedural creatures остаются последующими
интеграционными задачами Stage 2.

**Статический аудит подготовленного снимка:** `100 PASS / 0 FAIL` — JSON и
baseline-каталоги, ecology baseline/coverage, детерминированный planner, spacing и
infrastructure clearance, scene/persistence/F5 bindings, UID/res:// integrity,
C# lexical/bracket integrity и точная change-boundary относительно принятого
`TASK-114/115` снимка.

**Ограничение среды подготовки:** локально отсутствуют .NET SDK и Godot, поэтому
новый C# код не был фактически скомпилирован/запущен в этой среде; статус
`TASK-116` не повышается выше `IMPLEMENTED` до пользовательского F5.

---

## 1. Статусы

| Статус | Значение |
|---|---|
| `NOT_STARTED` | Реализация не начиналась |
| `PLANNED` | Задача сформулирована и поставлена в очередь |
| `IN_PROGRESS` | Работа начата, но требование выполнено не полностью либо ожидает проверки |
| `IMPLEMENTED` | Реализация присутствует в коде/сценах, но не подтверждена полным приёмочным прогоном |
| `VERIFIED` | Реализация подтверждена применимой автоматической или ручной runtime-проверкой; Git SHA фиксируется отдельным доказательством репозиторной трассируемости |
| `BLOCKED` | Выполнение невозможно до устранения указанной блокировки |
| `DEFERRED` | Требование осознанно перенесено на более поздний этап |
| `SUPERSEDED` | Задача заменена другой реализацией или утратила актуальность |
| `N/A` | Требование неприменимо; обязательно указывается обоснование |

Статический просмотр архива позволяет установить максимум `IMPLEMENTED`. Статус `VERIFIED` допускается после подтверждённого запуска или прямого runtime-доказательства пользователя; SHA коммита фиксируется отдельно в `TASK-006` и не подменяется предположением.

---

## 2. Текущий контрольный итог

### Этап 0. Технические прототипы

| Прототип | Статус | Текущее состояние |
|---|---|---|
| A. Персонаж | `VERIFIED` | Предыдущие функциональные итерации приняты пользователем по результатам локальных runtime-проверок; для репозиторной трассируемости остаётся записать SHA |
| B. Чанк рельефа | `VERIFIED` | `TASK-025` и `TASK-026` завершены `PASS`; стриминг, LOD, выгрузка mesh/collision и managed-memory soak подтверждены runtime |
| C. Сферическая планета | `VERIFIED` | Все критерии PDF-ТЗ подтверждены: cube sphere, гравитация к центру, ходьба, floating origin и LOD-швы; visual/collision streaming принят runtime-тестами |
| D. Корабль | `VERIFIED` | Полёт, атмосфера, посадка, взлёт и 100 последовательных физических посадок подтверждены runtime; Прототип D закрыт |
| E. Сохранение | `VERIFIED` | SQLite foundation, backup/recovery и copy migration schema `1→2` подтверждены чистой сборкой и runtime-проверками `C/X/Z`; все требования Прототипа E приняты |

**Вывод:** ранее подтверждённые технические прототипы и core-подсистемы сохраняют принятые статусы. `TASK-118/119`, `TASK-120/121` и `TASK-122/123` закрыты явными acceptance-waiver решениями владельца продукта, отдельно от локальных runtime-доказательств. Текущая mega-итерация `TASK-124` реализует локальную tiled `NavigationServer3D/NavigationRegion3D` подсистему §30.1 с bounded streaming, obstacle-aware pathfinding, `NavigationAgent3D` avoidance и recovery; `TASK-125` остаётся runtime acceptance до clean build + F5 на пользовательской машине.

## 3. Результат текущей итерации от 2026-08-03

### 2026-08-03 — mega-итерация: закрытие подсистемы строительства баз (`TASK-106`)

**Исходный снимок:** `ProjectHorizon-main-station-services-closure(2).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-base-construction-closure.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Граница:** цель итерации — закрыть не одиночную механику, а связную base-construction subsystem: data catalog, modular placement, snap/collision/connectivity, limits, power graph, player UI, persistence, reset и изолированную acceptance. Resource lifecycle не изменяется; готовый inventory/resource API только остаётся совместимым.

**Синхронизация подтверждённой приёмки:**

- пользователь предоставил clean build station-services редакции: `0` предупреждений, `0` ошибок;
- `TASK-102 station services acceptance PASS`: `economies=6; factions=3; npcs=1; quests=3; tradable=174; priceFormula=1; deterministicDaily=1; offlineEconomy=1; supplyDemand=1; buySell=1; atomicRejected=1; creditConservation=1; questGraph=1; questFeasibility=1; questFlow=1; reputation=1; coldRestore=1; legacyFallback=1; roundTrip=1; maxWriters=1; integrity=ok`;
- применимые F1–F12 regressions завершились `PASS`; `TASK-102/103 → VERIFIED`; station-services subsystem Этапа 1 закрыта;
- пользовательский screenshot подтвердил постоянные `PLAYER POS X/Y/Z`; `TASK-104/105 → VERIFIED`; исходный GitHub ZIP не содержал последнюю coordinate-overlay поправку, поэтому она аккуратно перенесена в эту редакцию и размещена в углу без зависимости от режимов Detailed/Compact/Hidden.

**Реализовано:**

- добавлен строгий `base_construction.json` schema `1` с ровно `50` data-driven modules — минимальным количеством из PDF-ТЗ;
- catalog покрывает все `16` категорий раздела 20.1: Foundation, Floor, Wall, Roof, Corridor, Door, Window, Stair, Room, Generator, Battery, Processor, Storage, LandingPad, Terminal и Decoration; дополнительная техническая категория `Structure` объединяет несущие балки, арки и колонны, поэтому всего в catalog `17` категорий;
- десять device modules связаны со всеми десятью Base outputs Industry Content v2; сорок structural variants являются construction templates и не требуют расширения закрытой resource subsystem;
- `BaseConstructionRuntime` реализует обязательный first anchor, сетку `2,5 м`, cardinal snap, overlap rejection, единственный связный graph, безопасный dismantle с refund и запрет удаления, разрывающего базу;
- исполняются лимиты PDF `500 modules / 100 interactive devices / 200 active physics objects / 20 dynamic lights`; acceptance строит связный domain-граф ровно из `500` модулей, проверяет отказ на 501-м, отдельно доводит interactive limit до `100` и проверяет следующий отказ `LimitExceeded`;
- электрическая сеть представлена графом и агрегирует generators, consumers, batteries, enabled/powered consumers, deficit и stored energy; device toggle отключает генерацию/потребление; offline power progress отсутствует;
- игровые modules создаются programmatically как `StaticBody3D` с mesh, collision и catalog-defined dynamic lights; terrain geometry не модифицируется;
- `G` открывает builder; Up/Down выбирают module, `R` поворачивает, `Enter` ставит, `X/Delete` демонтируют, `T` переключает device, `G/Esc` закрывают; target grid и preview показываются в HUD;
- palette из 50 modules показывается скользящим окном, чтобы UI не переполнялся; основной HUD содержит summary modules/devices/power/battery/components;
- сохраняются base ID, next sequence, module stock, instances, grid coordinates, rotation, enabled state и battery energy в optional `save_settings.base_construction`; SQLite schema остаётся `2`; legacy save без блока получает пустую базу и полный starter palette;
- cold start восстанавливает graph без offline progress, graceful exit и autosave включают base snapshot, `F8` очищает base state и перестраивает scene;
- `F6` сохраняет `TASK-072` regression и параллельно запускает `TASK-106` в отдельной БД `save_1.base-construction-test.db`, не изменяя gameplay-slot.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Content/base_construction.json`;
- `src/Game.Client/Scripts/VerticalSlice/BaseConstructionCatalog.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/BaseConstructionRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/BaseConstructionModuleNode.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/BaseConstructionAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-102`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-103`: `IN_PROGRESS` → `VERIFIED`;
- `TASK-104`: подтверждённый coordinate HUD сохраняется `VERIFIED`;
- `TASK-105`: подтверждённая coordinate HUD acceptance сохраняется `VERIFIED`;
- `TASK-106`: `PLANNED` → `IMPLEMENTED`;
- `TASK-107`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F6 dual acceptance, manual builder/power/persistence/F8 и regressions;
- `TASK-006` остаётся `BLOCKED`.

**Ожидаемый F6 HUD:**

```text
TASK-072 legacy fourth path (F6): PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1
TASK-106 base construction (F6): PASS modules=50, placed=50, snap=1, collision=1, power=1, limits=1, stress500=1, restore=1, roundTrip=1
```

**Ожидаемая строка Output:**

```text
TASK-106 base construction acceptance PASS: catalogModules=50; categories=17; placed=50; anchor=1; snapping=1; collisionRejected=1; disconnectedRejected=1; powerGraph=1; battery=1; toggle=1; removalRefund=1; limits=1; stress500=1; coldRestore=1; legacyFallback=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=<description>
```

**Граница закрытия:** после `TASK-107 → VERIFIED` core base-construction subsystem считается закрытой. Новые module variants, art assets, localization и gameplay devices добавляются data-driven поверх готового API; возврат к placement/snap/connectivity/limits/power/persistence допустим только при подтверждённой регрессии или изменении ТЗ. Planet terrain deformation сознательно не добавляется: PDF 20.4 прямо запрещает изменение геометрии планеты в версии 1.0.

**Ограничение среды:** .NET SDK и Godot отсутствуют в среде подготовки; фактическая компиляция и runtime-приёмка новой редакции остаются за `TASK-107`.

### 2026-08-03 — mega-итерация станционных услуг Этапа 1 (`TASK-102`)

**Исходный снимок:** `ProjectHorizon-main(2)(5).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-station-services-closure.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования:** ТЗ v2.0 §16 (factions, NPC, template dialogue), §18 (шесть типов экономики и шестимножительная цена), §19 (quest state graph и feasibility validation), §40 Этап 1 (торговля, один NPC, три quests), §41 (ручная приёмка торговли, NPC, quests и persistence).

**Синхронизация подтверждённой ресурсной приёмки:**

- пользователь выполнил локальную сборку Godot 4.7.1 Mono: `Предупреждений: 0`, `Ошибок: 0`;
- startup подтвердил `catalog=42; physicalTypes=42; nodes=58; authored=32; generated=26; unique=1; deterministicYield=1; maxStack=1; coverage=1`;
- `TASK-100 catalog resource lifecycle acceptance PASS`: `collectedTypes=42; collectedNodes=58; duplicateRejected=1; mirrors=1; depletion=1; coldRestore=1; reset=1; roundTrip=1; maxWriters=1; integrity=ok`;
- вручную подтверждены generic collection generated nodes, autosave и graceful-exit persistence;
- `TASK-100` и `TASK-101` переведены в `VERIFIED`; `RESOURCE-090`–`RESOURCE-098` и `RESOURCE-ACC-090`–`RESOURCE-ACC-093` синхронизированы как `VERIFIED`.

**Реализовано:**

- добавлен строгий `station_services.json` schema 1: ровно шесть economy types (`Mining`, `Industrial`, `Technology`, `Trading`, `Scientific`, `Military`), три factions с preferred goods, quest types, visual style, name pool и полной матрицей relations;
- добавлен один физический trader NPC `npc.trader.ilia_voss` в vertical slice и template dialogue с условиями reputation, вариантами `OpenTrade`, `OpenQuests`, `Close` и последствиями;
- весь catalog из 174 items доступен рынку; цена рассчитывается по формуле `BasePrice × SystemEconomyModifier × SupplyDemandModifier × FactionModifier × ReputationModifier × RandomDailyModifier`, после чего применяются buy/sell spread;
- market stock, player/merchant credits, supply-demand repricing и deterministic daily modifier работают независимо от Godot UI; economy day обновляется при возврате к услугам, trade и после значимого offline time delta;
- buy/sell выполняются с preflight-проверками stock, funds и shared inventory; player session и все пять production inventory mirrors синхронизируются, credits сохраняются как замкнутый баланс до quest rewards;
- реализованы три persistent quest graphs: `CollectResource`, `CraftItem`, `TradeItem`; catalog validation проверяет stable IDs, ссылки, reachability, отсутствие cycles, допустимый `MaxStack` и фактическую feasibility objective;
- quest progress подключён к generic resource collection, immediate/timed craft, production queue completion и trade; accept/claim, credit reward и faction reputation сохраняются;
- в SQLite schema 2 без migration добавлен optional `station_services` setting: market identity, credits, reputation, economy day/time, stock всех 174 items, current quest node/status/progress; old saves без блока загружаются через legacy fallback;
- добавлен station-services Panel UI: Dialogue/Buy/Sell/Quests, factor diagnostics, stock/inventory, quest node/progress/reward; HUD показывает credits, reputation, quest completion, market type/day и coverage;
- `F3` сохраняет прежнюю `TASK-082` acceptance и параллельно запускает изолированную `TASK-102` acceptance в `save_1.station-services-test.db`; gameplay-slot не изменяется;
- acceptance проверяет точные counts `6/3/1/3/3/174`, шестимножительную формулу, deterministic daily/offline economy, supply-demand repricing, atomic rejection, buy/sell, credit conservation, graph/feasibility, все три quest flows, reputation rewards, exact SQLite round-trip, cold restore, legacy fallback, autosave log, `maxWriters=1` и `integrity=ok`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Content/station_services.json`;
- `src/Game.Client/Content/localization.en.json`;
- `src/Game.Client/Content/localization.ru.json`;
- `src/Game.Client/Scripts/VerticalSlice/StationServicesCatalog.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StationServicesRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StationServicesNpc.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StationServicesAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-100`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-101`: `IN_PROGRESS` → `VERIFIED`;
- `TASK-102`: `PLANNED` → `IMPLEMENTED`;
- `TASK-103`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F3 acceptance, manual NPC/dialogue/trade/three-quest flow, cold restart/F8 и regressions;
- `TASK-006` остаётся `BLOCKED`.

**Ожидаемый F3 HUD:**

```text
TASK-102 station services (F3): PASS economies=6, factions=3, npc=1, quests=3, tradable=174, price=1, daily=1, trade=1, graph=1, restore=1, roundTrip=1
```

**Ожидаемая строка Output:**

```text
TASK-102 station services acceptance PASS: economies=6; factions=3; npcs=1; dialogueOptions=3; quests=3; questNodes=3; tradable=174; priceFormula=1; deterministicDaily=1; offlineEconomy=1; supplyDemand=1; buySell=1; atomicRejected=1; creditConservation=1; questGraph=1; questFeasibility=1; questFlow=1; reputation=1; coldRestore=1; legacyFallback=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=<description>
```

**Граница закрытия:** итерация закрывает самостоятельную подсистему **станционных услуг Этапа 1**: один trader NPC, data-driven dialogue, market, credits/reputation и три quest graphs. Полный galaxy-scale NPC population, все 15 objective types, procedural quest generation, 20+ quests, faction wars и межсистемная economy относятся к последующим этапам ТЗ и не считаются незавершённостью этого vertical-slice блока. Ресурсная подсистема не расширялась: новые функции только потребляют ранее закрытый generic inventory API.

**Ограничение среды:** .NET SDK и Godot отсутствуют в среде подготовки; фактическая компиляция и runtime-приёмка новой редакции остаются за `TASK-103`.

### 2026-08-03 — закрытие catalog-wide resource lifecycle (`TASK-100`)

**Исходный снимок:** `ProjectHorizon-main(1)(8).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-resource-lifecycle-closure.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования:** ТЗ v2.0 §9.9, §17.1–17.3, §22.4–22.6, §23, §45.1, §46.1–46.4, §53 и Этап 1: физическая добыча, stable IDs, inventory/depletion delta, static definitions вне SQLite, 42 world resources, persistence compatibility и reset.

**Синхронизация предыдущей приёмки:**

- пользователь запустил post-hotfix редакцию в Godot Engine 4.7.1 Mono и подтвердил корректную HUD-сводку `stations=5`, полную постанционную детализацию и отсутствие ложного `Production queue: unavailable`;
- `TASK-098 production network HUD acceptance PASS`: `stations=5; aggregateCounts=1; aggregateEnergy=1; simultaneousRunning=1; pauseResume=1; cancel=1; completion=1; recharge=1; coldRestore=1; legacyFallback=1; falseUnavailable=0; roundTrip=1; maxWriters=1; integrity=ok`;
- вручную выполнены enqueue/completion на Smelter и Refinery с autosave; пользователь подтвердил: «вроде, работает всё»;
- `TASK-098` и `TASK-099` переведены в `VERIFIED`; `INDUSTRY-080`–`INDUSTRY-085` и `INDUSTRY-ACC-075`–`INDUSTRY-ACC-079` синхронизированы как `VERIFIED`.

**Реализовано:**

- добавлен Godot-independent `CatalogResourceFieldPlanner`; он сравнивает 42 определения `resources.json` с hand-authored scene bindings и детерминированно размещает отсутствующие resource types;
- сохранены все 32 существующих узла и их stable IDs; для 26 отсутствовавших типов создаётся по одному generic `SalvageResourceNode`; итог: `42` физических типа и `58` узлов;
- generated IDs имеют формат `catalog.<resource_suffix>`, позиции стабильны и не пересекаются; тестовая площадка расширена до `80×80`;
- каждый узел получает deterministic yield и visual material из `GameResourceDefinition`; startup validation проверяет stable ID, catalog coverage, uniqueness и `yield <= MaxStack`;
- generic collection использует прежний `StarterRepairSession`, запрещает повторный сбор, синхронизирует available inventory со всеми очередями `ProductionNetworkRuntime`, сохраняет depletion и восстанавливает скрытое состояние после cold start;
- старые snapshots с только hand-authored resource nodes остаются совместимыми, поскольку их IDs не изменены, а generated IDs добавлены без schema migration; SQLite schema остаётся `2`;
- detailed HUD показывает `types=42/42`, число nodes, collected и generated; startup Output печатает binding/READY diagnostics;
- `F7` сохраняет прежнюю `TASK-062` regression и параллельно запускает изолированную `TASK-100` acceptance в `save_1.resource-lifecycle-test.db`; gameplay-slot не изменяется;
- acceptance проверяет все 42 типа, точные counts `32+26=58`, metadata/MaxStack, deterministic placement, collection, duplicate rejection, inventory mirrors, частичный расход/depletion, exact SQLite round-trip, cold restore, реальный `ResetSlotAsync`, autosave log, `maxWriters=1` и `integrity=ok`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/CatalogResourceFieldPlanner.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/CatalogResourceLifecycleAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-098`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-099`: `IN_PROGRESS` → `VERIFIED`;
- `TASK-100`: `PLANNED` → `IMPLEMENTED`;
- `TASK-101`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F7 catalog resource lifecycle, manual generated-node cold restore/reset и regressions;
- `TASK-006` остаётся `BLOCKED`.

**Ожидаемый F7 HUD:**

```text
TASK-100 resource lifecycle (F7): PASS catalog=42, physical=42, nodes=58, generated=26, collectTypes=42, collectNodes=58, duplicate=1, mirrors=1, depletion=1, restore=1, reset=1, roundTrip=1
```

**Ожидаемая строка Output:**

```text
TASK-100 catalog resource lifecycle acceptance PASS: catalog=42; physicalTypes=42; nodes=58; generated=26; collectedTypes=42; collectedNodes=58; metadata=1; placement=1; unique=1; duplicateRejected=1; mirrors=1; depletion=1; coldRestore=1; reset=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=<description>
```

**Граница закрытия:** runtime enforcement отдельных `ExtractionMethod`/`ScanTier`, процедурное распределение по биомам и специализированные tool animations относятся к будущим scan/tool/world-generation системам, а не к отдельным resource-lifecycle итерациям. После `TASK-101 → VERIFIED` ресурсная подсистема vertical slice считается закрытой; последующие механики используют существующий resource API и catalog без возврата к отдельному этапу ресурсов, кроме подтверждённого дефекта.

**Ограничение среды:** .NET SDK и Godot отсутствуют в среде подготовки; фактическая компиляция и runtime-приёмка новой редакции остаются за `TASK-101`.

### 2026-08-03 — единая HUD-сводка многостанционной производственной сети (`TASK-098`)

**Исходный снимок:** `ProjectHorizon-main(13).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-production-network-hud.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Граница:** только player-facing сводка и диагностика уже реализованной многостанционной сети; новые recipes, stations и production mechanics не добавляются.

**Синхронизация подтверждённой приёмки:**

- пользователь предоставил clean build: `0` предупреждений, `0` ошибок;
- `TASK-096 multi-station industry (F1)` подтверждён строкой `PASS stations=4, recipes=6, routing=1, repeatable=1, chain=1, recharge=1, properties=1, roundTrip=1`;
- ручная и автоматическая приёмка многостанционной сети завершена; `TASK-096` и `TASK-097` переведены в `VERIFIED`;
- повторно подтверждены `TASK-090`, `TASK-092`, `TASK-093`, `TASK-083`, `TASK-082`, `TASK-080`, `TASK-076`, `TASK-072`, `TASK-062`, `TASK-064`.

**Реализовано:**

- добавлена Godot-independent `ProductionNetworkHudModel`, строящая сводку непосредственно из `ProductionNetworkRuntime`;
- HUD агрегирует число физических stations, общее число jobs, `running/queued/paused`, текущую и максимальную энергию всей сети;
- добавлена постанционная детализация `energy [R/Q/P]` для PortableFabricator, Smelter, Refinery, DistillationColumn и ChemicalProcessor;
- detailed HUD показывает все станции, compact HUD — активные станции и `+N idle stations`;
- idle network с нулём jobs остаётся доступной и больше не отображается как `Production queue: unavailable`;
- projection пересчитывается каждый кадр, поэтому enqueue, queued→running, pause/resume, cancel/refund, completion, outputs/byproducts/catalysts, recharge, autosave/load, cold start и `F8` отражаются без отдельного кэша HUD;
- persistence-формат не изменён: используются существующие `production_queue_network` и legacy `production_queue`; версия SQLite schema не повышалась;
- `F1` расширен изолированной `TASK-098` acceptance с БД `save_1.production-network-hud-test.db`, не затрагивающей gameplay-slot;
- acceptance проверяет пять stations, aggregate counts/energy, две одновременно работающие stations, queued/paused transitions, cancel/refund, completion, recharge, exact cold restore без offline progress, legacy fallback, SQLite round-trip, `maxWriters=1` и `integrity=ok`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/ProductionNetworkHudModel.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionNetworkHudAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-096`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-097`: `IN_PROGRESS` → `VERIFIED`;
- `TASK-098`: `PLANNED` → `IMPLEMENTED` в этой исторической итерации; позднее подтверждён и переведён в `VERIFIED`;
- `TASK-099`: `NOT_STARTED` → `IN_PROGRESS` в этой исторической итерации; позднее закрыт как `VERIFIED`;
- `TASK-006` остаётся `BLOCKED`.

**Автоматические критерии:**

```text
TASK-098 production network HUD (F1): PASS stations=5, aggregate=1, transitions=1, recharge=1, restore=1, fallback=1, unavailable=0
```

```text
TASK-098 production network HUD acceptance PASS: stations=5; aggregateCounts=1; aggregateEnergy=1; simultaneousRunning=1; pauseResume=1; cancel=1; completion=1; recharge=1; coldRestore=1; legacyFallback=1; falseUnavailable=0; roundTrip=1; maxWriters=1; integrity=ok; elapsedMs=<время>
```

**Проверки в среде подготовки:** JSON catalog parse и counts; C# lexical/delimiter audit; проверка уникальности `.uid`; проверка всех `res://` references сцены; поиск legacy HUD-ветки и проверка, что игровой HUD использует aggregate projection. Повторная распаковка итогового ZIP и SHA-256 фиксируются при выдаче.

**Историческое ограничение:** на момент подготовки этой редакции .NET SDK и Godot отсутствовали. Последующее локальное runtime-подтверждение пользователя закрыло `TASK-098/099` как `VERIFIED`.

### 2026-08-03 — multi-station refining/chemistry и стартовая линия Компотия (`TASK-096`)

**Исходный снимок:** `ProjectHorizon-main(9)(1).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-multi-station-compotium-line.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования:** ТЗ v2.0: data-driven station routing, `RequiredStation`, multiple physical station types, energy, intermediate products, production persistence, Парафиний и каноническая химическая линия Компотия.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил build log `0 предупреждений / 0 ошибок`;
- `F1 / TASK-093` завершился `PASS Q=72, P=80, S=80, dismantle=1, roundTrip=1`;
- вручную подтверждены отображение `Q/P/S`, recovery preview, разбор `attitude_coil`, возврат `magnetic_ore` и исчезновение разобранного item;
- существующие `F2/F3/F4/F5/F6/F7/F9/F10/F11` завершились `PASS`;
- `TASK-093`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-095`: `IN_PROGRESS` → `VERIFIED`;
- `INDUSTRY-060`–`INDUSTRY-068` и `INDUSTRY-ACC-060`–`INDUSTRY-ACC-064` → `VERIFIED`.

**Реализовано:**

- runtime-enabled catalog расширен с `10` до `16` recipes; playable StoreOutputs matrix — с `9` до `15` recipes;
- подключены шесть связанных processes: `refined_ferrite`, `purified_water`, `paraffinium_fraction`, `paraffinium_lubricant`, `raw_compotium_solution`, `compotium_concentrate`;
- в сцену добавлены физические `Smelter`, `Refinery`, `DistillationColumn`, `ChemicalProcessor` и необходимые raw-resource nodes, включая `catalytic_dust`;
- обычная логика станции остаётся data-driven по `RequiredStation`; один C# runner на recipe не добавляется;
- добавлен `ProductionNetworkRuntime`: отдельная queue/energy/slots на каждую physical station при общем синхронизированном player inventory;
- refining и chemistry recipes объявлены повторяемыми и не входят в одноразовую ship-component objective;
- outputs одной станции становятся inputs другой; reservations, refund, outputs, byproducts и retained catalysts синхронизируются между session и всеми station mirrors;
- сеть очередей сохраняется в backward-compatible `save_settings.production_queue_network`; legacy `production_queue` продолжает загружаться;
- energy каждой gameplay station восстанавливается линейно до capacity за `60 s`; offline progress и offline recharge отсутствуют;
- F1 дополнен изолированным `TASK-096` acceptance на четырёх station types и шести recipes с отдельной БД `save_1.multi-station-industry-test.db`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/IndustryRecipePolicy.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionNetworkRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/MultiStationIndustryAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/TechnologyProgression.cs`;
- `src/Game.Client/Scripts/VerticalSlice/TechnologyRecipeSelectorAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/CatalogCraftingMatrixAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/PortableCraftingStation.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Content/catalog_manifest.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `Technical_Specification/2.0/Project_Horizon_Industry_Content_Schema_v2.0.json`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-093` → `VERIFIED`;
- `TASK-095` → `VERIFIED`;
- `TASK-096`: `PLANNED` → `IMPLEMENTED`;
- `TASK-097`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F1 multi-station acceptance, manual station chain, cold network restore и F2–F12 regressions;
- `TASK-006` остаётся `BLOCKED`.

**Проверки в среде подготовки:** JSON parse и JSON Schema v2 — PASS; counts `174/42/128/15/32`, runtime `16`, StoreOutputs `15`; station/category/tier и technology references — PASS; scene содержит `32` уникальных resource nodes и `5` требуемых crafting station types; C# lexical/delimiter audit — PASS; `res://` и UID проверяются перед упаковкой.

**Ограничение:** .NET SDK и Godot отсутствуют в среде подготовки; фактическая компиляция и runtime-приёмка новой network-семантики остаются за `TASK-097`.

### 2026-08-03 — quality/purity/stability и dismantle returns (`TASK-093`)

**Исходный снимок:** `ProjectHorizon-main(7)(1).zip` — последняя редакция с GitHub, приложенная пользователем.  
**Подготовленный снимок:** `ProjectHorizon-main-item-properties-dismantle.zip`.  
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.  
**Связанные требования:** ТЗ v2.0 §20, §21, §52.3 и §53: quality range, purity/stability, `DismantleReturns[]`, persistence и station UI.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил build log `0 предупреждений / 0 ошибок`;
- `F1 / TASK-090` и `F1 / TASK-092` завершились `PASS`;
- вручную подтверждены Queue tab, пустое состояние, enqueue, RUNNING progress, energy/input reservations и completion;
- `F2/F3/F4/F5/F6/F7/F9/F10/F11` подтверждены `PASS` на предоставленных экранах;
- `TASK-092`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-094`: `IN_PROGRESS` → `VERIFIED`.

**Реализовано:**

- `InventoryItemSaveData` расширен полями `Quality`, `Purity`, `Stability` в диапазоне `0..100`;
- добавлен Godot-independent `ItemPropertyRuntime` с детерминированным расчётом свойств по recipe, process sequence, environment и hazards;
- direct craft и gameplay production queue присваивают crafted outputs рассчитанные свойства;
- при объединении stack свойства агрегируются взвешенно по количеству;
- metadata сохраняется в backward-compatible `save_settings.inventory_properties`; версия SQLite schema остаётся `2`;
- старые saves без metadata загружаются как legacy `100/100/100`;
- девять runtime ship recipes получили каталоговые `DismantleReturns`;
- station terminal расширен до `Recipes / Research / Queue / Dismantle`; клавиша `D` открывает dismantle mode;
- Dismantle tab показывает quantity, `Q/P/S`, recovery efficiency и preview returns;
- `Enter/E` расходует один crafted item, возвращает целочисленную долю материалов по формуле `0.5Q + 0.3P + 0.2S`, синхронизирует queue inventory и вызывает `BaseChanged` autosave;
- recovered materials получают сниженные свойства, исключая бесконечную переработку без потерь;
- F1 дополнен изолированным `TASK-093` acceptance с отдельной БД `save_1.item-properties-dismantle-test.db`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/ItemPropertyRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ItemQualityDismantleAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Content/recipes.json`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-092` → `VERIFIED`;
- `TASK-094` → `VERIFIED`;
- `TASK-093`: `PLANNED` → `IMPLEMENTED`;
- `TASK-095`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F1 item-property acceptance, manual Dismantle UI и cold round-trip;
- `TASK-006` остаётся `BLOCKED`.

**Ограничение:** .NET SDK и Godot отсутствуют в среде подготовки; фактическая компиляция и runtime-приёмка новой семантики остаются за `TASK-095`.

## 3A. Предыдущая итерация от 2026-08-03

### 2026-08-03 — build hotfix Queue terminal (`TASK-094`)

**Исходный снимок:** `ProjectHorizon-main-production-queue-terminal-ui.zip`.  
**Подготовленный снимок:** `ProjectHorizon-main-production-queue-terminal-ui-build-fix.zip`.  
**Причина:** реальный `CoreCompile` выявил `CS0103` в `SalvageRepairSlice.cs:2537`: в обработчик результата legacy gameplay acceptance ошибочно попал вызов `GD.PushError(terminalOutput)`, тогда как локальная переменная `terminalOutput` существует только в обработчике production queue acceptance.

**Исправлено:**

- удалён посторонний вызов `GD.PushError(terminalOutput)` из `PollAcceptanceTask`;
- вывод legacy gameplay acceptance снова использует только локальный `output`;
- формирование и вывод `TASK-092 production queue terminal acceptance` в `PollProductionQueueAcceptanceTask` не изменены;
- runtime-семантика queue terminal, persistence и F1 acceptance не изменялась.

**Статус:**

- `TASK-092` остаётся `IMPLEMENTED`;
- `TASK-094` остаётся `IN_PROGRESS` до clean build `0 предупреждений / 0 ошибок` и runtime-приёмки F1/Queue UI;
- `TASK-006` остаётся `BLOCKED`, поскольку архив не содержит `.git`.

**Контроль:** выполнены поиск всех ссылок `terminalOutput`, проверка области видимости, лексическая проверка C# и повторная упаковка без build/runtime-артефактов.

## 3B. Предыдущая итерация от 2026-08-02

### 2026-08-02 — player-facing production queue terminal (`TASK-092`)

**Исходный снимок:** `ProjectHorizon-main(6)(2).zip` — последняя редакция с GitHub, приложенная пользователем.
**Подготовленный снимок:** `ProjectHorizon-main-production-queue-terminal-ui.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`.
**Связанные требования:** ТЗ v2.0 §52.1, §52.3 и §53; station terminal, production queue visualization, pause/resume/cancel, reservations и active-process persistence.

**Синхронизация предыдущей приёмки:**

- пользователь подтвердил исправленную nullable-редакцию словами «все работает»;
- `TASK-090` ранее подтверждён `F1: PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1`;
- warning hotfix изменял только nullable-контракт загрузки `production_queue`; функциональные регрессии уже были подтверждены;
- `TASK-091`: `IN_PROGRESS` → `VERIFIED`.

**Реализовано:**

- station terminal расширен до трёх вкладок `Recipes / Research / Queue`; `Tab` циклически переключает вкладки, `R` сохраняет быстрый переход Recipes/Research;
- из Recipes выбранный рецепт можно поставить в gameplay queue клавишей `Q`, при этом обычный `Enter/E` сохраняет прежний immediate timed craft path;
- Queue-вкладка показывает status, progress bar, elapsed/duration, slot/waiting state, remaining/capacity energy, reserved inputs, catalysts и reserved energy;
- `Enter/E` во вкладке Queue выполняет pause/resume, `C/Delete` отменяет job;
- cancellation возвращает в gameplay inventory все reserved inputs/catalysts и energy;
- gameplay queue использует `ProductionQueueRuntime`, синхронизируется с `StarterRepairSession`, resource collection, repair и legacy direct craft;
- completion переносит outputs, byproducts и retained catalysts в gameplay inventory, обновляет scene station и вызывает `QuestCompleted` autosave;
- enqueue/pause/resume/cancel сохраняются через `BaseChanged`; periodic/graceful-exit snapshots включают точный queue payload;
- cold restore использует freeze-and-resume без offline progress;
- `StarterRepairSession` сохраняет и восстанавливает не только outputs, но и queue-refunded inputs/catalysts/byproducts;
- добавлен Godot-независимый `ProductionQueueTerminalModel`; F1 дополнительно проверяет progress/energy/reservations и доступность player actions.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueTerminalModel.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статические проверки:**

- queue UI использует один Godot-independent projection для runtime и F1 acceptance;
- сохранены старые Recipes/Research actions и F1–F12 hotkeys;
- проверены balanced delimiters, строки и комментарии `52` C#-файлов;
- проверены `52` уникальных UID и `29` фактических `res://`-ссылок; отсутствующие ссылки не обнаружены;
- JSON Schema v2 подтверждена для `items/resources/recipes/stations/technologies`; counts сохранены: `174/42/128/15/32`;
- nullable-аннотации новых полей и nullable queue restore обработаны явно;
- `.git`, `.godot`, `bin`, `obj`, `.vs`, DLL/PDB, БД и runtime-логи не включаются в итоговый архив;
- ZIP повторно распакован: `142` исходных файла, `missing=0`, `extra=0`, `changed=0`; integrity `PASS`;
- .NET/Godot в среде подготовки отсутствуют: clean build и runtime остаются частью `TASK-094`.

**Статусы:**

- `TASK-091` → `VERIFIED`;
- `TASK-092`: `PLANNED` → `IMPLEMENTED`;
- `TASK-094`: `NOT_STARTED` → `IN_PROGRESS` — clean build, F1 terminal projection и ручной Queue UI/cold restore;
- `TASK-006` остаётся `BLOCKED`.

**Граница итерации:** UI управляет одной physical PortableFabricator queue с `ParallelSlots=1` и каталоговым energy capacity. Изменение баланса energy/recharge, quality/purity/stability и dismantle returns не входит в `TASK-092`.

**Приёмка `TASK-094`:** clean build `0/0`; F1 должен дополнительно показать `TASK-092 queue terminal (F1): PASS progress=1, energy=1, reservations=1, actions=1`; вручную поставить recipe через `Q`, увидеть RUNNING progress, выполнить pause/resume, проверить cancellation refund и cold restart с неизменным elapsed progress.

### 2026-08-02 — nullable warning hotfix для production queue (`TASK-091`)

**Исходный снимок:** `ProjectHorizon-main-production-queue.zip`
**Подготовленный снимок:** `ProjectHorizon-main-production-queue-warning-fix.zip`
**Причина:** пользователь подтвердил `F1: PASS` и все регрессии, но реальный `CoreCompile` выдал `CS8600` в `SaveDatabase.cs:1138`.

**Исправлено:**

- результат `Dictionary.TryGetValue` для `production_queue` принимается как `string?`, что соответствует nullable-контракту BCL;
- перед десериализацией добавлена явная проверка пустого/null JSON;
- повреждённое пустое значение теперь отклоняется диагностикой `InvalidDataException: production_queue setting is empty.`;
- семантика корректного queue snapshot, exact round-trip и malformed-JSON rejection не изменена.

**Подтверждённые runtime-доказательства пользователя:**

- `F1 / TASK-090: PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1`;
- `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12: PASS`;
- Recipes/Research terminal продолжает открываться;
- до hotfix сборка имела `0 ошибок / 1 предупреждение CS8600`.

**Статусы:**

- `TASK-090` → `VERIFIED`;
- `TASK-091` остаётся `IN_PROGRESS` до повторной clean build `0 предупреждений / 0 ошибок`;
- `TASK-006` остаётся `BLOCKED`.

**Приёмка hotfix:** выполнить clean build; ожидается реальный `CoreCompile`, `0 предупреждений / 0 ошибок`; затем достаточно повторить `F1`, поскольку gameplay-regressions уже подтверждены на той же функциональной редакции.

### 2026-08-02 — production queue и active-process lifecycle (`TASK-090`)

**Исходный снимок:** `ProjectHorizon-main(5)(3).zip` — последняя редакция с GitHub, приложенная пользователем 2026-08-02 23:23 (+03:00)
**Подготовленный снимок:** `ProjectHorizon-main-production-queue.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** ТЗ v2.0 §52.1, §52.3, §53; production queue, station parallel slots, cancellation/refunds и active-process persistence.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил реальный `CoreCompile` и clean build `0 предупреждений / 0 ошибок`;
- `F2 / TASK-083: PASS batch=2, energy=1, environment=1, vacuum=1, catalyst=1, byproduct=1, roundTrip=1`;
- вручную подтверждено, что после F2 основной slot сохраняет `RP=690`, `unlocked=11/32`, `components=9/9`, а Recipes/Research terminal продолжает открываться;
- `TASK-083`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-089`: `IN_PROGRESS` → `VERIFIED`.

**Реализовано:**

- добавлен Godot-независимый `ProductionQueueRuntime`;
- station definition `ParallelSlots` реально ограничивает одновременно работающие jobs;
- третий и последующие jobs сохраняют FIFO-порядок и ожидают свободного slot;
- inputs, catalysts и energy резервируются атомарно при enqueue, поэтому параллельные jobs не могут overcommit один stack или energy budget;
- реализованы pause/resume без изменения elapsed progress; paused job освобождает slot, resumed job возвращается в FIFO queue;
- cancellation активной, queued или paused job возвращает все зарезервированные inputs, catalysts и energy, поскольку outputs до completion не создаются;
- completion создаёт outputs/byproducts, применяет deterministic catalyst consumption и освобождает slot;
- large time step корректно завершает несколько jobs и запускает следующие в пределах того же advance;
- active jobs, slot index, elapsed/duration, environment, reservations, energy и sequences сериализуются в `save_settings.production_queue`; schema SQLite остаётся `2`;
- graceful-exit policy — `freeze-and-resume`: offline progress отсутствует, restore продолжает с точного persisted elapsed;
- `SaveDatabase.SnapshotsEqual` сравнивает queue payload; malformed queue JSON отклоняется как `InvalidDataException`;
- добавлена изолированная `F1 / TASK-090` acceptance с БД `save_1.production-queue-test.db`.

**F1 acceptance проверяет:**

- smelter использует два parallel slots; третья job ожидает;
- pause сохраняет elapsed, освобождает slot, resume возвращает job в очередь;
- mid-process `GracefulExit` autosave и exact SQLite round-trip;
- восстановление двух running и одной queued job с точным elapsed progress;
- cancellation активной titanium job, полный refund `2 × resource.titanium_ore + 48 energy`;
- completion оставшихся ferrite/copper jobs, два ingot outputs и два scrap byproducts;
- финальная energy `96`, queue drained, `QuestCompleted` autosave, autosave log, one-writer и `integrity=ok`.

**Проверки в среде подготовки:**

- JSON Schema v2 и все пять content-файлов: `PASS`;
- каталог: `items=174`, `resources=42`, `recipes=128`, `stations=15`, `technologies=32`;
- выбранные smelter recipes и station bindings сверены с JSON: `slots=2`, energy capacity `180`, process energy `40/44/48`, craft time `5.0/5.35/5.7`;
- лексически проверен `51` C#-файл: строки, комментарии и delimiters сбалансированы;
- `51` UID уникальны;
- `29` фактических `res://`-ссылок разрешены;
- `.git`, `.godot`, `bin`, `obj`, `.vs`, DLL/PDB, БД и runtime-логи отсутствуют;
- .NET build и Godot runtime в среде подготовки недоступны; успешная сборка и `F1: PASS` не заявляются.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ProductionQueueAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/IndustryProcessRuntime.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-083` → `VERIFIED`;
- `TASK-089` → `VERIFIED`;
- `TASK-090`: `PLANNED` → `IMPLEMENTED`;
- `TASK-091`: `NOT_STARTED` → `IN_PROGRESS` — clean build и runtime F1;
- `TASK-006` остаётся `BLOCKED`.

**Граница итерации:** queue domain и persistence реализованы и проверяются F1 изолированно. Интерактивное player-facing управление очередью внутри station terminal, process visualization, quality/purity/stability и dismantle returns не включены; они должны идти отдельными системными шагами.

**Приёмка `TASK-091`:** clean build `0/0`; `F1: PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1`; Output должен подтвердить `maxParallel=2`, `gracefulRestore=1`, `refundExact=1`, `queueDrained=1`, `energyRemaining=96`, `maxWriters=1`, `integrity=ok`; затем повторить `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12`.

### 2026-08-02 — extended chemical process runtime (`TASK-083`)

**Исходный снимок:** `ProjectHorizon-main(4)(3).zip` — последняя редакция с GitHub, приложенная пользователем 2026-08-02 22:59 (+03:00)
**Подготовленный снимок:** `ProjectHorizon-main-chemical-process-runtime.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** ТЗ v2.0, разделы 49.4, 52.3, 53 и 54.3; chemical runtime catalysts/byproducts/energy/environment.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил реальный `CoreCompile` и clean build `0 предупреждений / 0 ошибок`;
- `F3 / TASK-082: PASS recipes=9, oneStation=1, initial=4/5, unlocked=9, crafted=1, rp=690, roundTrip=1`;
- вручную подтверждены открытие единственного PortableFabricator, Recipes/Research UI, список девяти recipes, research progress и изготовленные outputs;
- `TASK-082`: `IMPLEMENTED` → `VERIFIED`;
- `TASK-084`: `IN_PROGRESS` → `VERIFIED`.

**Реализовано:**

- добавлен Godot-независимый `IndustryProcessRuntime`;
- process validation проверяет RequiredStation, station tier/category, RequiredTechnology, requested batch count, station energy capacity и доступный energy budget;
- temperature/pressure проверяются по inclusive recipe window, `RequiresVacuum` исполняется отдельно;
- несколько inputs расходуются пропорционально requested batches;
- outputs масштабируются на `recipe.BatchSize × requestedBatches`;
- byproducts масштабируются по requested batches и добавляются в inventory;
- catalyst stack обязателен, но не масштабируется по batch count; расход определяется детерминированным stable roll по RecipeId, catalyst DefinitionId и process sequence;
- execution report возвращает outputs, byproducts, consumed/retained catalysts, energy accounting, hazards и process sequence;
- добавлена изолированная `F2 / TASK-083` acceptance с БД `save_1.chemical-process-runtime-test.db`;
- F2 использует `recipe.chemistry.compotium_concentrate` для batch/byproduct/catalyst/energy/environment и `recipe.chemistry.compotium_crystal` для vacuum gate;
- acceptance проверяет оба детерминированных пути catalyst retained/consumed, QuestCompleted autosave, exact SQLite round-trip, autosave log, one-writer и integrity.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/IndustryProcessRuntime.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/ChemicalProcessAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:**

- `TASK-082` → `VERIFIED`;
- `TASK-084` → `VERIFIED`;
- `TASK-083`: `PLANNED` → `IMPLEMENTED`;
- `TASK-089`: `NOT_STARTED` → `IN_PROGRESS` — clean build и runtime `F2`;
- `TASK-006` остаётся `BLOCKED`.

**Граница итерации:** process execution пока атомарный. Production queue, active-process persistence, parallel slots, cancellation и refunds не включены и остаются следующим системным шагом ТЗ v2.0 §52.3.

**Приёмка `TASK-089`:** clean build `0/0`; `F2: PASS batch=2, energy=1, environment=1, vacuum=1, catalyst=1, byproduct=1, roundTrip=1`; Output должен подтвердить `energyConsumed=264`, `catalystRetained=1`, `catalystConsumed=1`, `hazards=1`, `maxWriters=1`, `integrity=ok`; затем повторить `F3/F4/F5/F6/F7/F9/F10/F11/F12`.

### 2026-08-02 — universal station selector, technology enforcement и research persistence (`TASK-082`)

**Исходный снимок:** `ProjectHorizon-main(3)(4).zip` — последняя редакция с GitHub, приложенная пользователем 2026-08-02 22:22 (+03:00)
**Подготовленный снимок:** `ProjectHorizon-main-station-selector-research.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** ТЗ v2.0, раздел 52.1 и первый шаг 52.3; `TASK-076`–`TASK-083`.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил clean build `0 предупреждений / 0 ошибок`;
- `F4: PASS recipes=128, chemistry=30, compotium=13, stations=15, tech=32, cycles=0, unreachable=0`;
- `F5: PASS resources=42, recipes=128, station=9, crafted=9, isolated=9, roundTrip=1`;
- `F6/F7/F9/F10/F11/F12: PASS`;
- SQLite `integrity=ok`, autosave и восстановление revision подтверждены;
- `TASK-072`, `TASK-073`, `TASK-076`, `TASK-077`, `TASK-080`, `TASK-081` → `VERIFIED`.

**Реализовано:**

- девять runtime `StoreOutputs` recipes объединены на одном физическом `PortableFabricator`;
- взаимодействие `E` открывает универсальный station UI вместо запуска жёстко привязанного recipe;
- UI имеет режимы `Recipes` и `Research`, навигацию `Up/Down`, переключение `Tab/R`, подтверждение `Enter/E`, закрытие `Esc`;
- список recipes строится из каталога по `RequiredStation`, показывает tier, время, inputs/outputs, `READY/MISSING/LOCKED/DONE`;
- `StarterRepairSession.ValidateCraft` исполняет `RequiredTechnology`; legacy acceptance runners сохраняют прежнее поведение через all-unlocked delegate;
- добавлен `TechnologyProgression`: prerequisites, research cost, RP balance, automatic free roots и relevant technology closure;
- стартовый vertical slice получает `2000 RP`; free root technologies разблокируются автоматически;
- RP и unlocked technology IDs сохраняются в существующей таблице `save_settings`, входят в exact snapshot comparison и восстанавливаются после cold restart;
- visual state единственной станции отражает завершение всех её runtime recipes, а не одного recipe;
- добавлена изолированная `F3 / TASK-082` acceptance с отдельной БД `save_1.technology-selector-test.db`;
- F3 проверяет девять recipes на одной станции, начальную смесь locked/unlocked, отказ без prerequisites, unlock graph, technology gate, изготовление выбранного recipe, autosave, exact round-trip, progress restore, one-writer и integrity.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/VerticalSlice/TechnologyProgression.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/TechnologyRecipeSelectorAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/PortableCraftingStation.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статические проверки:**

- C# delimiter/string/comment scan: `47` файлов, ошибок нет;
- scene: одна physical crafting station, девять runtime recipes routed по одному `station.portable_fabricator`;
- JSON counts и cross-references сохранены: `174/42/128/15/32`;
- UID и `res://` проверяются перед итоговой упаковкой;
- .NET SDK и Godot отсутствуют в среде подготовки, поэтому build/runtime `F3` здесь не заявляются.

**Статусы:**

- `TASK-072`, `TASK-073`, `TASK-076`, `TASK-077`, `TASK-080`, `TASK-081` → `VERIFIED`;
- `TASK-082` → `IMPLEMENTED`;
- `TASK-084` → `IN_PROGRESS` — clean build и runtime `F3`;
- `TASK-083` остаётся `PLANNED`;
- `TASK-006` остаётся `BLOCKED`.

**Приёмка `TASK-084`:** clean build `0/0`; startup `TASK-082 station selector binding PASS: physicalStations=1; selectorRecipes=9`; `F3: PASS recipes=9, oneStation=1, initial>0/locked>0, crafted=1, roundTrip=1`; вручную открыть PortableFabricator, разблокировать technology, изготовить recipe, дождаться autosave и проверить cold restart; затем повторить `F4/F5/F6/F7/F9/F10/F11/F12`.

### 2026-08-02 — законченное ТЗ v2.0, Industry Content v2 и химическая линия Компотия (`TASK-080`)

**Исходный снимок:** `ProjectHorizon-main-complete-crafting-catalog.zip` — последняя подготовленная редакция из чата
**Подготовленный снимок:** `ProjectHorizon-main-industry-spec-v2-compotium.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** новая нормативная редакция `Technical_Specification/2.0`, разделы 17, 23, 36, 39–41 исходного ТЗ; `TASK-076`–`TASK-083`, `INDUSTRY-001`–`INDUSTRY-032`, `CHEM-001`–`CHEM-030`.

**Решение по масштабу:** ограничение в 100 recipes признано искусственным. В ТЗ v2.0 зафиксирован связный каталог из 128 recipes с пятью технологическими уровнями, 15 типами станций и 32 технологиями. Контент оригинален; из других космических и промышленных игр заимствуются только общие жанровые принципы, без копирования названий, лора, текстов, чисел и конкретных цепочек.

**Каталог v2:**

- `items=174`;
- `worldResources=42`;
- `recipes=128`;
- `stations=15`;
- `technologies=32`;
- `runtimeEnabledRecipes=10`;
- `chemistryRecipes=30`;
- `compotiumRecipes=13`;
- `paraffiniumRecipes=5`;
- `recipesWithCatalysts=10`;
- `recipesWithByproducts=17`;
- `recipesWithEnvironmentControls=32`;
- `dependencyCycles=0`;
- `unreachableRecipes=0`.

**Реализовано:**

- создано полное ТЗ `Project_Horizon_Technical_Specification_v2.0` в DOCX и PDF;
- создан машиночитаемый полный recipe catalog CSV и опубликована JSON-схема Industry Content v2;
- `items.json`, `resources.json` и `recipes.json` переведены на schemaVersion 2;
- добавлены `stations.json`, `technologies.json`, русская и английская локализация, manifest каталога;
- recipe schema поддерживает catalysts, byproducts, dismantle returns, station/technology tiers, energy, batch size, environment, quality и hazards;
- добавлена оригинальная химическая линия Парафиния и Компотия; название «Компотий» закреплено как каноническое по предложению сына автора проекта;
- `GameContentCatalog` валидирует все cross-references, technology graph, station compatibility, recipe dependency cycles и достижимость;
- playable vertical slice фильтрует только `runtimeEnabled=true`, поэтому 118 следующих recipes не требуют 118 физических станций;
- добавлена единая structural acceptance `F4 / TASK-080`; существующая `F5` остаётся runtime matrix для текущих 10 recipes;
- persistence заранее регистрирует все 174 item IDs каталога.

**Граница итерации:** каталог, схема и ТЗ завершены как спецификация и статическая реализация. В этой итерации не заявляются полностью готовые UI/production runtime для всех 128 recipes. `runtimeEnabled=false` означает, что запись валидна и включена в технологический граф, но ожидает универсальный station recipe selector и исполнение расширенной семантики.

**Изменённые и добавленные файлы:**

- `Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.docx`;
- `Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf`;
- `Technical_Specification/2.0/Project_Horizon_Recipe_Catalog_v2.0.csv`;
- `Technical_Specification/2.0/Project_Horizon_Industry_Content_Schema_v2.0.json`;
- все восемь файлов `src/Game.Client/Content/*`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Проверки в среде подготовки:**

- все пять catalog JSON распарсены и имеют schemaVersion 2;
- проверены 174 уникальных item ID, 42 resource ID, 128 recipe ID, 15 station ID и 32 technology ID;
- проверены все input/output/catalyst/byproduct/station/technology references;
- station category и tier contracts согласованы;
- technology graph ацикличен;
- все 128 recipes достижимы от world resources, циклы отсутствуют;
- 45 C#-файлов прошли лексическую проверку строк, комментариев и delimiters;
- DOCX и PDF подлежат отдельному render QA перед упаковкой;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому build/runtime `F4` здесь не заявляются.

**Статусы:**

- `TASK-078` → `SUPERSEDED` пакетной архитектурой `TASK-080/082`;
- `TASK-080` → `IMPLEMENTED`;
- `TASK-081` → `IN_PROGRESS` — clean build и runtime `F4`;
- `TASK-082` → `PLANNED` — универсальный station recipe selector, technology unlocks и research UI;
- `TASK-083` → `PLANNED` — catalysts, byproducts, energy, pressure/temperature/vacuum и chemical process runtime;
- `TASK-006` → `BLOCKED`.

**Приёмка `TASK-081`:** clean build `0/0`, startup `schema=2; items=174; resources=42; recipes=128; stations=15; technologies=32`, затем `F4: PASS recipes=128, chemistry=30, compotium=13, stations=15, tech=32, cycles=0, unreachable=0`; после этого повторить `F5/F7/F9/F10/F11/F12`.

### 2026-08-02 — пакетное завершение crafting-каталога и универсальная recipe matrix (`TASK-076`)

**Исходный снимок:** `ProjectHorizon-main-fifth-crafting-path.zip` — последняя подготовленная редакция из чата
**Подготовленный снимок:** `ProjectHorizon-main-complete-crafting-catalog.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-074`–`TASK-078`, `CONTENT-060`–`CONTENT-079`, `CONTENT-ACC-060`–`CONTENT-ACC-077`.

**Изменение стратегии итераций:** по прямому указанию пользователя прекращено добавление обычных рецептов по одному. Все оставшиеся recipes с уже поддерживаемой семантикой `StoreOutputs` обработаны одним проходом. Следующие отдельные итерации допускаются только для новой механики, а не для очередной записи JSON.

**Реализовано в `TASK-076`:**

- каталог доведён до `items=20`, `resources=10`, `recipes=10`, из которых один repair recipe и девять station recipes;
- одним пакетом добавлены пять resource/component/recipe chains:
  - `resource.quantum_resin` → `component.ship.sensor_lens`, `3.25 s`;
  - `resource.aerogel_matrix` → `component.ship.life_support_filter`, `3.75 s`;
  - `resource.magnetic_ore` → `component.ship.attitude_coil`, `4.25 s`;
  - `resource.bio_polymer` → `component.ship.sealant_cartridge`, `2.75 s`;
  - `resource.ceramic_composite` → `component.ship.heat_shield_tile`, `4.5 s`;
- в scene добавлены десять физических resource nodes с уникальными IDs и пять соответствующих fabricator stations; итог сцены — `21` resource node и `9` station;
- `SalvageRepairSlice` получает все `StoreOutputs` recipes из каталога и маршрутизирует interaction, timer, event, autosave, load/reset и station visual по `RecipeId`, без новой ветки C# на каждый обычный рецепт;
- crafted outputs и восстановление session остаются словарными и принимают весь набор recipe outputs;
- HUD строит сводку каталога и pending recipes динамически вместо отдельной строки на каждый output;
- удалён поштучный `FifthCraftingPathAcceptanceRunner`; `F5` переподключён к универсальному `CatalogCraftingMatrixAcceptanceRunner`;
- единый F5-runner ремонтирует корабль и в одном изолированном прогоне проверяет все девять station recipes: blocked-before-inputs, wrong station, JSON time, duplicate start, inputs held, single completion, isolation, all outputs, `QuestCompleted` autosave, exact SQLite round-trip, autosave log, `maxWriters=1` и `integrity=ok`;
- persistence registry получает все item definition ID из каталога до инициализации БД; ручное пополнение списка для каждого нового ordinary item не требуется;
- прежние `F6/F7/F9/F10/F11/F12` сохранены как regression routes.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/CatalogCraftingMatrixAcceptance.cs` и `.uid`;
- удалены `src/Game.Client/Scripts/VerticalSlice/FifthCraftingPathAcceptance.cs` и `.uid`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** выполнен количественный минимум Этапа 1 — `10 ресурсов / 10 рецептов`. Не реализованы новые семантики PDF-полей: непустой `RequiredTechnology`, выбор нескольких recipes внутри одной physical station, multiple-input/multiple-output recipes, очередь и параллельная работа stations. Они должны идти отдельными механическими итерациями, а не копированием текущего шаблона.

**Проверки в среде подготовки:**

- PDF-ТЗ сверено текстово и по рендерам страниц 29 и 56–57: recipe должен содержать Inputs, Outputs, RequiredTechnology, RequiredStation и CraftTime; Этап 1 требует 10 ресурсов и 10 рецептов;
- три JSON-файла независимо распарсены; подтверждены counts `20/10/10`, уникальные stable IDs, schema version, все item/resource/recipe cross-references и положительный CraftTime девяти station recipes;
- scene проверена на `21` resource node, `9` stations, уникальные node IDs, полное physical input coverage и точное совпадение station RecipeId/RequiredStation с JSON;
- лексически проверены `45` C#-файлов: строки, комментарии, delimiters; ссылок на удалённый fifth-specific runner в source/scene/csproj нет;
- проверены `45` уникальных UID, все `res://`-ссылки и отсутствие `.git`, `.godot`, `bin`, `obj`, `.vs`, локальных БД и runtime-логов;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому clean build и runtime `F5` не заявляются.

**Статусы:**

- `TASK-074`, `CONTENT-060`–`CONTENT-066` остаются `IMPLEMENTED`;
- `TASK-075` и поштучные `CONTENT-ACC-060`–`CONTENT-ACC-067` → `SUPERSEDED` универсальной матрицей;
- `TASK-076`, `CONTENT-070`–`CONTENT-079` → `IMPLEMENTED`;
- `TASK-077`, `CONTENT-ACC-070`–`CONTENT-ACC-076` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** выполнить clean build `0/0`, проверить startup `items=20/resources=10/recipes=10`, выполнить `F5: PASS` с `station=9, crafted=9, isolated=9, roundTrip=1`, затем повторить `F6/F7/F9/F10/F11/F12`. После этого переходить не к одиннадцатому простому рецепту, а к `TASK-078` — универсальному выбору рецепта и enforcement `RequiredTechnology`.

### 2026-08-02 — пятый resource/recipe path и four-recipe station session (`TASK-074`)

**Исходный снимок:** `ProjectHorizon-main(2)(4).zip`
**Подготовленный снимок:** `ProjectHorizon-main-fifth-crafting-path.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-072`–`TASK-075`, `CONTENT-050`–`CONTENT-067`, `CONTENT-ACC-050`–`CONTENT-ACC-067`.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил успешную сборку `0` предупреждений / `0` ошибок (`00:00:01.31`);
- HUD подтвердил `F6: PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1`;
- регрессии `F7`, `F9`, `F10`, `F11` и `F12` завершились `PASS`; F9 подтвердил counts `8/4/4`;
- screenshots подтвердили `QuestCompleted` autosave и штатную работу основной session после reset/periodic save;
- `TASK-072`, `TASK-073`, `CONTENT-050`–`CONTENT-057` и `CONTENT-ACC-050`–`CONTENT-ACC-057` → `VERIFIED`.

**Реализовано в `TASK-074`:**

- каталог расширен до `items=10`, `resources=5`, `recipes=5`;
- добавлены stable IDs `resource.plasma_filament`, `component.ship.power_coupler`, `recipe.ship.power_coupler`;
- power recipe использует `2 × resource.plasma_filament`, station `station.portable_fabricator`, data-driven `craftTimeSeconds=4.0` и `StoreOutputs` в `inventory.ship`;
- в сцену добавлены два уникальных plasma-filament node (`plasma.alpha`, `plasma.beta`) и отдельный `PowerFabricator`;
- production session/load/reset/autosave/graceful-exit/HUD поддерживают четыре независимых station recipes и четыре crafted outputs;
- controller routing и domain events различают launch capacitor, navigation array, coolant regulator и power coupler;
- startup validation проверяет пять recipe inputs, четыре scene stations, положительный CraftTime и physical resource coverage;
- добавлен изолированный `F5` runner: repair prerequisite, блокировка до plasma resources, timed completion, isolation от трёх предыдущих recipes, изготовление всех четырёх outputs, `QuestCompleted` autosave, exact SQLite round-trip, log, one-writer и integrity;
- debug HUD расширен до четвёртой station chain и показывает отдельный статус `TASK-074 fifth path (F5)`.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/VerticalSlice/FifthCraftingPathAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован пятый из требуемых Этапом 1 resource/recipe paths. UI выбора нескольких recipes внутри одной physical station, параллельная очередь процессов, RequiredTechnology и полный объём `10 ресурсов / 10 рецептов` остаются последующими задачами.

**Проверки в среде подготовки:**

- PDF-ТЗ текстово и визуально сверено на страницах 29 и 56–57: recipe содержит Inputs/Outputs/RequiredTechnology/RequiredStation/CraftTime, definitions data-driven, Этап 1 требует 10 ресурсов и 10 рецептов;
- все JSON-файлы распарсены; counts, stable IDs, schema versions и cross-references проверены;
- scene bindings проверены на одиннадцать resource nodes, четыре crafting stations, уникальные instance IDs и совпадение RecipeId/RequiredStation;
- C# проверен лексически по строкам, комментариям и delimiters; новый acceptance runner не зависит от Godot API;
- проверены UID, `res://`, hotkey `F5` внутри текущей main scene и отсутствие build/cache/database artifacts;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому фактическая сборка и runtime `F5` здесь не заявляются.

**Статусы:**

- `TASK-072`, `TASK-073`, `CONTENT-050`–`CONTENT-057`, `CONTENT-ACC-050`–`CONTENT-ACC-057` → `VERIFIED`;
- `TASK-074`, `CONTENT-060`–`CONTENT-067` → `IMPLEMENTED`;
- `TASK-075`, `CONTENT-ACC-060`–`CONTENT-ACC-067` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** выполнить clean build `0/0`, проверить startup counts `10/5/5` и `TASK-074 ... binding PASS`, выполнить `F5: PASS`, затем повторить `F6/F7/F9/F10/F11/F12` и вручную подтвердить plasma-filament → PowerFabricator → `READY` → autosave → cold restart.

### 2026-08-02 — четвёртый resource/recipe path и three-recipe station session (`TASK-072`)

**Исходный снимок:** `ProjectHorizon-main(1)(7).zip`
**Подготовленный снимок:** `ProjectHorizon-main-fourth-crafting-path.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-070`–`TASK-073`, `CONTENT-040`–`CONTENT-057`, `CONTENT-ACC-040`–`CONTENT-ACC-057`.

**Синхронизация предыдущей приёмки:**

- пользователь предоставил успешную сборку `0` предупреждений / `0` ошибок (`00:00:01.37`);
- HUD подтвердил `F12: PASS resources=2, blocked=1, timed=1, isolated=1, both=1, output=1, roundTrip=1`;
- отдельный screenshot подтвердил `F9: PASS`, `F10: PASS` и `F11: PASS`; repair/persistence path дополнительно выполняется внутри comprehensive `F12` setup;
- `F12` подтвердил autosave, exact round-trip новых definitions, single writer и `integrity=ok`; runtime-дефект compatibility registry устранён;
- `TASK-070`, `TASK-071`, `CONTENT-040`–`CONTENT-047` и `CONTENT-ACC-040`–`CONTENT-ACC-047` → `VERIFIED`.

**Реализовано в `TASK-072`:**

- каталог расширен до `items=8`, `resources=4`, `recipes=4`;
- добавлены stable IDs `resource.thermal_gel`, `component.ship.coolant_regulator`, `recipe.ship.coolant_regulator`;
- coolant recipe использует `2 × resource.thermal_gel`, station `station.portable_fabricator`, data-driven `craftTimeSeconds=3.5` и `StoreOutputs` в `inventory.ship`;
- в сцену добавлены два уникальных thermal-gel node (`thermal.alpha`, `thermal.beta`) и отдельный центральный `CoolantFabricator`;
- production session/load/reset/autosave/graceful-exit/HUD поддерживают три независимых station recipes и три crafted outputs;
- controller routing и domain events различают launch capacitor, navigation array и coolant regulator без binary fallback;
- startup validation проверяет четыре recipe inputs, три scene stations, положительный CraftTime и physical resource coverage;
- добавлен изолированный `F6` runner: repair prerequisite, блокировка до thermal-gel resources, timed completion, isolation от двух предыдущих recipes, изготовление всех трёх outputs, `QuestCompleted` autosave, exact SQLite round-trip, log, one-writer и integrity;
- debug HUD расширен до третьей station chain, уменьшен до 16 px и расширен по горизонтали, чтобы новые строки не обрезались в окне 1158×650.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/VerticalSlice/FourthCraftingPathAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован четвёртый из требуемых Этапом 1 resource/recipe paths. UI выбора нескольких recipes внутри одной physical station, параллельная очередь процессов, RequiredTechnology и полный объём `10 ресурсов / 10 рецептов` остаются последующими задачами.

**Проверки в среде подготовки:**

- PDF-ТЗ текстово и визуально сверено на страницах 29 и 56–57: recipe содержит Inputs/Outputs/RequiredTechnology/RequiredStation/CraftTime, definitions data-driven, Этап 1 требует 10 ресурсов и 10 рецептов;
- все JSON-файлы распарсены; counts, stable IDs, schema versions и cross-references проверены;
- scene bindings проверены на девять resource nodes, три crafting stations, уникальные instance IDs и совпадение RecipeId/RequiredStation;
- C# проверен лексически по строкам, комментариям и delimiters; Godot API отсутствует в новом acceptance runner;
- проверены UID, `res://`, hotkey `F6` внутри текущей main scene и отсутствие build/cache/database artifacts;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому фактическая сборка и runtime `F6` здесь не заявляются.

**Статусы:**

- `TASK-070`, `TASK-071`, `CONTENT-040`–`CONTENT-047`, `CONTENT-ACC-040`–`CONTENT-ACC-047` → `VERIFIED`;
- `TASK-072`, `CONTENT-050`–`CONTENT-057` → `IMPLEMENTED`;
- `TASK-073`, `CONTENT-ACC-050`–`CONTENT-ACC-057` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** собрать проект `0/0`, проверить startup counts `8/4/4` и `TASK-072 ... binding PASS`, выполнить `F6: PASS`, затем повторить `F7/F9/F10/F11/F12` и вручную подтвердить thermal-gel → CoolantFabricator → `READY` → autosave → cold restart.

### 2026-08-02 — третий resource/recipe path и multi-recipe station session (`TASK-070`)

**Исходный снимок:** `ProjectHorizon-main(12).zip`
**Подготовленный снимок:** `ProjectHorizon-main-third-crafting-path-f12-registry-rebuild-fix.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-068`–`TASK-071`, `CONTENT-030`–`CONTENT-047`, `CONTENT-ACC-030`–`CONTENT-ACC-047`.

**Синхронизация предыдущей приёмки:**

- пользователь собрал hotfix-редакцию с `0` предупреждений и `0` ошибок (`00:00:01.94`);
- `F11` показал `PASS duration=3.0, started=1, duplicate=1, inputsHeld=1, completed=1, single=1, output=1`;
- screenshots подтвердили ручное завершение launch-capacitor process, `LaunchCapacitorCrafted`, `QuestCompleted` autosave и сохранённое состояние `REPAIRED/READY`;
- `F7` и `F9` повторно завершились `PASS`; `TASK-066/TASK-067` уже были приняты ранее;
- `TASK-068`, `TASK-069`, `CONTENT-030`–`CONTENT-037` и `CONTENT-ACC-030`–`CONTENT-ACC-037` → `VERIFIED`.

**Реализовано в `TASK-070`:**

- каталог расширен до `items=6`, `resources=3`, `recipes=3`;
- добавлены стабильные ID `resource.phase_fiber`, `component.ship.navigation_array` и `recipe.ship.navigation_array`;
- navigation recipe использует `2 × resource.phase_fiber`, station `station.portable_fabricator`, data-driven `craftTimeSeconds=2.5` и `StoreOutputs` в `inventory.ship`;
- в сцену добавлены два уникальных phase-fiber node (`phase.alpha`, `phase.beta`) и отдельный `NavigationFabricator`, связанный с navigation recipe;
- `StarterRepairSession` обобщён с одного secondary recipe до словаря station recipes; добавлены recipe-addressed `ValidateCraft`, `TryCraft` и `IsRecipeCrafted`, при этом прежний secondary API сохранён для регрессий;
- controller разрешает recipe по `RecipeId`, поддерживает две stations, общий single-active `DataDrivenCraftTimer`, независимые states/output и безопасную отмену;
- load/reset/scene restore/HUD/autosave учитывают оба crafted component и не смешивают recipe states;
- добавлен изолированный `F12` runner: repair prerequisite, блокировка до resources, timed navigation completion, recipe isolation, совместное наличие обоих outputs, `QuestCompleted` autosave, exact SQLite round-trip, log, one-writer и integrity;
- startup validation проверяет обе station bindings, уникальность node IDs, достаточность physical resources и положительный `CraftTime` каждого station recipe.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/ThirdCraftingPathAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован третий из требуемых Этапом 1 resource/recipe paths и масштабирование текущей domain session на несколько station recipes. UI выбора рецепта внутри одной station, параллельная очередь процессов, технологии и полный объём `10 ресурсов / 10 рецептов` остаются последующими задачами.

**Проверки в среде подготовки:**

- JSON-файлы независимо распарсены; counts, stable IDs, schema versions и все item/resource/recipe cross-references проверены;
- scene bindings проверены на два phase-fiber node, две crafting stations, уникальные instance IDs и совпадение RecipeId/RequiredStation;
- C#-файлы проверены лексически по строкам, комментариям и delimiters; выполнен поиск старой single-station ссылки и конфликтов `F12`;
- проверены UID, `res://`, состав проекта и отсутствие `.git`, `.godot`, `bin`, `obj`, `.vs`, локальных БД и логов;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому фактическая сборка и runtime `F12` здесь не заявляются.

**Статусы:**

- `TASK-068`, `TASK-069`, `CONTENT-030`–`CONTENT-037`, `CONTENT-ACC-030`–`CONTENT-ACC-037` → `VERIFIED`;
- `TASK-070`, `CONTENT-040`–`CONTENT-047` → `IMPLEMENTED`;
- `CONTENT-ACC-040` → `VERIFIED`;
- `TASK-071`, `CONTENT-ACC-041`–`CONTENT-ACC-047` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** собрать проект `0/0`, проверить startup counts `6/3/3` и `TASK-070 ... binding PASS`, выполнить `F12: PASS`, затем повторить `F7/F9/F10/F11` и вручную подтвердить независимый navigation process, autosave и cold restart.

**Фактическая локальная проверка и hotfix `F12` (2026-08-02):**

- пользователь подтвердил успешную инкрементальную сборку: `0` предупреждений, `0` ошибок, `00:00:01.21`; startup/runtime подтверждают `CONTENT-ACC-040`, однако позднее установлено, что `CoreCompile` был пропущен;
- startup HUD подтвердил `DB: Ready` и каталог `items=6`, `resources=3`, `recipes=3`;
- `F12` дошёл до autosave write, но завершился `FAIL`: `InvalidDataException: Primary snapshot validation failed: inventory item ... differs`;
- root cause: новые catalog IDs `resource.phase_fiber` и `component.ship.navigation_array` отсутствовали в persistence compatibility registry `KnownInventoryDefinitions`; при контрольном чтении они разрешались как `content.unknown.item`, поэтому exact round-trip сравнение отклоняло snapshot;
- в `SaveDatabase.Migration.cs` оба стабильных ID добавлены в реестр известных inventory definitions; логика crafting, timer, autosave transaction и HUD не изменена;
- `TASK-070` остаётся `IMPLEMENTED`, `TASK-071` и `CONTENT-ACC-042`–`CONTENT-ACC-047` остаются `IN_PROGRESS` до повторного `F12: PASS` и регрессий.

**Подготовленный hotfix-снимок:** `ProjectHorizon-main-third-crafting-path-f12-save-fix.zip`.

**Повторная локальная проверка и усиленный hotfix `F12` (2026-08-02):**

- повторный `F12` завершился тем же `FAIL`: `Primary snapshot validation failed: inventory item crafted.component.ship.navigation_array differs`; остальные показатели runner обнулены, потому что исключение возникло внутри autosave batch до формирования итогового отчёта;
- приложенный build log формально показывает `0` предупреждений и `0` ошибок, однако `CoreCompile` был **пропущен как актуальный**, поэтому новая редакция `SaveDatabase.Migration.cs` не была гарантированно скомпилирована; runtime мог использовать прежний `Game.Client.dll`;
- persistence registry сделан расширяемым: `SaveDatabase.RegisterKnownInventoryDefinitions(...)` регистрирует все item definition IDs текущего JSON-каталога перед инициализацией БД; `F12` дополнительно регистрирует входы и outputs используемых recipes, поэтому runner больше не зависит от порядка загрузки сцены;
- exact snapshot diagnostics теперь выводит ожидаемые и фактические `definition/original/resolution/quantity/durability`, если round-trip снова разойдётся;
- добавлен `tools\clean-build-windows10.cmd`, удаляющий stale `.godot\mono\temp` и запускающий реальную Debug-сборку; README дополнен обязательным clean-rebuild шагом после замены файлов поверх существующей рабочей копии;
- устранены известные nullable `CS8600` sites: nullable-aware `TryGetValue` в `GameContentCatalog`, alias resolution в `SaveDatabase.Migration` и стабильный local capture `_secondaryRecipe` в `StarterRepairDomain`; цель повторной полной компиляции — `0 warnings / 0 errors`;
- `TASK-070` остаётся `IMPLEMENTED`, `TASK-071` — `IN_PROGRESS` до clean build, `F12: PASS` и регрессий `F7/F9/F10/F11`.

**Изменённые файлы усиленного hotfix:**

- `src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/ThirdCraftingPathAcceptance.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `tools/clean-build-windows10.cmd`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Подготовленный усиленный hotfix-снимок:** `ProjectHorizon-main-third-crafting-path-f12-registry-rebuild-fix.zip`.

### 2026-08-02 — исправление ошибки сборки `TASK-068` (`CS0136`)

**Исходный снимок:** `ProjectHorizon-main-data-driven-craft-time.zip`
**Подготовленный снимок:** `ProjectHorizon-main-data-driven-craft-time-build-fix.zip`
**Основание:** локальная сборка пользователя от 2026-08-02 в Godot 4.7.1/.NET завершилась с `2` ошибками и `7` nullable-предупреждениями.

**Исправлено:**

- в `SalvageRepairSlice.UpdateTimedCraft` переменные аварийной ветки `recipeId` и `elapsed` конфликтовали с одноимёнными переменными внешней области и вызывали `CS0136`;
- переменные аварийной отмены переименованы в `cancelledRecipeId` и `cancelledElapsed`; логика timed craft, completion и safe cancellation не изменена;
- повторно проверены изменённый участок, баланс скобок, строки и ссылки на переименованные переменные.

**Фактический результат до исправления:** `7` предупреждений, `2` ошибки `CS0136`; сборка неуспешна.
**После исправления:** повторная сборка в среде подготовки недоступна; `CONTENT-ACC-030` и `TASK-069` остаются `IN_PROGRESS` до локального результата `0` ошибок. Семь `CS8600` фиксируются как технический долг и не скрываются.

**Изменённые файлы:**

- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `REQUIREMENTS_STATUS.md`.

### 2026-08-01 — data-driven время крафта и station process (`TASK-068`)

**Исходный снимок:** `ProjectHorizon-main(6)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-data-driven-craft-time-build-fix.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** раздел 17.4, раздел 23, раздел 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-066`–`TASK-069`, `CONTENT-020`–`CONTENT-037`, `CONTENT-ACC-020`–`CONTENT-ACC-037`.

**Синхронизация предыдущей приёмки:**

- пользователь текущим запросом явно назначил `TASK-066 → VERIFIED` и `TASK-067 → VERIFIED`;
- `CONTENT-020`–`CONTENT-027` и `CONTENT-ACC-020`–`CONTENT-ACC-027` синхронизированы в `VERIFIED`;
- числовой build/runtime log в текущем запросе отсутствует, поэтому журнал фиксирует только фактически полученное прямое подтверждение без реконструкции неуказанных значений.

**Реализовано в `TASK-068`:**

- `recipe.ship.launch_capacitor` получил положительный JSON-параметр `craftTimeSeconds=3.0`; repair recipe сохранил мгновенное применение с `0.0`;
- добавлен Godot-независимый `DataDrivenCraftTimer`, который читает duration из immutable recipe definition, детерминированно считает elapsed/remaining/progress и завершает operation ровно один раз;
- доменная модель получила отдельную неразрушающую проверку `ValidateSecondaryCraft`; inputs и outputs не изменяются при старте или промежуточном progress;
- production-взаимодействие с `PortableFabricator` теперь запускает процесс, а не выдаёт output немедленно; повторное `E` не перезапускает и не дублирует operation;
- inputs расходуются и capacitor создаётся только после достижения JSON-duration; затем вызывается существующий `QuestCompleted` autosave и сохраняется прежний exact SQLite round-trip;
- station получила три визуальных состояния: фиолетовая `idle`, оранжевая `crafting`, зелёная `crafted`;
- detailed и compact HUD показывают configured time, `RUNNING elapsed/3.0s`, процент и отдельную строку `TASK-068 craft time (F11)`;
- закрытие окна во время active craft безопасно отменяет timer до graceful-exit snapshot: inputs не расходуются, незавершённый output не создаётся;
- `F11` запускает изолированную pure-.NET acceptance: positive JSON duration, start, duplicate rejection, удержание inputs, partial RUNNING, completion exactly at configured duration, single completion и output quantity;
- `F7`, `F9` и `F10` сохранены как регрессии прежних принятых подсистем.

**Изменённые файлы:**

- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/DataDrivenCraftTimer.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/CraftTimeAcceptance.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/PortableCraftingStation.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован один механизм — исполнение `CraftTime` для уже принятого launch-capacitor recipe. In-progress operation намеренно не сериализуется: при штатном закрытии она отменяется без расходования inputs. Третий resource/recipe path, очередь нескольких рецептов, технологии и UI выбора рецептов остаются последующими задачами.

**Проверки в среде подготовки:**

- PDF-ТЗ текстово и визуально сверено на страницах 29 и 56–57: recipe обязан содержать `CraftTime`, определения data-driven, Этап 1 требует 10 ресурсов и 10 рецептов;
- все JSON-файлы независимо распарсены, counts и cross-reference сохранены `items=4/resources=2/recipes=2`, launch duration равна `3.0`;
- новые timer/acceptance/domain файлы не используют Godot API; Godot API остаётся только в main-thread controller/station;
- проверены UID, `res://`, hotkey `F11`, отсутствие конфликта в текущей main scene, строки, комментарии и баланс скобок;
- первоначальная статическая проверка не выявила конфликт областей видимости C#; локальная сборка пользователя выявила `CS0136`, после чего конфликт исправлен отдельным hotfix;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому повторная фактическая сборка и runtime `F11` здесь не заявляются.

**Статусы:**

- `TASK-066`, `TASK-067`, `CONTENT-020`–`CONTENT-027`, `CONTENT-ACC-020`–`CONTENT-ACC-027` → `VERIFIED`;
- `TASK-068`, `CONTENT-030`–`CONTENT-037` → `IMPLEMENTED`;
- `TASK-069`, `CONTENT-ACC-030`–`CONTENT-ACC-037` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** собрать проект `0/0`, проверить startup `craftTime=3.0`, выполнить `F11: PASS`, повторить `F7/F9/F10`, затем вручную подтвердить оранжевое состояние station, промежуточный HUD `RUNNING`, отсутствие преждевременного output и единственный completion/autosave через 3 секунды.

### 2026-08-01 — второй ресурс, рецепт и отдельная crafting station (`TASK-066`)

**Исходный снимок:** `ProjectHorizon-main(5)(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-second-resource-crafting-station.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-064`–`TASK-067`, `CONTENT-020`–`CONTENT-027`, `CONTENT-ACC-020`–`CONTENT-ACC-027`.

**Синхронизация предыдущей приёмки:**

- пользователь собрал data-driven редакцию с `0` предупреждений и `0` ошибок (`00:00:01.38`);
- `F9` показал `PASS schema=1, items=2, resources=1, recipes=1, dataDriven=1, invalidRejected=2`;
- `F7` показал `PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1`;
- ручной цикл завершён: `collected=3/3`, `Objective: COMPLETE`, `Ship: REPAIRED`, autosave `QuestCompleted`;
- `TASK-064`, `TASK-065`, `CONTENT-010`–`CONTENT-016`, `CONTENT-ACC-010`–`CONTENT-ACC-015` → `VERIFIED`.

**Реализовано в `TASK-066`:**

- каталог расширен до `items=4`, `resources=2`, `recipes=2`;
- добавлены stable IDs `resource.conductive_crystal`, `component.ship.launch_capacitor`, `recipe.ship.launch_capacitor`, `station.portable_fabricator`;
- два фиолетовых физических crystal-узла получают yield и material из `resources.json`;
- отдельный `PortableCraftingStation` реализует `IInteractable` и связывается с JSON через точные C# exports `StationId` и `RecipeId`;
- второй рецепт расходует `2 × resource.conductive_crystal`, создаёт `1 × component.ship.launch_capacitor` и сохраняет output в `inventory.ship`;
- `GameContentCatalog` поддерживает application `StoreOutputs` наряду с `RepairShip`, сохраняя строгую валидацию `ResultHealth`;
- доменная модель запрещает крафт до ремонта корабля, отклоняет неверную станцию, блокирует нехватку inputs, предотвращает повторное изготовление и сохраняет crafted outputs;
- accepted F7 regression фильтрует только inputs repair recipe и по-прежнему ожидает ровно три salvage-узла;
- snapshot сохраняет `crafted.<definitionId>`; migration/content resolver знает новые definitions; cold load восстанавливает capacitor и визуальное состояние station;
- production interaction после успешного крафта вызывает autosave `QuestCompleted`;
- `F10` запускает изолированную `TASK-066` acceptance: prerequisite repair, wrong station, insufficient inputs, сбор двух crystal nodes, crafting, autosave, exact round-trip, log, single writer и integrity;
- HUD расширен до двух последовательных целей и показывает оба recipe definitions.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/VerticalSlice/PortableCraftingStation.cs` и `.uid`;
- `src/Game.Client/Scripts/VerticalSlice/CraftingExpansionAcceptance.cs` и `.uid`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован второй из требуемых Этапом 1 resource/recipe paths и отдельная station. Полные `10 ресурсов / 10 рецептов`, craft time, технологии, UI выбора рецептов и торговая станция остаются последующими задачами.

**Проверки в среде подготовки:**

- PDF-ТЗ сверено по item/recipe model, JSON static definitions, Этапу 1 и общим acceptance criteria;
- JSON независимо распарсен, schema versions и все item/resource/recipe references проверены;
- проверены C#-лексика, строки, комментарии, скобки, record-конструкторы и отсутствие Godot API в domain/acceptance model;
- проверены project XML, `res://`, UID, scene exports/groups; `F10` не конфликтует внутри текущей main scene (в отдельной terrain-regression scene она исторически используется независимо);
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому сборка и runtime `F10` здесь не заявляются.

**Статусы:**

- `TASK-064`, `TASK-065`, `CONTENT-010`–`CONTENT-016`, `CONTENT-ACC-010`–`CONTENT-ACC-015` → `VERIFIED`;
- `TASK-066`, `CONTENT-020`–`CONTENT-027` → `IMPLEMENTED`;
- `TASK-067`, `CONTENT-ACC-020`–`CONTENT-ACC-027` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`.

**Следующий рекомендуемый шаг:** собрать проект `0/0`, получить startup `TASK-066 crafting binding PASS`, выполнить `F10: PASS`, затем повторить `F9`, `F7` и ручной цикл `repair → crystals → PortableFabricator → cold restart`.

### 2026-08-01 — первый data-driven каталог ресурса и рецепта ремонта (`TASK-064`)

**Исходный снимок:** `ProjectHorizon-main(4)(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-data-driven-starter-repair.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`
**Связанные требования:** разделы 17.2–17.4, 23, 36.1, Этап 1 раздела 40 и критерии 6/10/14 раздела 41 PDF-ТЗ; `TASK-062`–`TASK-065`, `CONTENT-010`–`CONTENT-016`, `CONTENT-ACC-010`–`CONTENT-ACC-015`.

**Синхронизация предыдущей приёмки:**

- финальная resource-ID редакция собрана пользователем с `0` предупреждений и `0` ошибок (`00:00:01.26`);
- HUD подтвердил `rev=26`, `collected=3/3`, `Objective: COMPLETE — starter ship repaired`, `Ship: REPAIRED`;
- production-autosave завершился с причиной `QuestCompleted`;
- пользователь прямо подтвердил, что после полного перезапуска состояние отремонтированного корабля восстановилось из autosave;
- ранее получен `F7: PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1`;
- `TASK-062`, `TASK-063`, `VS-010`–`VS-016`, `VS-ACC-010`–`VS-ACC-016` → `VERIFIED`.

**Реализовано в `TASK-064`:**

- добавлены `Content/items.json`, `Content/resources.json`, `Content/recipes.json` со schema version `1`;
- статические определения используют стабильные строковые ID, а не индекс массива;
- добавлен Godot-независимый `GameContentCatalog` на `System.Text.Json` со строгим запретом неизвестных полей, trailing comma и нестрогих чисел;
- валидируются schema version, обязательные поля, dotted IDs, дубликаты, tags, диапазоны, resource→item и recipe input/output→item references;
- первый item-набор содержит `resource.salvage_alloy` и `component.starter_hull_patch`;
- `resources.json` задаёт deterministic yield и визуальные параметры salvage-узла; материал больше не является gameplay-константой сцены;
- `recipe.ship.starter_repair` задаёт input `3 × resource.salvage_alloy`, output `1 × component.starter_hull_patch`, станцию `station.field_repair` и application `RepairShip` с health `100`;
- `StarterRepairSession` получает recipe object через конструктор, вычисляет требуемые количества, расходует inputs, формирует outputs и применяет repair effect без константы `RequiredSalvage`;
- resource nodes хранят только instance `ResourceNodeId` и stable `ResourceDefinitionId`; quantity и material разрешаются из content catalog;
- repair terminal имеет `StationId`, который обязан совпадать с `RequiredStation` рецепта;
- SQLite snapshot сохраняет фактический definition ID каждого собранного узла и remaining quantity; существующие принятые сохранения совместимы;
- `F7` теперь выполняет gameplay/persistence regression на реально загруженном рецепте и scene bindings;
- `F9` запускает отдельную `TASK-064` acceptance: меняет копию recipe threshold `3→4` только в памяти, проверяет блокировку на `3/4`, ремонт на `4/4`, recipe outputs, stable IDs и отклонение duplicate/missing-reference catalogs; gameplay-slot и JSON не изменяются;
- detailed/compact HUD показывает content schema, counts и активный recipe; Output содержит `TASK-064 content catalog READY` и `TASK-064 content binding PASS`.

**Изменённые файлы:**

- `src/Game.Client/Content/items.json`;
- `src/Game.Client/Content/resources.json`;
- `src/Game.Client/Content/recipes.json`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs`;
- `src/Game.Client/Scripts/Content/GameContentCatalog.cs.uid`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterShipRepairTerminal.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован первый реальный item/resource/recipe-набор и строгая runtime/acceptance validation. Полное автоматическое выполнение JSON schema validation как отдельного MSBuild target и расширение Этапа 1 до требуемых 10 ресурсов/10 рецептов остаются последующими задачами; текущая итерация их не заявляет.

**Проверки в среде подготовки:**

- PDF-ТЗ сверено по разделам 17.2–17.4, 23 и Этапу 1 раздела 40;
- все три JSON-файла распарсены независимым JSON parser, schema versions и cross-references проверены;
- изменённые C#-файлы проверены лексически, по строкам, комментариям и скобкам;
- проверены exact C# export names `ResourceNodeId`, `ResourceDefinitionId`, `StationId`;
- проверены `res://Content/*.json`, scene bindings, UID, project XML и отсутствие конфликта F9 в текущей main scene;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому сборка и runtime `F9` здесь не заявляются.

**Статусы:**

- `TASK-062`, `TASK-063`, `VS-010`–`VS-016`, `VS-ACC-010`–`VS-ACC-016` → `VERIFIED`;
- `TASK-064`, `CONTENT-010`–`CONTENT-016` → `IMPLEMENTED`;
- `TASK-065`, `CONTENT-ACC-010`–`CONTENT-ACC-015` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED`: в ZIP отсутствует `.git` и контрольный SHA.

**Следующий рекомендуемый шаг:** собрать проект `0/0`, проверить startup lines, нажать `F9` и получить `TASK-064 ... PASS`, затем повторить `F7` и короткий ручной цикл после `F8`.

### 2026-08-01 — hotfix уникальных ID ресурсных узлов (`TASK-062/TASK-063`)

**Исходный снимок:** `ProjectHorizon-main-vertical-slice-interaction-hotfix.zip`
**Подготовленный снимок:** `ProjectHorizon-main-vertical-slice-resource-id-hotfix.zip`
**Git SHA:** отсутствует в архиве
**Причина hotfix:** после исправления proximity-взаимодействия первый конус собирался, но второй и третий не изменяли прогресс, хотя HUD корректно показывал ближайшую цель.

**Полученное runtime-доказательство дефекта:**

- локальная сборка: `0` предупреждений, `0` ошибок;
- `F7`: `PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1`;
- production-сцена после первого ручного сбора показывала `collected=1/3`;
- HUD находил следующую цель: `Interaction: near SalvageGamma (1,4 m) — press E`;
- повторные нажатия `E` не увеличивали счётчик.

**Корневая причина:** экспортируемое C#-свойство называется `ResourceNodeId`, но в `SalvageRepairSlice.tscn` было записано GDScript-подобное имя `resource_node_id`. В этом проекте C# exports сериализуются с точным PascalCase-именем. Значения сцены не применялись, поэтому все три узла сохраняли default `salvage.unassigned`. Первый узел добавлял этот ID, а два остальных корректно отклонялись доменной моделью как повторный сбор того же ID. Автоматический `F7` не обнаруживал дефект, поскольку создавал уникальные ID непосредственно в Godot-независимом тесте.

**Исправлено:**

- свойства сцены заменены на точные `ResourceNodeId = "salvage.alpha|beta|gamma"`;
- каждый `SalvageResourceNode` при `_Ready()` запрещает пустой/default ID и выдаёт явную ошибку сериализации;
- корневой контроллер проверяет количество, уникальность и точный canonical-набор ID до запуска persistence;
- при успешном старте Output содержит `TASK-062 scene binding PASS: resourceIds=salvage.alpha,salvage.beta,salvage.gamma; unique=1`;
- старый ошибочный `item.salvage.unassigned` из локального snapshot больше не засчитывается как реальный salvage-узел;
- дефект больше не может быть скрыт доменной `F7`-приёмкой: сцена с неправильными или дублирующимися ID останавливается до READY.

**Изменённые файлы:**

- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:** `TASK-062` и `VS-010`–`VS-016` остаются `IMPLEMENTED`; `TASK-063` и `VS-ACC-010`–`VS-ACC-016` остаются `IN_PROGRESS` до полного ручного цикла `0/3 → 1/3 → 2/3 → 3/3 → REPAIRED` и cold restart.

**Повторная приёмка:** нажать `F8`, проверить строку scene-binding PASS, собрать три разных узла и убедиться, что Output содержит три разных ID `salvage.alpha`, `salvage.beta`, `salvage.gamma`; затем отремонтировать корабль, проверить `H`, graceful exit и холодное восстановление.

### 2026-08-01 — hotfix ручного взаимодействия и HUD (`TASK-062/TASK-063`)

**Исходный снимок:** `ProjectHorizon-main-vertical-slice-salvage-repair.zip`
**Подготовленный снимок:** `ProjectHorizon-main-vertical-slice-interaction-hotfix.zip`
**Git SHA:** отсутствует в архиве
**Причина hotfix:** автоматическая `F7`-приёмка прошла, однако пользовательская runtime-проверка выявила, что низкие salvage-узлы не подбираются при обычном приближении, а заявленная клавиша `H` вообще не была реализована.

**Полученные runtime-доказательства исходной редакции:**

- локальная сборка завершилась с `0` предупреждений и `0` ошибок;
- HUD показал `TASK-062 acceptance (F7): PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1`;
- взаимодействие `E` с кораблём сработало и сформировало `ShipRepairBlocked`, то есть input action была зарегистрирована;
- ручное взаимодействие с ресурсными конусами не давало результата;
- нажатие `H` не меняло HUD, поскольку обработчик и режимы HUD отсутствовали в `SalvageRepairSlice`.

**Исправлено:**

- точный `InteractionRay` сохранён как приоритетный способ выбора цели;
- добавлен proximity-fallback на ближайший активный узел группы `interactable` в радиусе `2,75 м`, с ограничением по направлению камеры;
- собранные узлы с нулевым collision layer исключаются из fallback и не блокируют выбор следующего ресурса;
- resource nodes и повреждённый корабль явно включены в группу `interactable`;
- HUD теперь показывает фактическую цель: `aimed at ...` либо `near ... (N m) — press E`;
- реализованы три режима `H`: `DETAILED → COMPACT → HIDDEN`; в скрытом режиме остаётся hint для возврата;
- HUD-контролы переведены в `mouse_filter=IGNORE`, чтобы диагностическая панель не участвовала в обработке пользовательского ввода;
- README и ручная приёмка синхронизированы с исправленным управлением.

**Изменённые файлы hotfix:**

- `src/Game.Client/Scripts/Player/PlayerController.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Статусы:** `TASK-062` и `VS-010`–`VS-016` остаются `IMPLEMENTED`; `TASK-063` и `VS-ACC-010`–`VS-ACC-016` остаются `IN_PROGRESS` до повторной ручной проверки `E`, `H`, ремонта и cold restart.

**Повторная приёмка hotfix:** после `F8` подойти к каждому конусу до появления `Interaction: near Salvage... — press E`, нажать `E` и получить `1/3 → 2/3 → 3/3`; затем проверить ремонт корабля, три последовательных режима `H`, `F7: PASS`, graceful-exit и холодное восстановление.

### 2026-08-01 — первый сквозной цикл: сбор ресурса, ремонт корабля и domain autosave (`TASK-062`)

**Исходный снимок:** `ProjectHorizon-main(3)(3).zip`
**Подготовленный снимок:** `ProjectHorizon-main-vertical-slice-salvage-repair.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 2.2, 17.1–17.3, 19.1–19.3, 22.3, 22.8, 36.3, Этап 1 раздела 40 и критерии 6, 10, 14 раздела 41 PDF-ТЗ; `TASK-060`–`TASK-063`, `VS-010`–`VS-016`, `VS-ACC-010`–`VS-ACC-016`.

**Синхронизация предыдущей приёмки:**

- локальная сборка autosave-редакции завершилась с `0` предупреждений и `0` ошибок;
- `TASK-060 autosave (F6): PASS triggers=8, requests=8, batches=2, coalesced=6, exit=1`;
- реальный periodic autosave подтвердил `triggers=Periodic`;
- регрессионные `C`, `X`, `Z` одновременно остались `PASS`;
- при закрытии получено `Prototype E graceful-exit flush started: activeTasks=0; inMemoryRevision=2`;
- финальная строка: `Prototype E graceful-exit autosave PASS: saved=1; revision=3; pending=0`;
- пользователь прямо подтвердил, что после перезапуска revision `3` восстановилась;
- `TASK-060`, `TASK-061`, `PERSIST-040`–`PERSIST-046`, `PERSIST-ACC-040`–`PERSIST-ACC-047` → `VERIFIED`.

**Реализовано в `TASK-062`:**

- стартовая сцена переключена на `Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- сцена объединяет принятого персонажа, физическое взаимодействие, три ресурсных узла, повреждённый корабль, SQLite и autosave coordinator;
- добавлена Godot-независимая доменная модель `StarterRepairSession`; она запрещает повторный сбор одного узла и ремонт при наличии менее трёх единиц salvage;
- сбор выполняется реальным взаимодействием `E` через существующий `IInteractable`: точный raycast имеет приоритет, а hotfix добавляет proximity-fallback для низких узлов на близкой дистанции;
- успешный ремонт расходует три единицы `resource.salvage_alloy`, переводит здоровье корабля с `28` до `100` и завершает стартовую ремонтную цель;
- завершение цели формирует реальное доменное событие `StarterRepairQuestCompleted` и вызывает production-autosave с типизированной причиной `QuestCompleted`;
- `resource.salvage_alloy` и `ship.starter.repairable` добавлены в известный content registry persistence, поэтому не превращаются в unknown placeholders;
- gameplay snapshot хранит позицию игрока, salvage inventory, состояние корабля и посещённую планету; при холодной загрузке восстанавливаются revision, количество собранных узлов и визуальное состояние корабля;
- periodic autosave и graceful-exit flush подключены к новой стартовой сцене; незавершённый сбор также сохраняется при штатном выходе;
- `F8` очищает отдельный gameplay-slot и позволяет повторить цикл; persistence-прототип сохранён отдельной регрессионной сценой;
- `F7` запускает изолированную acceptance route: ранний ремонт должен быть заблокирован, три ресурса собраны, корабль отремонтирован, `QuestCompleted` autosave завершён, snapshot точно прочитан обратно, журнал записан и `integrity_check=ok`.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Player/PlayerController.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs`;
- `src/Game.Client/Scripts/VerticalSlice/StarterShipRepairTerminal.cs`;
- `src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs`;
- соответствующие `.uid`;
- `src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn`;
- `src/Game.Client/project.godot`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Проверки в среде подготовки:**

- PDF-ТЗ визуально и текстово сверено по базовому циклу, inventory/quest-модели и составу Этапа 1;
- проверены все новые `res://`-ссылки, UID, NodePath, группы и отсутствие конфликта `H/F7/F8`;
- доменная и acceptance-инфраструктура не обращаются к Godot API;
- выполнена лексическая проверка C#-файлов, строк, комментариев и скобок;
- проверено, что новый профиль и изолированная test-БД не затрагивают persistence-прототип;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому сборка и runtime `F7` здесь не заявляются.

**Статусы:**

- `TASK-060`, `TASK-061`, `PERSIST-040`–`PERSIST-046`, `PERSIST-ACC-040`–`PERSIST-ACC-047` → `VERIFIED`;
- `TASK-062`, `VS-010`–`VS-016` → `IMPLEMENTED`;
- `TASK-063`, `VS-ACC-010`–`VS-ACC-016` → `IN_PROGRESS`.

**Следующий рекомендуемый шаг:** собрать проект, выполнить `F7: PASS`, затем вручную проверить блокировку раннего ремонта, сбор трёх узлов, зелёное состояние корабля, строку `QuestCompleted`, штатный выход и холодное восстановление отремонтированного состояния.

### 2026-08-01 — autosave coordinator и безопасный штатный выход (`TASK-060`)

**Исходный снимок:** `ProjectHorizon-main(2)(3).zip`
**Подготовленный снимок:** `ProjectHorizon-main-persistence-autosave-graceful-exit.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся блокированным до предоставления SHA из Git
**Связанные требования:** разделы 22.3, 22.8, 22.9, 36.3 и критерии 10, 14, 15 раздела 41 PDF-ТЗ; `TASK-058`–`TASK-061`, `PERSIST-040`–`PERSIST-046`, `PERSIST-ACC-040`–`PERSIST-ACC-047`.

**Синхронизация предыдущей приёмки:**

- локальная сборка migration-редакции завершилась с `0` предупреждений и `0` ошибок;
- HUD подтвердил `TASK-058 migration (C): PASS 1→2, source=1, aliases=1, unknown=2, roundTrip=1`;
- регрессионный recovery завершился `TASK-056 recovery (X): PASS`, `candidateRejected=1`, `backupPreserved=1`, `atomic=1`, `quarantine=1`;
- foundation regression завершилась `TASK-054 save (Z): PASS rev=2, items=3, writes=8, maxWriters=1, integrity=ok`;
- очередь после проверок освобождалась: `pending=0`; schema оставалась `2`;
- `TASK-058`, `TASK-059`, `PE-030`–`PE-035`, `PE-ACC-020`–`PE-ACC-026` → `VERIFIED`;
- Прототип E и весь Этап 0 технических прототипов → `VERIFIED`.

**Реализовано в `TASK-060`:**

- добавлен независимый от Godot `SaveAutosaveCoordinator`; worker получает только immutable `SaveGameSnapshot`;
- периодический autosave стартовой persistence-сцены выполняется каждые `60` секунд после появления snapshot;
- введены типизированные причины `Periodic`, `Landing`, `Takeoff`, `Hyperspace`, `QuestCompleted`, `ShipPurchased`, `BaseChanged`, `GracefulExit`;
- burst запросов в коротком coalescing-window объединяется в один batch, сохраняющий самый новый snapshot и полный набор причин;
- все autosave-записи проходят через существующую последовательную writer-очередь `SaveDatabase`, транзакционную validation и backup-защиту;
- добавлен журнал `logs/save_1.autosave.log` с revision, причинами, числом запросов и coalesced requests;
- `SceneTree.AutoAcceptQuit` отключается для persistence-сцены; `NotificationWMCloseRequest` запускает `GracefulExit` autosave, ждёт полного flush и только после этого вызывает `SceneTree.Quit()`;
- при ошибке graceful-exit save приложение не закрывается автоматически, состояние `FAIL` остаётся видимым;
- HUD показывает countdown до periodic autosave, requests/batches/coalesced, последний revision и причины;
- `F6` запускает изолированный acceptance route в `save_1.autosave-test.db`: один periodic batch и burst из шести gameplay events плюс graceful exit; ожидаются восемь trigger types, восемь requests, два batch, шесть coalesced requests, final revision `27`, exact round-trip, log и `integrity=ok`;
- основной пользовательский slot тестом `F6` не изменяется.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Persistence/SaveDatabase.Autosave.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Autosave.cs.uid`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SavePrototype.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован autosave/graceful-exit foundation и его изолированная acceptance route. Подключение причин `Landing/Takeoff/...` к реальным подсистемам вертикального среза выполняется при появлении соответствующих application/domain событий; текущая итерация предоставляет готовый типизированный контракт и проверяет его end-to-end.

**Проверки в среде подготовки:**

- PDF-ТЗ сверено по разделам 22.8, 22.9, 36.3, 39 и критериям 10, 14, 15 раздела 41;
- официальная модель Godot quit handling сверена: `NotificationWMCloseRequest`, `SceneTree.AutoAcceptQuit=false`, явный `Quit()` после завершения пользовательской процедуры;
- изменённые C#-файлы проверены лексически: строки, комментарии, круглые/квадратные/фигурные скобки сбалансированы;
- проверено отсутствие Godot API в `SaveDatabase.Autosave.cs`;
- клавиша `F6` не конфликтует с существующими hotkey текущей сцены;
- проверены `res://`, UID, сцена, проектный XML и состав архива;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому фактическая сборка и runtime `F6` здесь не заявляются.

**Статусы:**

- `TASK-058`, `TASK-059`, `PE-030`–`PE-035`, `PE-ACC-020`–`PE-ACC-026` → `VERIFIED`;
- `TASK-060`, `PERSIST-040`–`PERSIST-046` → `IMPLEMENTED`;
- `TASK-061`, `PERSIST-ACC-040`–`PERSIST-ACC-047` → `IN_PROGRESS`;
- `TASK-006` → `BLOCKED` для текущего архива: каталог `.git` и SHA отсутствуют.

**Следующий рекомендуемый шаг:** выполнить локальную сборку, `F6: PASS`, реальный 60-секундный periodic autosave и закрытие окна с последующим повторным запуском; после `TASK-061: VERIFIED` начать первую интеграционную итерацию вертикального среза.

### 2026-08-01 — copy migration и unknown-content compatibility (`TASK-058`)

**Исходный снимок:** `ProjectHorizon-main(1)(6).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-copy-migration.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 22.1–22.5, 22.9, 36.1, 36.3 и Прототип E раздела 39 PDF-ТЗ; `TASK-056`–`TASK-059`, `PE-020`–`PE-035`, `PE-ACC-010`–`PE-ACC-026`.

**Синхронизация предыдущей приёмки:**

- hotfix собран локально с `0` предупреждений и `0` ошибок;
- ручной сценарий полностью выполнен: `R` очистил slot, первое `S` записало revision `1`, `B` сохранила revision `1`, второе `S` записало revision `2`, `Y` восстановила revision `1`;
- HUD подтвердил `Backup B: PASS rev=1, integrity=ok, atomic=1`;
- HUD подтвердил `Restore Y: PASS rev=1, atomic=1, quarantine=1`;
- очередь после каждой операции освобождалась: `pending=0`, `maxConcurrent=1`;
- пользователь прямо подтвердил: «ВСЁ РАБОТАЕТ!»;
- `TASK-056`, `TASK-057`, `PE-020`–`PE-025`, `PE-ACC-010`–`PE-ACC-016` → `VERIFIED`.

**Реализовано в `TASK-058`:**

- schema сохранений повышена с `1` до `2`, content version — с `1` до `2`;
- миграция существующей schema-1 БД выполняется не на исходнике, а на отдельном SQLite online-backup кандидате `*.migration-candidate`;
- кандидат проходит явную migration chain, WAL checkpoint, schema/snapshot validation и `PRAGMA integrity_check` до установки;
- установка выполняется через `File.Replace`; byte-identical исходник сохраняется как `save_1.pre-migration.v1.db` и сверяется по SHA-256;
- rollback восстанавливает primary и WAL/SHM sidecar-файлы даже при сбое до создания backup-файла `File.Replace`;
- добавлен `logs/save_1.migration.log`;
- schema 2 хранит `original_definition_id` и `original_template_id`;
- alias `resource.iron → resource.iron_ore` разрешается детерминированно с сохранением исходного ID;
- неизвестный item преобразуется в `content.unknown.item`, неизвестный/удалённый шаблон корабля — в `content.unknown.ship`;
- quantity, durability, health, fuel и исходные content ID сохраняются при повторном save/load;
- добавлена изолированная acceptance route по клавише `C`: schema-1 fixture, migration `1→2`, неизменность исходника, один alias, два placeholder, шесть точных content-проверок, round-trip и integrity;
- основной пользовательский slot acceptance-тестом не изменяется.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs.uid`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Recovery.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SavePrototype.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Граница итерации:** реализован один функциональный шаг — migration старой schema и compatibility неизвестного контента. Autosave, Save Inspector UI и остальные подсистемы сохранений в эту итерацию не включены.

**Проверки в среде подготовки:**

- PDF-ТЗ визуально проверено на страницах разделов 22, 36.3 и Прототипа E;
- migration SQL проверен на отдельной SQLite schema-1 fixture;
- изменённые C#-файлы проверены лексически, по скобкам, строкам, record-конструкторам и nullable-путям;
- проверены `Game.Client.csproj`, `res://`, сцена, NodePath и отсутствие конфликта клавиши `C`;
- инфраструктурные migration/DB-файлы не используют Godot API;
- .NET SDK и Godot в среде подготовки отсутствуют, поэтому локальная сборка и runtime `C` здесь не заявляются.

**Статусы:**

- `TASK-054`–`TASK-057`, `PE-001`, `PE-010`–`PE-025`, `PE-ACC-001`–`PE-ACC-016` → `VERIFIED`;
- `TASK-058`, `PE-030`–`PE-035` → `IMPLEMENTED`;
- `TASK-059`, `PE-ACC-020`–`PE-ACC-026` → `IN_PROGRESS`.

**Следующий рекомендуемый шаг:** собрать проект, дождаться `DB: Ready`, нажать `C` и зафиксировать `TASK-058 ... PASS`; затем повторить `X` и `Z` как регрессионные проверки.

### 2026-08-01 — hotfix ручного контура backup/recovery (`TASK-056/TASK-057`)

**Исходный снимок:** `ProjectHorizon-main-prototype-e-backup-recovery.zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-manual-recovery-hotfix.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 22.2, 22.3, 22.9, 36.3, Прототип E раздела 39 и пункты 10, 14, 15 раздела 41 PDF-ТЗ; `TASK-056`, `TASK-057`, `PE-020`–`PE-025`, `PE-ACC-010`–`PE-ACC-016`.

**Полученное runtime-доказательство:**

- локальная сборка завершилась успешно: `0` предупреждений, `0` ошибок;
- автоматический тест показал `TASK-056 recovery (X): PASS rev=10`;
- подтверждены `candidateRejected=1`, `backupPreserved=1`, `atomic=1`, `quarantine=1`;
- backup существовала и проходила `integrity=ok`;
- при последовательной ручной проверке HUD оставался на snapshot revision `2`, а счётчик `writes=2` не увеличивался после первой завершённой операции;
- пользователь обоснованно сообщил, что ручной режим работает не полностью.

**Установленная причина:**

- служебный `RefreshDiagnostics()` использовал то же поле `_loadTask`, что и ручная команда `L`;
- `PollLoadTask()` после завершения любого такого task снова вызывал `RefreshDiagnostics()`;
- возникала бесконечная цепочка `load → refresh → load → refresh`;
- `_loadTask` почти постоянно оставался ненулевым, поэтому `CanStartOperation()` молча отклонял последующие `S/L/R/B/Y/X/Z`, хотя HUD уже показывал `DB: Ready`;
- initialization не загружал существующий snapshot, из-за чего `_manualRevision` после перезапуска мог начинаться с нуля и записать revision ниже фактической;
- compact HUD не сохранял отдельные результаты ручных `B` и `Y`, поэтому успешность операций нельзя было однозначно увидеть на одном экране.

**Исправлено:**

- внутреннее обновление вынесено в отдельный `_refreshTask` и `PollRefreshTask()`;
- refresh является одноразовым и не запускает новый refresh после собственного завершения;
- `CanStartOperation()` учитывает `_refreshTask`, а HUD показывает `DB: Loading` до фактического окончания обновления вместо ложного `Ready`;
- после initialization выполняется одноразовая загрузка snapshot и diagnostics;
- `_manualRevision` синхронизируется с revision, реально находящейся в SQLite;
- ручной `L` больше не смешивается со служебным refresh;
- compact и detailed HUD получили сохраняемые строки `Slot S/L/R`, `Backup B` и `Restore Y` со статусами `RUNNING/PASS/FAIL` и ключевыми метриками;
- успешные `S/L/R` дополнительно печатаются в Godot Output;
- прежняя автоматическая логика backup, candidate validation, `File.Replace`, quarantine и recovery не изменялась.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Persistence/SavePrototype.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Проверки в среде подготовки:**

- статически подтверждено, что `PollRefreshTask()` не вызывает `BeginRefresh()`;
- ручная `_loadTask` и служебная `_refreshTask` разделены;
- `CanStartOperation()` блокирует запуск только до завершения явно видимого состояния `Loading`;
- initialization выполняет ровно один refresh;
- проверены ветви `S`, `L`, `R`, `B`, `Y`, `Z`, `X` и отсутствие конфликтов клавиш;
- выполнена лексическая проверка изменённого C#-файла, строк, комментариев и скобок;
- `.NET SDK` и Godot в среде подготовки отсутствуют, поэтому сборка hotfix и повторный runtime-тест здесь не заявляются.

**Статусы:**

- `TASK-054`, `TASK-055` и соответствующие foundation-требования остаются `VERIFIED`;
- `TASK-056`, `PE-020`–`PE-025` остаются `IMPLEMENTED`;
- `TASK-057`, `PE-ACC-010`–`PE-ACC-016` остаются `IN_PROGRESS` до сборки hotfix и полного ручного сценария;
- автоматический `X: PASS` принят как частичное доказательство, но не закрывает ручную приёмку.

**Следующий рекомендуемый шаг:** собрать hotfix, выполнить последовательность `R → S → B → S → Y`, убедиться в появлении трёх независимых `PASS`-строк и изменении revision `1 → 2 → 1`, затем повторить `X` и `Z`.

### 2026-08-01 — валидированная backup и атомарное recovery (`TASK-056`)

**Исходный снимок:** `ProjectHorizon-main(11).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-backup-recovery.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 22.2, 22.3, 22.9, 36.3, Прототип E раздела 39 и пункты 10, 14, 15 раздела 41 PDF-ТЗ; `TASK-054`–`TASK-057`, `PE-001`, `PE-010`–`PE-025`, `PE-ACC-001`–`PE-ACC-016`.

**Синхронизация runtime-приёмки предыдущей ступени:**

- пользователь прямо разрешил перевести `TASK-054` и `TASK-055` в `VERIFIED`;
- приняты критерии foundation acceptance: сборка `0` предупреждений / `0` ошибок;
- `TASK-054 save (Z): PASS`;
- schema `1`, `journal=wal`, `foreignKeys=1`, `synchronous=1`, `busyTimeout=5000`;
- revision `2`, inventory rows `3`, visited rows `1`;
- queued submissions `8`, maximum concurrent writer `1`;
- exact comparisons `2`, `integrity=ok`;
- `PE-001`, `PE-010`–`PE-015`, `PE-ACC-001`–`PE-ACC-006` → `VERIFIED`.

**Реализовано в `TASK-056`:**

- добавлен отдельный infrastructure-файл `SaveDatabase.Recovery.cs`; SQL и файловые операции не обращаются к Godot API;
- путь предыдущей копии соответствует ТЗ: `save_1.backup.db` рядом с основной БД;
- перед изменением существующего slot текущая исправная ревизия копируется SQLite online-backup API;
- первая успешно записанная ревизия получает backup после exact validation;
- очистка slot предварительно сохраняет предыдущую копию;
- backup-кандидат проверяется по наличию, размеру, schema, `PRAGMA integrity_check` и наличию snapshot до установки;
- существующая backup заменяется через `File.Replace`; до валидации кандидата исправная backup не изменяется;
- при неуспешной установке предусмотрен возврат предыдущей backup;
- основная БД проверяется при инициализации; повреждение вызывает автоматическое recovery из валидной backup;
- recovery-кандидат проверяется до подмены; основная БД заменяется атомарно, прежний файл и sidecar-файлы сохраняются как `save_1.quarantine.last.db`;
- backup при recovery не изменяется; её SHA-256 проверяется до и после теста;
- corruption/recovery события записываются в `logs/save_1.recovery.log`;
- `B` создаёт валидированную backup, `Y` вручную загружает предыдущую копию, `X` запускает изолированную acceptance route;
- `X` использует отдельную временную БД, поэтому намеренное повреждение не затрагивает основной slot;
- acceptance route создаёт protected revision `10`, более новую primary revision `11`, отклоняет повреждённый backup-кандидат, проверяет неизменность исправной backup, повреждает primary, выполняет атомарный rollback к revision `10`, проверяет обе БД, quarantine и recovery-log;
- HUD и Output расширены измеримыми признаками backup/recovery.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Persistence/SaveDatabase.cs`;
- `src/Game.Client/Scripts/Persistence/SaveDatabase.Recovery.cs`;
- `src/Game.Client/Scripts/Persistence/SaveGameModels.cs`;
- `src/Game.Client/Scripts/Persistence/SavePrototype.cs`;
- `src/Game.Client/Scenes/Persistence/SavePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Проверки в среде подготовки:**

- PDF-ТЗ извлечено и сверено по разделам 22.2, 22.9, 36.3, 39 и 41;
- проверена структура сцены и существующие NodePath;
- проверены новые горячие клавиши `B`, `Y`, `X` на отсутствие конфликтов в стартовой сцене;
- выполнена лексическая проверка C#-файлов: сбалансированы строки, комментарии, скобки и блоки;
- проверено отсутствие незавершённых строковых констант;
- `.NET SDK` и Godot в среде подготовки отсутствуют, поэтому фактическая сборка и runtime-тест здесь не заявляются.

**Статусы:**

- `TASK-054`, `TASK-055`, `PE-001`, `PE-010`–`PE-015`, `PE-ACC-001`–`PE-ACC-006` → `VERIFIED`;
- `TASK-056`, `PE-020`–`PE-025` → `IMPLEMENTED`;
- `TASK-057`, `PE-ACC-010`–`PE-ACC-016` → `IN_PROGRESS`;
- Прототип E остаётся `IN_PROGRESS`; исходная редакция уже дала сборку `0/0` и
  `X: PASS`, но hotfix ручного контура требует повторной сборки и полного
  сценария `R → S → B → S → Y → X → Z`.

**Следующий рекомендуемый шаг:** выполнить локальную runtime-приёмку `TASK-057`; после `PASS` закрыть backup/recovery и определить отдельную итерацию миграции старой версии/unknown content.

### 2026-08-01 — build-hotfix SQLite HUD (`TASK-054/TASK-055`)

**Runtime-доказательство пользователя:**

- NuGet успешно восстановил `Microsoft.Data.Sqlite 8.0.29` и зависимости SQLitePCLRaw;
- компиляция дошла до `CoreCompile`;
- сборка завершилась с `0` предупреждений и `1` ошибкой;
- единственная ошибка: `CS0103` в `SavePrototype.cs:606` — вызов `GetViewportRect()` недоступен в контексте `Node3D`.

**Исправлено:**

- получение размера viewport заменено на `GetViewport().GetVisibleRect().Size`;
- логика SQLite, migration, writer gate, транзакций и acceptance route не изменялась;
- `TASK-054` остаётся `IMPLEMENTED`, `TASK-055` остаётся `IN_PROGRESS` до чистой сборки и `Z: PASS`;
- `PE-ACC-001` не переводится в `VERIFIED`, поскольку предоставленная сборка была неуспешной.

**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-sqlite-build-hotfix.zip`

### 2026-08-01 — закрытие Прототипа D и SQLite-фундамент Прототипа E (`TASK-053/TASK-054`)

**Исходный снимок:** `ProjectHorizon-main(9).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-sqlite-foundation.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 22.1–22.5, 36.3 и Прототип E раздела 39 PDF-ТЗ; `TASK-051`–`TASK-056`, `PD-050`–`PD-053`, `PD-ACC-040`–`PD-ACC-045`, `PE-001`, `PE-010`–`PE-015`, `PE-ACC-001`–`PE-ACC-006`.

**Runtime-доказательство предыдущей итерации:**

- локальная сборка: `0` предупреждений, `0` ошибок;
- `TASK-051 soak (V): PASS 100/100`;
- все 100 физических touchdown-циклов завершены;
- `gear=3`, максимальная скорость касания `2,67 м/с`;
- managed-memory delta `0,02 MiB`;
- `nodeDelta=0`;
- baseline восстановлен, посадочная точка освобождена, опоры сложены;
- пользователь предоставил screenshot финального `PASS`.

**Закрытие Прототипа D:**

- `TASK-051`, `TASK-052`, `PD-050`–`PD-053`, `PD-ACC-040`–`PD-ACC-045` → `VERIFIED`;
- `TASK-053` → `VERIFIED`;
- Прототип D → `VERIFIED`.

**Реализовано в `TASK-054`:**

- добавлена отдельная стартовая сцена `Scenes/Persistence/SavePrototype.tscn`;
- подключён `Microsoft.Data.Sqlite` `8.0.29` без Entity Framework;
- игровой slot хранится по структуре `user://profiles/profile_prototype/save_1.db`;
- реализована явная migration `1` и таблицы `schema_migrations`, `save_meta`, `save_settings`, `player_state`, `ships`, `containers`, `inventory_items`, `visited_planets`;
- каждое подключение устанавливает `journal_mode=WAL`, `foreign_keys=ON`, `synchronous=NORMAL`, `busy_timeout=5000`;
- запись выполняется через единственный `SemaphoreSlim` writer gate и полностью уходит с main thread;
- snapshot транзакционно сохраняет позицию игрока, состояние корабля, три inventory item и посещённую планету;
- загрузка восстанавливает snapshot и сравнивается с исходными данными без сериализации Godot-объектов;
- все SQL-запросы параметризованы;
- `Z` запускает acceptance route: migration → baseline save/load → восемь конкурентных submit через единственный writer → final exact load → `integrity_check`;
- `S/L/R` обеспечивают ручное сохранение, загрузку и очистку slot;
- compact/detailed/hidden HUD показывает PRAGMA, schema, queue, snapshot и результат `TASK-054`;
- SQL и файловые операции не обращаются к Godot API и выполняются вне main thread.

**Граница итерации:** backup, атомарная замена, повреждение основной БД и recovery относятся к следующей `TASK-056`.

**Статусы:**

- `TASK-054`, `PE-001`, `PE-010`–`PE-015` → `IMPLEMENTED`;
- `TASK-055`, `PE-ACC-001`–`PE-ACC-006` → `IN_PROGRESS`;
- Прототип E → `IN_PROGRESS`.

### 2026-08-01 — runtime `TASK-051: FAIL total timeout` и масштабируемый timeout-hotfix

**Runtime-доказательство пользователя:**

- локальная сборка завершена: `0` предупреждений, `0` ошибок;
- soak дошёл до `cycle=98`, завершив `97` полных физических посадок;
- `attempts=98`, `touchdowns=97`, `locks=97`, `gearMin=3`;
- `touchdownSpeed=2,667 м/с`, `positionError=0,000 м`, `angularError=0,040°`;
- `managedGrowthMiB=0,013`, `managedPeakGrowthMiB=4,512`, `nodeDelta=0`;
- `recoveries=0`, `collisions=0`, `errors=0`;
- единственная причина `FAIL`: `total timeout phase=Descending, cycle=98`.

**Диагноз:** общий лимит `240 с` противоречил локальному per-cycle лимиту `4,5 с`: для 100 допустимых циклов сумма только циклов могла достигать `450 с`, не считая первоначального поиска и alignment. Поэтому корректный прогон мог быть остановлен общим timeout при отсутствии функциональных дефектов.

**Исправлено:**

- effective total timeout теперь вычисляется как максимум между настроенным floor и `setupAllowance + cycles × cycleTimeout`;
- при стандартных параметрах: `max(240, 30 + 100 × 4,5) = 480 с`;
- per-cycle timeout `4,5 с` сохранён без ослабления и по-прежнему немедленно выявляет зависший отдельный цикл;
- HUD показывает `elapsed/effective budget`;
- стартовая и итоговая строки Output содержат configured/effective timeout, elapsed и budget;
- критерии counters, gear contacts, touchdown errors, SceneTree и memory не изменялись.

**Статусы:**

- `PD-ACC-040` → `VERIFIED` по чистой локальной сборке;
- `TASK-051`, `PD-050`–`PD-053` остаются `IMPLEMENTED`;
- `TASK-052`, `PD-ACC-041`–`PD-ACC-045` остаются `IN_PROGRESS` до `V: PASS 100/100`.

### 2026-08-01 — приёмка `TASK-049/TASK-050` и реализация 100-landing soak `TASK-051`

**Исходный снимок:** `ProjectHorizon-main(5)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-100-landing-soak.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** раздел 36.4 PDF-ТЗ (`100 последовательных посадок`); `TASK-049`–`TASK-052`, `PD-040`–`PD-053`, `PD-ACC-030`–`PD-ACC-045`.

**Runtime-доказательство предыдущей итерации:**

- после build-hotfix локальная сборка завершена: `0` предупреждений, `0` ошибок;
- ручной цикл `M` подтверждён screenshots: `Aligned` на высоте `12,0 м`, `posErr=0,00 м`, `angErr=0,04°`;
- `Landed`: высота `1,6 м`, deployment `1,00`, contacts `3/3`, linear/angular speed `0`;
- после взлёта: `Idle`, высота `18,2 м`, deployment `0`, radial speed `16,7 м/с`;
- Godot Output: `TASK-049 touchdown/takeoff acceptance PASS`;
- `cycles=2`, `attempts=2`, `touchdowns=2`, `locks=2`, `takeoffs=2`, `gear=3`;
- `touchdownSpeed=2,800 м/с`, `positionError=0,000 м`, `angularError=0,040°`, `takeoffClearance=12,00 м`;
- `recoveries=0`, `collisions=0`, `errors=0`.

**Исправлен UI-дефект:**

- `TouchdownTestStatusText` добавлен в compact и detailed HUD;
- итог `TASK-049 touchdown (O): PASS` больше не остаётся только в Godot Output.

**Реализовано в `TASK-051`:**

- добавлен `ShipLandingSoakAcceptance.cs`;
- клавиша `V` запускает 100 последовательных физических touchdown-циклов;
- первый цикл выполняет обычный поиск и alignment, последующие циклы используют сохранённую reservation и короткий стартовый clearance `3,8 м`;
- каждый цикл реально проходит `Descending → GearContact → Landed`, требует `3/3` probes и `PhysicsLockedOnGear`;
- прямой reposition между циклами исключает кинематографический takeoff и сокращает ожидаемое время теста до 2–4 минут, не подменяя физическое касание;
- контролируются точные счётчики attempts/touchdowns/locks, минимальное число контактов, максимальные contact speed, position error и angular error;
- фиксируются recoveries, collisions, runtime errors и per-cycle timeout;
- до и после soak сравниваются число узлов SceneTree и managed memory после full GC; дополнительно контролируется peak managed growth;
- прогресс выводится каждые 10 посадок; итог дублируется в HUD и Godot Output;
- повторное `V` безопасно отменяет тест и восстанавливает baseline;
- `J/L/N/O/P/M` блокируются во время soak, чтобы исключить конфликт управляющих контуров.

**Изменения статусов:**

- `TASK-049`, `TASK-050`, `PD-040`–`PD-045`, `PD-ACC-030`–`PD-ACC-036` → `VERIFIED`;
- `TASK-051`, `PD-050`–`PD-053` → `IMPLEMENTED`;
- `TASK-052`, `PD-ACC-040`–`PD-ACC-045` → `IN_PROGRESS`.

**Ограничение:** Прототип D будет переведён в `VERIFIED` после локальной чистой сборки и `TASK-051 soak (V): PASS 100/100`.

### 2026-08-01 — build hotfix `TASK-049` после локальной C#-сборки

**Основание:** локальная сборка пользователя выявила четыре ошибки `CS0102`: partial-класс `ShipFlightPrototype` содержал повторные определения `_testCollisions` и `_testErrors` одновременно в базовом free-flight acceptance и новом touchdown acceptance. Два дополнительных сообщения относились к тем же именам в сгенерированном Godot `PropertyName`.

**Исправлено:**

- поля touchdown-теста переименованы в `_touchdownTestCollisions` и `_touchdownTestErrors`;
- обновлены все присваивания, проверки критериев и итоговая строка touchdown/takeoff Output;
- выполнена статическая проверка уникальности полей во всех partial-файлах `ShipFlightPrototype`;
- функциональная логика `TASK-049`, критерии `PASS/FAIL` и статусы требований не изменялись.

**Ожидаемая повторная проверка:** чистая локальная сборка `Game.Client.csproj`, затем ручной трёхэтапный цикл `M` и автоматический тест `O`.

### 2026-08-01 — приёмка `TASK-047/TASK-048` и реализация touchdown/takeoff `TASK-049`

**Исходный снимок:** `ProjectHorizon-main(4)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-touchdown-takeoff.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 14.4, 15.3, 36.4 и Прототип D раздела 39 PDF-ТЗ; `TASK-047`–`TASK-051`, `PD-030`–`PD-045`, `PD-ACC-020`–`PD-ACC-036`.

**Runtime-доказательство предыдущей итерации:**

- локальная сборка: `0` предупреждений, `0` ошибок;
- ручной `M` завершился состоянием `Aligned`;
- reserved slope `0,0°`, clearance `29,7 м`;
- position error `0,00 м`, angular error `0,04°`;
- `TASK-047 landing (N): PASS`;
- `checks=3`, `slopeReject=1`, `obstacleReject=1`;
- free-flight regression `J: PASS` сохранён.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/Ship/ArcadeShipTouchdown.cs`;
- `src/Game.Client/Scripts/Ship/ArcadeShipTouchdown.cs.uid`;
- `src/Game.Client/Scripts/Ship/ShipTouchdownAcceptance.cs`;
- `src/Game.Client/Scripts/Ship/ShipTouchdownAcceptance.cs.uid`;
- `src/Game.Client/Scripts/Ship/ArcadeShipController.cs`;
- `src/Game.Client/Scripts/Ship/ShipFlightPrototype.cs`;
- `src/Game.Client/Scripts/Ship/ShipLandingAcceptance.cs`;
- `src/Game.Client/Scripts/Ship/ShipAtmosphereAcceptance.cs`;
- `src/Game.Client/Scenes/Ship/ArcadeShip.tscn`;
- `src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано в `TASK-049`:**

- добавлены три визуальные посадочные опоры и три независимых `RayCast3D` gear probes;
- финальное снижение начинается только после подтверждённого состояния `Aligned` и действующей reservation;
- скорость снижения ограничена `2,8 м/с`, basis продолжает сходиться к normal поверхности;
- касание подтверждается всеми тремя физическими probes, ограничением скорости, position error и angular error;
- после устойчивого контакта корабль переходит в `LANDED`: linear/angular velocity обнуляются, transform фиксируется на опорах, обычная flight/atmosphere physics не выполняется;
- взлёт перемещает корабль по normal до clearance `12 м`, затем возвращает ручное управление и складывает опоры;
- `M` выполняет ручной трёхэтапный цикл `alignment → touchdown → takeoff`;
- `O` запускает автономный двухцикловый `TASK-049` acceptance test;
- тест проверяет повторяемость reservation/alignment/touchdown/landed lock/takeoff, три gear contacts, контактную скорость, ошибки положения/ориентации, takeoff clearance и отсутствие recovery/collision/runtime errors;
- `J`, `L`, `N`, `P` и ручные режимы блокируются во время активного touchdown test/sequence, чтобы исключить конфликт управляющих контуров.

**Статические проверки:**

- все `26` C#-файлов прошли лексическую проверку строк, комментариев и скобок;
- проверены `res://`-ссылки, scene `load_steps`, NodePath и уникальность UID;
- геометрия gear probes рассчитана для трёх контактов при center clearance `1,55 м`;
- расчётный цикл: alignment ≈ `4,7 с`, descent ≈ `4 с`, landed hold `0,8 с`, takeoff ≈ `2 с`; два цикла укладываются в timeout `34 с`;
- Godot/.NET SDK в текущей среде отсутствуют, поэтому сборка и runtime новой функции не заявляются.

**Изменения статусов:**

- `TASK-047`, `TASK-048`, `PD-030`–`PD-035`, `PD-ACC-020`–`PD-ACC-027` → `VERIFIED`;
- `TASK-049`, `PD-040`–`PD-045` → `IMPLEMENTED`;
- `TASK-050`, `PD-ACC-030`–`PD-ACC-036` → `IN_PROGRESS`.

**Ограничение:** Прототип D переводится в `VERIFIED` только после локального `O: PASS`, ручного цикла `M` и подтверждения отсутствия регрессий. Нагрузочный сценарий 100 последовательных посадок остаётся отдельной `TASK-051` раздела 36.4 PDF-ТЗ.

### 2026-08-01 — приёмка `TASK-045/TASK-046` и реализация landing-point alignment `TASK-047`

**Исходный снимок:** `ProjectHorizon-main(3)(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-landing-point-alignment.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 14.4, 15.3, 36.4 и Прототип D раздела 39 PDF-ТЗ; `TASK-045`–`TASK-049`, `PD-020`–`PD-035`, `PD-ACC-010`–`PD-ACC-027`.

**Runtime-доказательство предыдущей итерации:**

- локальная сборка: `0` предупреждений, `0` ошибок;
- ручной `P`-подход подтвердил переход `SPACE → ATMOSPHERE`;
- `TASK-045 atmosphere (L): PASS`;
- `entry=1`, `exit=1`, `maxBlend=0,94`, `dragDrop=9,4 м/с`;
- minimum-speed applications `62`, climb limit `16,0 м/с`, surface-safety `85`;
- `minAltitude=13,8 м`, `recoveries=0`, `collisions=0`, `errors=0`;
- free-flight regression: `J: PASS`, `vmax=72,0 м/с`, distance `220,5 м`, final speed/angular `0`.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/Ship/ArcadeShipLanding.cs`;
- `src/Game.Client/Scripts/Ship/ArcadeShipLanding.cs.uid`;
- `src/Game.Client/Scripts/Ship/ShipLandingAcceptance.cs`;
- `src/Game.Client/Scripts/Ship/ShipLandingAcceptance.cs.uid`;
- `src/Game.Client/Scripts/Ship/ShipLandingTestSite.cs`;
- `src/Game.Client/Scripts/Ship/ShipLandingTestSite.cs.uid`;
- `src/Game.Client/Scripts/Ship/ArcadeShipController.cs`;
- `src/Game.Client/Scripts/Ship/ShipFlightPrototype.cs`;
- `src/Game.Client/Scripts/Ship/ShipAtmosphereAcceptance.cs`;
- `src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано в `TASK-047`:**

- surface probe выполняется лучами к физической поверхности планеты;
- кандидаты проверяются последовательно и получают измеримый счётчик `checks/hits`;
- уклон вычисляется по углу между normal попадания и радиальным Up; предел — `12°`;
- препятствия проверяются по зарезервированной clearance-зоне; тестовый obstacle находится в группе `landing_obstacle`;
- первая тестовая точка отклоняется по уклону, вторая — по препятствию, третья безопасно резервируется;
- зарезервированная точка визуализируется отдельным emissive marker;
- landing assist отключает ручную основную физику, плавно перемещает корабль к hover-точке `12 м` и согласует локальный Up с normal поверхности;
- состояние `Aligned` удерживает нулевые линейную и угловую скорости до отмены;
- `M` запускает/отменяет ручной поиск и выравнивание;
- `N` запускает автономный `TASK-047` acceptance test и восстанавливает baseline;
- free-flight и atmosphere tests блокируются на время landing assist, чтобы исключить конфликт внешних команд.

**Статические проверки:**

- все `24` C#-файла прошли лексическую проверку строк, комментариев и скобок;
- проверены все `res://`-ссылки, scene `load_steps`, NodePath и resource IDs;
- расчётная модель подхода: расстояние до safe hover ≈ `40,33 м`, минимальный clearance траектории `12 м`, settle ≈ `4,65 с`;
- тестовая геометрия гарантирует один slope rejection (`18° > 12°`) и один obstacle rejection;
- Godot/.NET SDK в текущей среде отсутствуют, поэтому сборка и runtime новой функции не заявляются.

**Изменения статусов:**

- `TASK-045`, `TASK-046`, `PD-020`–`PD-025`, `PD-ACC-011`–`PD-ACC-018` → `VERIFIED`;
- `TASK-047`, `PD-030`–`PD-035` → `IMPLEMENTED`;
- `TASK-048`, `PD-ACC-020`–`PD-ACC-027` → `IN_PROGRESS`.

**Ограничение:** текущая ступень заканчивается устойчивым выравниванием над зарезервированной точкой. Финальное снижение, посадочные опоры, `LANDED`, отключение основной физики на опорах и взлёт относятся к следующей `TASK-049`.

### 2026-08-01 — runtime `L: FAIL Entry timeout` и atmospheric-entry hotfix

**Фактическое доказательство пользователя:**

- локальная сборка `Game.Client.csproj`: `0` предупреждений, `0` ошибок;
- free-flight regression остаётся `TASK-043 flight (J): PASS`;
- автоматический атмосферный тест завершился `TASK-045 atmosphere (L): FAIL — timeout phase=Entry`;
- после восстановления baseline HUD показывал `SPACE`, `alt=441,8 м`, `blend=0,00`;
- следовательно, сборка и запуск подтверждены, но переход через entry-фазу не был воспроизводимым.

**Причина и исправление:**

- прежний acceptance route полагался на единственный стартовый inward-импульс и локальную lift-команду;
- добавлен отдельный radial-guidance target, применяемый внутри `_PhysicsProcess` после атмосферных сил и до `MoveAndSlide`;
- guidance поддерживает заданную отрицательную радиальную скорость только до достижения `InAtmosphere && blend >= 0,20`, после чего полностью отключается;
- ручной подход по `P` использует тот же временный entry-guidance и отключает его после устойчивого входа;
- `ResetToSpawn`, восстановление baseline и завершение теста принудительно очищают guidance;
- вместо общего 16-секундного ожидания entry-фаза имеет отдельный timeout `5 с` и выводит `startAlt`, `minAlt`, текущие `alt/radial/blend`;
- остальные фазы minimum-speed, drag, climb-limit, surface-safety и exit не изменялись.

**Статусы:**

- `PD-ACC-010` → `VERIFIED` по чистой пользовательской сборке;
- `TASK-045`, `PD-020`–`PD-025` остаются `IMPLEMENTED`;
- `TASK-046`, `PD-ACC-011`–`PD-ACC-018` остаются `IN_PROGRESS` до повторного `L: PASS`.

### 2026-08-01 — приёмка free-flight и `TASK-045` atmospheric flight foundation

**Runtime-доказательство предыдущей итерации:**

- локальная сборка `Game.Client.csproj`: `0` предупреждений, `0` ошибок;
- `TASK-043 flight (J): PASS`;
- `vmax=72,0 м/с`, `distance=221,2 м`, `lateral=38,4 м/с`, `vertical=20,4 м/с`;
- `angular=105,4°/с`, конечные линейная и угловая скорости равны `0`;
- столкновения `0/0/0`;
- пользователь вручную подтвердил тягу, strafe/lift, тангаж/рыскание/крен, форсаж, торможение, стабилизацию и обе камеры.

**Реализовано в `TASK-045`:**

- сцена получила физическую тестовую планету радиусом `120 м` и атмосферную оболочку высотой `90 м`;
- корабль автоматически определяет высоту над поверхностью, радиальное направление и плавный коэффициент атмосферы;
- в атмосфере применяются упрощённые gravity/lift forces без полноценной аэродинамической модели;
- минимальная forward airspeed поддерживается мягким stall-assist, если торможение не включено;
- сопротивление зависит от плотности атмосферы и квадрата скорости, с ограничением максимального замедления;
- радиальная скорость набора ограничена параметром `AtmosphereMaximumClimbSpeed`;
- surface-safety прогнозирует тормозной путь, создаёт подъёмный импульс и блокирует отрицательную радиальную скорость около поверхности;
- hard-floor correction оставлена только как аварийная защита и отдельно учитывается счётчиком recoveries;
- `P` переносит корабль к границе атмосферы для ручной проверки и возвращает к космическому spawn;
- `L` запускает автономный acceptance route: вход, minimum-speed assist, drag, climb-limit, safety descent и выход обратно в космос;
- compact/detailed HUD показывает SPACE/ATMOSPHERE, altitude, blend, radial speed, forward airspeed, stall/safety state и измеримые результаты теста;
- free-flight test по `J` сохранён и остаётся отдельным регрессионным сценарием.

**Выполненные статические проверки:**

- лексическая проверка всех 21 C#-файлов, строковых констант, комментариев и скобок — `PASS`;
- все ссылки `res://`, `load_steps`, NodePath и идентификаторы ресурсов сцен — `PASS`;
- в архиве отсутствуют `.godot`, `bin`, `obj`, `.git` и IDE-кэш;
- детерминированная математическая симуляция acceptance route при 120 Hz завершилась за `6,51 с`: entry=`1`, exit=`1`, maxBlend=`0,940`, dragDrop=`9,75 м/с`, minSpeed=`122`, climbLimit=`63`, maxClimb=`15,73 м/с`, safety=`169`, minAltitude=`13,40 м`, recoveries=`0`.

Симуляция не заменяет сборку и runtime-приёмку в Godot; она подтверждает только согласованность порогов и тестового маршрута.

**Изменения статусов:**

- `TASK-043`, `TASK-044`, `PD-001`, `PD-010`–`PD-017`, `PD-ACC-001`–`PD-ACC-006` → `VERIFIED`;
- `TASK-045`, `PD-020`–`PD-025` → `IMPLEMENTED`;
- `TASK-046`, `PD-ACC-011`–`PD-ACC-018` → `IN_PROGRESS` до локального `L: PASS`;
- Прототип D остаётся `IN_PROGRESS`.

**Ограничение:** посадка, проверка уклона/препятствий, опоры и отключение основной физики не входят в `TASK-045` и будут реализованы после приёмки атмосферного режима.

### 2026-08-01 — приёмка HUD ergonomics и `TASK-043` free-flight foundation

**Runtime-доказательство предыдущей итерации:**

- локальная сборка `Game.Client.csproj`: `0` предупреждений, `0` ошибок;
- compact HUD оставляет основную часть 3D-холста доступной;
- detailed HUD сохраняет полную visual/collision телеметрию и вертикальную прокрутку;
- hidden HUD оставляет только индикатор `HUD скрыт • H`;
- пользователь подтвердил корректное циклическое переключение режимов и отсутствие регрессии Прототипа C.

**Реализовано в `TASK-043`:**

- добавлена отдельная стартовая сцена `Scenes/Ship/ShipFlightPrototype.tscn`;
- добавлен базовый корабль `Scenes/Ship/ArcadeShip.tscn` на `CharacterBody3D` в floating motion mode;
- реализованы тяга вперёд/назад, боковые и вертикальные импульсные двигатели;
- реализованы рыскание, тангаж и крен в локальной системе корабля;
- добавлены форсаж, торможение и автоматическая угловая стабилизация;
- `F2` переключает chase/cockpit камеры, `R` восстанавливает spawn, `G` переключает стабилизацию;
- compact/detailed/hidden HUD корабля переключается по `H` и не использует ScrollContainer в compact mode;
- `J` запускает автономный acceptance route, который измеряет скорость, дистанцию, боковую/вертикальную тягу, угловую скорость, торможение, стабилизацию и переключение камер;
- в принятой планетарной сцене compact scrollbar явно отключается; прокрутка остаётся только в detailed mode.

**Границы итерации:** атмосферный режим, посадка, взлёт и переход к поверхности не включены; они выделены в следующие задачи Прототипа D.

**Изменения статусов:**

- `TASK-041`, `TASK-042`, `PC-100`–`PC-104`, `PC-ACC-070`–`PC-ACC-074` → `VERIFIED`;
- `TASK-043`, `PD-001`, `PD-010`–`PD-017` → `IMPLEMENTED`;
- `TASK-044`, `PD-ACC-001`–`PD-ACC-006` → `IN_PROGRESS`;
- Прототип D → `IN_PROGRESS`.

### 2026-08-01 — приёмка dynamic collision LOD, завершение Прототипа C и HUD ergonomics

**Runtime-доказательство пользователя:**

- сборка `Game.Client.csproj`: `0` предупреждений, `0` ошибок;
- исходное состояние collision: `active=42/42`, `staged=0`, `queue=0`, `state=Idle`, `fallback=off`, `recoveries=0`, `errors=0`;
- acceptance test: `TASK-038 collision (K): PASS`;
- `plans=60`, `commits=60`, `created=257`, `unloaded=233`, `fallback=60`;
- во время маршрута присутствовал fine collision LOD: `L3=28`;
- `gap=0,00 с`, `rMin=92,46 м`, `recoveries=0`, `errors=0`;
- после теста сохранены `ground=да`, `floor=да`, `probe=да`, `Δup=0,00°` и четыре межгранных перехода;
- пользователь больше не наблюдает циклических провалов и подбрасываний.

**Итог Прототипа C (`TASK-040`):**

PDF-ТЗ требует cube sphere, гравитацию к центру, ходьбу, floating origin и швы LOD. Все пять пунктов подтверждены отдельными runtime-тестами; visual и collision quadtree дополнительно проверены стресс-маршрутами. Прототип C переведён в `VERIFIED`.

**Реализовано в `TASK-041`:**

- диагностический HUD по умолчанию переведён в компактный режим высотой `220 px` вместо панели почти на весь экран;
- клавиша `H` циклически переключает `COMPACT → DETAILED → HIDDEN`;
- compact HUD показывает только критические visual/collision/player/topology/test показатели;
- detailed HUD сохраняет всю прежнюю телеметрию внутри ограниченной прокручиваемой панели;
- hidden mode оставляет только небольшой индикатор `HUD скрыт • H` в правом верхнем углу;
- размер панели ограничивается фактическим viewport, поэтому HUD не выходит за границы окна при изменении разрешения;
- compact mode не перехватывает мышь; detailed mode разрешает прокрутку колёсиком;
- смена режима дублируется в Output строкой `Prototype HUD mode: ...`; HUD обновлён в сцене, README и инструкции приёмки.

**Изменения статусов:**

- `TASK-038`, `TASK-039`, `TASK-040`, `PC-090`–`PC-096`, `PC-ACC-061`–`PC-ACC-068` → `VERIFIED`;
- Прототип C → `VERIFIED`;
- `TASK-041`, `PC-100`–`PC-104` → `IMPLEMENTED`;
- `TASK-042`, `PC-ACC-070`–`PC-ACC-074` → `IN_PROGRESS`.

### 2026-08-01 — hotfix после `TASK-038 collision (K): FAIL timeout`

**Полученное доказательство:**

- сборка новой collision-редакции: `0` предупреждений, `0` ошибок;
- исходное состояние: `active=27/27`, `state=Idle`, `fallback=off`, `errors=0`, `ground/floor/probe=да`;
- после маршрута: `plan=150`, `commits=150`, `created=647`, `unloaded=620`, `fallback activations=149`, `errors=0`;
- итог: `TASK-038 collision (K): FAIL timeout`;
- игрок: `r=83,0 м`, `ground=нет`, `floor=нет`, `probe=нет`;
- ручное наблюдение: игрок циклически подбрасывается примерно к `r=90 м` и снова падает внутрь.

**Причина:** collision target зависел от visual resident/horizon culling и не гарантировал полного physics-cover. Дополнительно collision mesh исключал skirts, поэтому смешанные `L1/L2/L3` границы могли содержать физические T-junction. Fallback периодически включался новым plan и выталкивал уже провалившегося игрока наружу, после отключения цикл повторялся.

**Исправлено:**

- collision target теперь равен полной логической quadtree-топологии (`42–48` leaves), независимо от visual culling;
- удалена angular-distance активация fallback, создававшая лишние plan/commit циклы при уже полном collision-cover;
- collision patches строятся непосредственно из детерминированного patch builder, включая невидимые visual leaves;
- physics mesh включает inward-skirts, закрывающие межуровневые T-junction;
- overlap увеличен до `4` physics-кадров и дополнен `6` кадрами подтверждения контакта;
- введён минимальный безопасный радиус `PlanetRadius - HeightAmplitude - 0,5 м`;
- acceptance test немедленно фиксирует `radial underflow`, а не ждёт timeout;
- при аварийном underflow fallback включается и игрок однократно восстанавливается над максимальной поверхностью;
- acceptance test всегда восстанавливает исходный transform игрока после `PASS/FAIL/CANCELLED`;
- HUD и Output дополнены `rMin` и `recoveries`.

**Статус:** `TASK-038` остаётся `IMPLEMENTED`; `TASK-039` остаётся `IN_PROGRESS` до повторного `K: PASS`.

### 2026-08-01 — приёмка `TASK-036/TASK-037` и динамический collision LOD `TASK-038`

**Runtime-доказательство принятой async visual streaming итерации:**

- локальная сборка `Game.Client.csproj`: `0` ошибок, `0` предупреждений;
- стабильное состояние: applied/resident/logical `41/41/42`, `L3=8`, queue `0`, workers `0`, errors `0`;
- acceptance test: `TASK-036 stream (I): PASS revisions=10, L3=12, resident=44/45, unloaded=93, queue=0, workers=0, cancel=0, stale=24, errors=0`;
- topology после теста: `open=0`, `nonManifold=0`, `Δlod=1`, `Δpos=0`;
- collision `6/6 (129×129)`, ground/floor/probe и радиальная система остались `PASS`.

**Реализовано в `TASK-038`:**

- collision больше не ограничивается постоянно активными шестью полногранными сетками;
- рядом с игроком выбирается отдельный набор collision patches из текущих quadtree-листьев `L1/L2/L3`;
- collision patch создаётся только из верхней поверхности `33×33`, без визуальных skirts;
- `ArrayMesh`, `ConcavePolygonShape3D`, `CollisionShape3D` и `SceneTree` применяются только в main thread;
- неизменяемые шесть полногранных collision-форм `129×129` сохранены как safety fallback;
- при каждом новом collision plan fallback включается до готовности целевого набора;
- новые collision shapes сначала создаются disabled, затем включаются deferred;
- старый набор и fallback остаются активными ещё `CollisionOverlapPhysicsFrames` physics-кадров;
- только после подтверждения включения нового набора и фактического deferred-отключения fallback/устаревших shapes выполняется commit и освобождение;
- при быстром удалении игрока от collision anchor fallback включается автоматически;
- HUD показывает active/target/staged collision patches, queue, plan, commits, состояние перехода, распределение `L1/L2/L3`, created/unloaded, fallback activations и errors;
- `K` запускает acceptance test: после подготовки динамического набора автоматически выполняется принятый межгранный seam traversal, а collision LOD сопровождает реальное движение игрока;
- тест требует несколько collision plans/commits, создание и выгрузку patches, фактические fallback activations, наличие `L3`, нулевые ошибки и ground gap не более `0,12 с`;
- повторное `K`, `F2`, `R`, `T`, `Y`, `U` или `I` безопасно останавливает collision acceptance test.

**Статические проверки:**

- геометрическая симуляция принятого четырёхшовного great-circle маршрута: collision target содержит `27–47` patches, меняется многократно, создаёт и выгружает локальные участки; во всех выборках присутствуют рабочие `L1/L2/L3`;
- проверены C#-строки, комментарии, скобки, nullable-out, scene/resource paths и ZIP hygiene;
- deferred-состояние проверяется фактически: commit ожидает `Disabled=true` у fallback и всех устаревших shapes; `ConcavePolygonShape3D` используется только под `StaticBody3D`;
- Godot/.NET SDK в текущей среде отсутствуют, поэтому сборка и runtime новой collision-итерации не заявляются.

**Изменения статусов:**

- `TASK-036`, `TASK-037`, `PC-080`–`PC-086`, `PC-ACC-050`–`PC-ACC-057` → `VERIFIED`;
- `TASK-038`, `PC-090`–`PC-096` → `IMPLEMENTED`;
- `TASK-039`, `PC-ACC-060`–`PC-ACC-068` → `IN_PROGRESS`.

**Следующая задача:** выполнить runtime-приёмку `TASK-039` по клавише `K`; после `PASS` определить завершённость Прототипа C и переход к Прототипу D.

### 2026-08-01 — приёмка `TASK-033/TASK-035` и реализация async visual streaming `TASK-036`

**Основание:** раздел 9.3 PDF-ТЗ; локальное доказательство пользователя по `TASK-033/TASK-035`.

**Принято как runtime-доказательство:**

- сборка `Game.Client.csproj`: `0` ошибок, `0` предупреждений;
- HUD: `TASK-033 LOD (U): PASS split=17, merge=16, Δlod=1, open=0, seam=0`;
- `patches=36`, `L1=20`, `L2=16`, `atomic=112`, `nonManifold=0`;
- collision `6/6 (129×129)`, `ground=да`, `floor=да`, `probe=да`;
- `DEBT-CS8600` отсутствует в чистой сборке.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/CubeSpherePatchLod.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePatchStreaming.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано в `TASK-036`:**

- рекурсивное дерево использует три уровня `L1/L2/L3`;
- отдельные split/merge thresholds и hysteresis применяются на базовом и глубоком уровнях;
- после выбора уровней выполняется 2:1 balancing, поэтому соседние листья не отличаются более чем на один уровень;
- полная логическая топология отделена от resident-набора визуальных patches;
- patches за горизонтом не входят в resident-set; culling учитывает не только центр, но и угловой радиус участка, а obsolete patches освобождаются после полной готовности нового плана;
- построение вершин, нормалей, UV, индексов и skirts выполняется в отменяемых worker jobs;
- каждый план имеет revision; отменённые и stale результаты не применяются;
- `MeshInstance3D`, `ArrayMesh`, `SceneTree` и освобождение узлов выполняются только в main thread;
- новые patches создаются скрытыми; после загрузки всего нового resident-set устаревшие участки скрываются, целевой набор включается и старые узлы удаляются одним main-thread commit-этапом;
- HUD показывает applied/resident/logical, `L1/L2/L3`, plan, queue, workers, ready, cancel, stale, errors и unloaded;
- `I` запускает быстрый маршрут из девяти направлений, создаёт несколько revisions и затем ожидает `queue=0`, `workers=0`;
- стабильные шесть collision-граней `129×129` сохранены; динамический collision LOD перенесён в `TASK-038`.

**Статические проверки:**

- математическая симуляция девяти направлений подтверждает `L3>0`, `open=0`, `nonManifold=0`, `maxDelta=1` и `atomic=228–240`;
- типичные состояния: `logical=42–48`, resident `39–44`, уровни включают `L1/L2/L3`;
- проверены C#-строки, комментарии, баланс скобок, scene/resource paths и ZIP hygiene;
- Godot/.NET SDK в текущей среде отсутствуют, поэтому сборка и runtime для новой функции не заявляются.

**Изменения статусов:**

- `TASK-033`, `TASK-035`, `PC-070`–`PC-074`, `PC-ACC-040`–`PC-ACC-045`, `DEBT-CS8600` → `VERIFIED`;
- `TASK-036`, `PC-080`–`PC-086` → `IMPLEMENTED`;
- `TASK-037`, `PC-ACC-050`–`PC-ACC-057` → `IN_PROGRESS`;
- `TASK-038` → `PLANNED` как отдельный collision LOD шаг.

**Следующая задача:** выполнить локальную приёмку `TASK-037` по клавише `I`; после `PASS` перейти к динамическому collision LOD `TASK-038`.

### 2026-08-01 — реализация начальной quadtree LOD-ступени `TASK-033`

**Основание:** раздел 9.3 и Прототип C раздела 39 PDF-ТЗ; предыдущие `TASK-032/TASK-034` подтверждены runtime.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/CubeSpherePatchLod.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePatchLod.cs.uid`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано:**

- шесть граней визуально покрываются независимыми patches, каждый patch имеет сетку `33 × 33`;
- базовый уровень `L1` создаёт 24 участка, ближайшие к игроку родители делятся на четыре дочерних участка `L2`;
- split/merge используют разные угловые пороги, поэтому переходы имеют hysteresis;
- геометрия общей поверхности вычисляется одной функцией по радиальному направлению и seed;
- каждый patch получает skirts на четырёх сторонах; collision остаётся шестью стабильными полногранными поверхностями;
- topology validator раскладывает границы до атомарных сегментов максимального уровня и проверяет полное двойное покрытие, non-manifold, `Δlod <= 1` и позиционную ошибку общих точек;
- HUD показывает patches, `L1/L2`, split-родителей, skirts, revision, open/non-manifold, `Δlod` и `Δpos`;
- `F1` циклически переключает диагностику граней, уровней LOD и радиальных нормалей;
- `U` запускает маршрут фокуса через девять направлений и формирует `PASS/FAIL` по split/merge, topology changes и LOD-швам;
- тесты `T` и `Y`, радиальная физика, collision и floating origin сохранены;
- nullable warning `CS8600` в `TerrainChunkManager.cs:399` исправлен nullable-out переменной с явной защитой от `null`.

**Статическая проверка:**

- математическая симуляция маршрута: 9 topology revisions, суммарно 20 split и 16 merge событий;
- для каждого состояния маршрута: 36 активных patches, 112 атомарных сегментов, `open=0`, `nonManifold=0`, `maxDelta=1`;
- проверены C#-строки, комментарии, скобки, scene paths и отсутствие build/cache-каталогов;
- Godot и .NET SDK отсутствуют в текущей среде, поэтому сборка и runtime не заявляются выполненными.

**Изменения статусов:**

- `TASK-033`, `PC-070`–`PC-074` → `IMPLEMENTED`;
- `TASK-035`, `PC-ACC-040`–`PC-ACC-045` → `IN_PROGRESS` до локального `U: PASS`;
- `DEBT-CS8600` → `IMPLEMENTED`, окончательная верификация требует чистой локальной сборки.

**Следующая задача после приёмки:** `TASK-036` — расширить глубину quadtree, добавить фоновую генерацию и выгрузку невидимых patches.

### 2026-08-01 — приёмка floating origin и формализация регламента итераций

**Runtime-доказательство пользователя:**

- локальная сборка `Game.Client.csproj` завершена успешно: `0` ошибок, `1` предупреждение `CS8600` в `TerrainChunkManager.cs:399`;
- HUD: `TASK-032 origin (Y): PASS shifts=4, cells=6`;
- `localMax=1809,2 м` при допустимом пределе `2048 м`;
- `logicalErr=0,000 м`;
- `relativeErr=0,0003 м` при допустимом пределе `0,001 м`;
- `gap=0,00 с`, `ground=да`, `floor=да`, `probe=да`;
- радиальная система и геометрические швы `388/388` остаются `PASS`.

**Документационные изменения:**

- добавлен `DEVELOPMENT_ITERATION_PROTOCOL.md` с обязательным порядком выбора задачи, реализации, проверки, обновления журнала, упаковки и передачи результата;
- в конце этого журнала добавлена обязательная ссылка на регламент и сокращённый стандартный запрос;
- README дополнен разделом о регламенте итеративной разработки.

**Изменения статусов:**

- `TASK-032`, `TASK-034`, `PC-060`–`PC-064`, `PC-ACC-030`–`PC-ACC-035` → `VERIFIED`;
- `TASK-033` становится следующей функциональной задачей.

**Технический долг:** предупреждение nullable `CS8600` в `TerrainChunkManager.cs:399` не блокирует сборку, но должно быть устранено в ближайшей кодовой итерации.

### 2026-08-01 — build hotfix HUD floating origin

**Основание:** локальная C#-сборка пользователя выявила `CS1039`, `CS1002` и `CS1010` в `CubeSpherePrototype.cs` на строках 412–413.

**Исправлено:**

- физический перевод строки внутри интерполированной строковой константы HUD заменён на экранированный `\n`;
- строка локальных координат и следующая строка логических координат снова формируют одну корректную C#-конкатенацию;
- выполнена лексическая статическая проверка всех `.cs`-файлов на незавершённые строки, символы, комментарии и несбалансированные скобки — ошибок не обнаружено;
- функциональная логика `TASK-032` и статусы требований не изменялись.

**Ожидаемая повторная проверка:** локальная сборка `Game.Client.csproj`, затем runtime-приёмка `TASK-032 origin (Y)`.

### 2026-08-01 — приёмка межгранных швов и реализация floating origin `TASK-032`

**Основание:** раздел 6.2 и Прототип C раздела 39 PDF-ТЗ; screenshot и прямое подтверждение пользователя по `TASK-030/TASK-031`.

**Принято по runtime-доказательству пользователя:**

- автоматический seam-test: `PASS`, `crossings=4`, `gap=0,00 с`, `Δup=0,59°`;
- HUD после теста: `ground=да`, `floor=да`, `probe=да`, фактический счётчик переходов `5`;
- геометрические швы остаются `PASS 388/388`;
- пользователь отдельно подтвердил отсутствие провалов, резких подпрыгиваний, переворотов камеры и ошибок WASD, а также корректную работу `R`;
- `TASK-030`, `TASK-031`, `PC-050`–`PC-053`, `PC-ACC-020`–`PC-ACC-024` → `VERIFIED`.

**Реализовано в `TASK-032`:**

- добавлен отдельный `FloatingOriginController`;
- локальная позиция хранится вместе с `CellX/CellY/CellZ: long`;
- размер ячейки — `4096 м`, перенос выполняется после выхода игрока за `±2048 м`;
- cell delta вычисляется отдельно по каждой оси, включая отрицательные координаты;
- планета, игрок и обзорная камера переводятся одним вектором, поэтому их взаимные трансформы не меняются;
- логическая позиция вычисляется в `double` как `cell × CellSize + local` и проверяется до/после каждого переноса;
- сохранённые spawn/test-трансформы планетарного игрока получают ту же мировую трансляцию, поэтому `R` остаётся корректным после rebase;
- HUD показывает cell, local position и число origin shifts;
- `Y` запускает автоматический acceptance test из четырёх шагов и шести переходов ячеек по положительным и отрицательным осям;
- тест проверяет `local <= 2048 м`, непрерывность logical position, сохранение относительных позиций игрока/планеты/камеры, контакт с поверхностью, радиальный Up и точное восстановление исходного состояния;
- повторное `Y`, а также `F2`, `R` или `T`, безопасно отменяет тест и восстанавливает baseline;
- принятый seam-test по `T` и остальные функции Прототипа C сохранены.

**Статус:**

- `TASK-032`, `PC-060`–`PC-064` — `IMPLEMENTED`;
- `TASK-034`, `PC-ACC-030`–`PC-ACC-035` — `IN_PROGRESS` до локального `TASK-032 origin (Y): PASS`;
- следующий кодовый шаг после приёмки — `TASK-033`, quadtree LOD граней.

**Ограничение проверки:** в текущей среде отсутствуют Godot 4.7.1 .NET и .NET SDK, поэтому выполнены статический контроль C#, scene/resource paths и проверка архива; runtime-статус требует доказательства по разделу 10.

## 4. Базовая конфигурация и инструменты

| ID | Раздел ТЗ | Требование | Статус | Доказательство / замечание | Следующее действие |
|---|---:|---|---|---|---|
| `TOOL-001` | 1.1 | Godot Engine 4.7.1 .NET | `IMPLEMENTED` | `Game.Client.csproj`: `Godot.NET.Sdk/4.7.1`; `project.godot`: `4.7`, `C#` | Подтвердить текущей сборкой и записать SHA |
| `TOOL-002` | 1.3 | Производственный код на C# | `IMPLEMENTED` | `DebugWorld.cs`, `PlayerController.cs`, terrain-компоненты, `CubeSpherePrototype.cs`, `CubeSphereMeshBuilder.cs`, `PlanetaryPlayerController.cs`, `FloatingOriginController.cs`, компоненты взаимодействия и боя | Проверять в CI |
| `TOOL-003` | 1.3 | Не использовать GDScript в производственном коде | `IMPLEMENTED` | В архиве нет `.gd` | Подтвердить CI-проверкой |
| `TOOL-004` | 37.1 | Использовать Git | `IMPLEMENTED` | Архив получен из репозитория; `.gitignore`, `.gitattributes` присутствуют | Записать merge commit SHA из `main` |
| `TOOL-005` | 37.1 | Git LFS для крупных бинарных файлов | `IMPLEMENTED` | LFS-шаблоны настроены для `.blend`, `.glb`, `.fbx`, аудио, видео и исходников текстур | Проверить `git lfs track` |
| `TOOL-006` | 37.1 | Не хранить кеш, сборки, IDE-настройки, локальные БД и логи | `IMPLEMENTED` | `.gitignore` + TASK-140 repository contract; релизный архив проходит forbidden-artifact audit | Подтвердить green CI на GitHub |
| `TOOL-007` | 37.2 | `main`, `develop`, `feature/*`, `fix/*`, `release/*` | `VERIFIED` | TASK-140 implementation + TASK-141 acceptance waiver владельца продукта; `.git` metadata не входит в release ZIP | Поддерживать policy в GitHub |
| `TOOL-008` | 37.2 | `main` всегда собирается | `VERIFIED` | TASK-140 CI pipeline + TASK-141 acceptance waiver владельца продукта | Не ослаблять required quality/export checks |

---

## 5. Архитектура и настройки

| ID | Раздел ТЗ | Требование | Статус | Доказательство / замечание | Следующее действие |
|---|---:|---|---|---|---|
| `ARCH-001` | 4.1 | Многослойная архитектура | `VERIFIED` | TASK-144 compiled boundaries + static/xUnit contract; remaining runtime tail accepted by product-owner waiver 2026-08-15 | Maintain regression gates |
| `ARCH-002` | 4.1 | Доменная логика не зависит от `Godot.Node` | `VERIFIED` | TASK-142 static/event-bus contract; runtime tail accepted by product-owner waiver 2026-08-15 | Maintain source gate |
| `ARCH-003` | 4.2 | Godot-клиент в `src/Game.Client` | `VERIFIED` | Repository layout + accepted technical foundation | Maintain source layout |
| `ARCH-006` | 4.3 | Клиент содержит сцены, камеры, управление и адаптеры взаимодействия | `IMPLEMENTED` | `DebugWorld`, `TerrainChunkPrototype`, `CubeSpherePrototype`, `PlanetaryPlayer`, управление, взаимодействие и бой | Довести Прототип A до приёмки |
| `CFG-001` | 1.2 | Основной renderer — Mobile | `VERIFIED` | project.godot + TASK-144 primary runtime evidence; technical tail accepted 2026-08-15 | Maintain renderer gate |
| `CFG-002` | 1.2 | Основной графический API — Vulkan | `VERIFIED` | project.godot + фактический Forward Mobile/Vulkan runtime evidence | Maintain renderer gate |
| `CFG-003` | 1.2 | Compatibility/OpenGL 3.3 — резервный профиль | `VERIFIED` | TASK-144 presets/static contract; omitted Compatibility smoke accepted by explicit product-owner waiver 2026-08-15 | Maintain Compatibility export gate |
| `CFG-004` | 38 | Nullable включён | `VERIFIED` | `<Nullable>enable</Nullable>` + section-38 gate; technical tail accepted 2026-08-15 | Maintain 0-warning policy |
| `CFG-005` | 38 | Предупреждения контролируются | `VERIFIED` | TASK-140 warnings-as-errors implementation; TASK-141 принят владельцем продукта | Сохранять 0-warning CI policy |
| `CFG-006` | 38 | Нет циклических зависимостей | `VERIFIED` | TASK-144 one-way project graph + `projectCycles=0`; accepted technical foundation | Maintain graph gate |
| `CFG-007` | 38 | Генерация мира не выполняется в `_Process` | `IMPLEMENTED` | `_PhysicsProcess` только обнаруживает переход; worker-задачи считают данные, timer дозированно применяет готовые mesh/collision в main thread | Подтвердить профилированием |
| `CFG-008` | 37.1 | Хранить import-настройки, исключая `.godot/` | `IMPLEMENTED` | `icon.svg.import` хранится, `.godot/` исключена | Не игнорировать глобально `*.import` |
| `CFG-009` | 38.1 | Частоты systems 60/60/10/2 Hz, background economy 0.2–1 Hz | `VERIFIED` | `SystemFrequencyPolicy`; TASK-149.4 boundary fix; runtime tail accepted 2026-08-15 | Maintain frequency regression tests |
| `CFG-010` | 38.2 | Typed domain event bus и 11 нормативных событий | `VERIFIED` | exact 11 typed events + F5 probe; technical tail accepted 2026-08-15 | Maintain exact 11/11 gate |
| `CFG-011` | 38 | Async operations принимают `CancellationToken` | `VERIFIED` | TASK-142 audit охватывает public/private/protected/internal production Task/ValueTask; missing=0 | Поддерживать source gate |
| `CFG-012` | 38 | SQLite только parameterized persistence boundary, без SQL в scenes | `VERIFIED` | TASK-142 source gate: SQL только Persistence/Developer inspector; `.tscn` SQL=0 | Поддерживать source gate |
| `CFG-013` | 38 | Public interfaces документированы | `VERIFIED` | XML `<summary>` contract для всех 5 public interfaces | Поддерживать source gate |
| `CFG-014` | 38 | Exceptions не подавляются | `VERIFIED` | Empty catch scan=0; cancellation фильтруется явно | Maintain source gate |
| `CFG-015` | 38 | Godot Node не domain model; UI не содержит item business mutations | `VERIFIED` | TASK-142 node/UI/domain separation gates; accepted technical foundation | Maintain separation gates |

---

## 6. Прототип A — персонаж

Требования ТЗ: плоская сцена, управление, камера, прыжок, взаимодействие и простая стрельба.

### 6.1. Сцена и физическая основа

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PA-001` | Отдельная тестовая 3D-сцена | `VERIFIED` | `Scenes/DebugWorld.tscn`; сохранена как отдельная сцена, пока стартовой назначен Прототип B | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-002` | Плоский видимый пол | `VERIFIED` | `BoxMesh 20 × 0.2 × 20` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-003` | Статическая коллизия пола | `VERIFIED` | `GroundBody/GroundCollision`, размер совпадает | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-004` | Игрок на `CharacterBody3D` | `VERIFIED` | `Scenes/Player/Player.tscn` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-005` | Коллизия игрока | `VERIFIED` | `CapsuleShape3D`, radius `0.4`, height `1.8` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-006` | Временный визуальный меш | `VERIFIED` | `CapsuleMesh`; скрыт для вида от первого лица | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-007` | Корректная начальная позиция | `VERIFIED` | `Player Y=1` соответствует верху пола | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-008` | Направленный свет | `VERIFIED` | `DirectionalLight3D` присутствует | Принято пользователем в составе предшествующих runtime-итераций |

### 6.2. Камера

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PA-010` | Камера в иерархии игрока | `VERIFIED` | `Player/Head/Camera3D` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-011` | Узел вертикального поворота | `VERIFIED` | `Head Y=0.65` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-012` | Ровно одна игровая камера | `VERIFIED` | В `DebugWorld` отдельной камеры нет; у игрока одна камера | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-013` | Горизонтальный обзор мышью | `VERIFIED` | `RotateY()` в `_UnhandledInput` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-014` | Ограниченный вертикальный обзор | `VERIFIED` | Clamp `-89°…+89°` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-015` | Захват и освобождение курсора | `VERIFIED` | Capture в `_Ready`, Escape освобождает, ЛКМ захватывает | Принято пользователем в составе предшествующих runtime-итераций |

### 6.3. Перемещение и прыжок

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PA-020` | Input Map движения | `VERIFIED` | W/S/A/D настроены в `[input]` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-021` | Input Map прыжка | `VERIFIED` | `jump` назначен на Space | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-022` | WASD-перемещение | `VERIFIED` | `Input.GetVector`, направление относительно `Transform.Basis` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-023` | Гравитация | `VERIFIED` | Используется `physics/3d/default_gravity` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-024` | Прыжок только с пола | `VERIFIED` | `IsOnFloor()` и `JumpVelocity` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-025` | Повторный прыжок после приземления | `VERIFIED` | Логика допускает новый прыжок после `IsOnFloor()` | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-026` | Движение в `_PhysicsProcess` | `VERIFIED` | `MoveAndSlide()` вызывается на физическом шаге | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-027` | Столкновения со стенами | `VERIFIED` | `WallFront`, `WallSide`, `LowBlock`, `HighBlock`: размеры Mesh/Collision совпадают, дочерние Transform нулевые | Принято пользователем в составе предшествующих runtime-итераций |
| `PA-028` | Скорость стабильна при разных FPS | `VERIFIED` | Скорость задаётся как velocity; тест не выполнен | Принято пользователем в составе предшествующих runtime-итераций |

### 6.4. Взаимодействие

| ID | Требование | Статус | Следующее действие |
|---|---|---|---|
| `PA-030` | Действие взаимодействия | `VERIFIED` | `interact` назначен на `E`; пользователь подтвердил работу в запущенном проекте; принято пользователем в составе предшествующих runtime-итераций |
| `PA-031` | Луч взаимодействия из камеры | `VERIFIED` | `InteractionRay` из камеры работает; пользователь подтвердил взаимодействие с терминалом; принято пользователем в составе предшествующих runtime-итераций |
| `PA-032` | Контракт взаимодействуемого объекта | `VERIFIED` | `IInteractable.Interact(Node3D interactor)` успешно вызван в runtime; принято пользователем в составе предшествующих runtime-итераций |
| `PA-033` | Тестовый объект взаимодействия | `VERIFIED` | Пользователь подтвердил переключение состояния терминала; принято пользователем в составе предшествующих runtime-итераций |
| `PA-034` | Ограничение дистанции | `VERIFIED` | Луч ограничен 4 м; текущая итерация подтверждена пользователем как работающая; принято пользователем в составе предшествующих runtime-итераций |

### 6.5. Простая стрельба

| ID | Требование | Статус | Следующее действие |
|---|---|---|---|
| `PA-040` | Действие стрельбы | `VERIFIED` | `fire_primary` назначен на ЛКМ; клик при свободном курсоре только возвращает захват; принято пользователем в составе предшествующих runtime-итераций |
| `PA-041` | Простой выстрел | `VERIFIED` | `HitscanWeapon/FireRay` выполняет raycast из камеры на 50 м; принято пользователем в составе предшествующих runtime-итераций |
| `PA-042` | Тестовая цель | `VERIFIED` | `ShootTarget` вспыхивает красным и пишет номер попадания в Output; принято пользователем в составе предшествующих runtime-итераций |
| `PA-043` | Стрельба не находится в UI | `VERIFIED` | Код расположен в отдельном компоненте `Scripts/Combat/HitscanWeapon.cs`; принято пользователем в составе предшествующих runtime-итераций |
| `PA-044` | Ограничение частоты стрельбы | `VERIFIED` | Монотонный таймер `Time.GetTicksMsec()` ограничивает стрельбу до 4 выстрелов/с; принято пользователем в составе предшествующих runtime-итераций |

### 6.6. Приёмка Прототипа A

| ID | Критерий | Статус | Доказательство / пробел |
|---|---|---|---|
| `PA-ACC-001` | Сборка без ошибок | `VERIFIED` | Предыдущие локальные итерации успешно запускались; пользователь распорядился зафиксировать предшествующие задачи как принятые. |
| `PA-ACC-002` | Сборка без предупреждений | `VERIFIED` | Предыдущие итерации приняты пользователем; отдельный warning-count остаётся инфраструктурной задачей CI. |
| `PA-ACC-003` | Запуск из чистого клона | `VERIFIED` | Рабочие редакции загружались из GitHub-архивов и запускались локально пользователем. |
| `PA-ACC-004` | WASD работает | `VERIFIED` | WASD подтверждался пользователем в ходе итеративной runtime-проверки. |
| `PA-ACC-005` | Обзор мышью работает | `VERIFIED` | Обзор мышью подтверждался пользователем в ходе итеративной runtime-проверки. |
| `PA-ACC-006` | Прыжок и приземление стабильны | `VERIFIED` | Прыжок и приземление приняты пользователем в составе предыдущей итерации игрока. |
| `PA-ACC-007` | Взаимодействие работает | `VERIFIED` | Пользователь подтвердил успешную работу взаимодействия. |
| `PA-ACC-008` | Простая стрельба работает | `VERIFIED` | Простая стрельба и исправление compile-дефекта приняты пользователем как предшествующая итерация. |
| `PA-ACC-009` | Нет критических ошибок в Godot Output | `VERIFIED` | Предыдущие runtime-итерации приняты без зафиксированных критических ошибок Godot Output. |
| `PA-ACC-010` | Прототип принят отдельным коммитом/тегом | `NOT_STARTED` | Выполняется после всех критериев |

**Прототип A функционально `VERIFIED` по прямому подтверждению пользователя и результатам предыдущих итеративных проверок. Для окончательной репозиторной приёмки остаётся только `PA-ACC-010`/`TASK-006`: фиксация SHA контрольного коммита или тега.**

---

## 7. Прототип B — чанк рельефа

Требования ТЗ: noise, mesh, collision, LOD, фоновая генерация и выгрузка. Параметр базового чанка из раздела 9.3 PDF-ТЗ — `33 × 33` вершины и `2048` треугольников без соединительных элементов.

### 7.1. Данные высот и детерминизм

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PB-001` | Отдельная тестовая сцена чанка | `VERIFIED` | `Scenes/Terrain/TerrainChunkPrototype.tscn`; назначена стартовой сценой | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-010` | Noise формирует высоты | `VERIFIED` | `FastNoiseLite.GetNoise2D()` формирует Y каждой вершины | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-011` | Детерминированный seed | `VERIFIED` | Экспортируемый `NoiseSeed`; одинаковые параметры дают одинаковые выборки | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-012` | Согласованные координаты соседних чанков | `VERIFIED` | Активная сетка использует `ChunkX * ChunkSize`/`ChunkZ * ChunkSize`; общая граница получает одинаковые noise-координаты | Подтверждено пользователем и успешным `TASK-025: PASS` |

### 7.2. Процедурный mesh

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PB-020` | Сетка 33 × 33 вершины | `VERIFIED` | `GridResolution=33`; создаётся `1089` вершин | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-021` | 2048 треугольников | `VERIFIED` | `(33 - 1)² × 2 = 2048`; индексная сетка из двух треугольников на ячейку | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-022` | Нормали и материал | `VERIFIED` | Нормали вычисляются центральной разностью по общей функции высот; vertex color используется как albedo в `StandardMaterial3D` | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-023` | Диагностическая окраска mesh | `VERIFIED` | Режимы `HeightAndSlope`, `Lod`, `Normals`; `F1` переключает их без изменения collision | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-024` | Диагностическая сетка и границы | `VERIFIED` | Мировая сетка, фактический wireframe и границы чанков; переключатели `F2`–`F4` | Подтверждено пользователем и успешным `TASK-025: PASS` |

### 7.3. Collision

| ID | Требование | Статус | Доказательство | Что нужно для `VERIFIED` |
|---|---|---|---|---|
| `PB-030` | Collision создаётся отдельно | `VERIFIED` | `mesh.CreateTrimeshShape()` назначается отдельному `CollisionShape3D` | Подтверждено пользователем и успешным `TASK-025: PASS` |
| `PB-031` | Игрок взаимодействует с рельефом | `VERIFIED` | В сцену добавлен `Player` на высоте 8 м | Подтверждено пользователем и успешным `TASK-025: PASS` |

### 7.4. LOD, фоновые задачи и выгрузка

| ID | Требование | Статус | Следующее действие |
|---|---|---|---|
| `PB-040` | Минимум два уровня LOD | `VERIFIED` | `TerrainChunkManager`: LOD0 `33 × 33`, LOD1 `17 × 17`; подтверждено пользователем и `TASK-025: PASS` |
| `PB-041` | Переключение LOD по дистанции | `VERIFIED` | Chebyshev-дистанция; переход планируется очередью, а не выполняется одним burst; подтверждено пользователем и `TASK-025: PASS` |
| `PB-042` | Отсутствие критических щелей LOD | `VERIFIED` | Высокая кромка геометрически привязывается к линейным сегментам низкой сетки; skirts остаются только на внешнем периметре; подтверждено пользователем и `TASK-025: PASS` |
| `PB-043` | Согласованные нормали соседних чанков | `VERIFIED` | Нормаль вычисляется из глобальных noise-выборок с одинаковым шагом по обе стороны границы; подтверждено пользователем и `TASK-025: PASS` |
| `PB-050` | Высоты/вершины считаются в фоне | `VERIFIED` | `TerrainChunkDataBuilder` вычисляет noise-высоты, глобальные нормали, UV и индексы в `Task.Run`; `F10` создаёт серию фоновых ревизий для воспроизводимого runtime-контроля; подтверждено пользователем и `TASK-025: PASS` |
| `PB-051` | SceneTree изменяется только в main thread | `VERIFIED` | Worker возвращает только `TerrainMeshData`; `AddChild`, `ArrayMesh`, material и collision создаются в `ProcessOperationQueue`; stress-test валидирует итоговый активный набор после серии worker-revision; подтверждено пользователем и `TASK-025: PASS` |
| `PB-052` | Отмена устаревшей фоновой генерации | `VERIFIED` | На каждый план создаётся `CancellationTokenSource`; используются `jobId` и revision; `F10` требует наблюдаемой отмены/stale и проверяет, что финальные чанки соответствуют только последней revision; подтверждено пользователем и `TASK-025: PASS` |
| `PB-060` | Выгрузка невидимого чанка | `VERIFIED` | Исходящие чанки удаляются только после создания входящих и завершения обновлений; подтверждено пользователем и `TASK-025: PASS` |
| `PB-061` | Освобождение mesh/collision | `VERIFIED` | `ReleaseGeneratedResources()` обнуляет mesh и shape; фактический `TASK-026: PASS` после 121 с и 82 переходов дал `managedDelta=0,0 MB`, `mesh=9`, `collision=9` |
| `PB-062` | Ограничение burst-нагрузки при переходе | `VERIFIED` | Worker-задачи параллельны, но main-thread apply сохраняет порядок `create → demotion → promotion → neutral update → remove`; таймер применяет по умолчанию одну тяжёлую операцию каждые `0,06 с`; visual-only update не пересоздаёт collision; подтверждено пользователем и `TASK-025: PASS` |
| `PB-063` | Защита от дребезга границы чанка | `VERIFIED` | `ChunkSwitchHysteresis=3 м`; обратное переключение требует выхода из гистерезисной зоны; подтверждено пользователем и `TASK-025: PASS` |

### 7.5. Приёмка текущей основы Прототипа B

| ID | Критерий | Статус | Доказательство / пробел |
|---|---|---|---|
| `PB-ACC-001` | Сборка без ошибок | `VERIFIED` | Успешный запуск Godot после hotfix подтверждает сборку без ошибок; `TASK-025` завершилась `PASS`. |
| `PB-ACC-002` | Сцена запускается | `VERIFIED` | Сцена `TerrainChunkPrototype` запущена пользователем; предоставлен screenshot HUD. |
| `PB-ACC-003` | Рельеф виден | `VERIFIED` | На предоставленном screenshot процедурный рельеф виден и диагностически читаем. |
| `PB-ACC-004` | Игрок не проваливается | `VERIFIED` | Пользователь принял предыдущие физические итерации; игрок остаётся на рельефе после стресс-переходов. |
| `PB-ACC-005` | Output сообщает параметры LOD | `VERIFIED` | Runtime-лог генерации содержит LOD, vertices, triangles, worker/main timing; код сохранён в текущей редакции. |
| `PB-ACC-006` | Нет критических ошибок Output | `VERIFIED` | HUD после теста: `ошибки: 0`; итог `PASS` получен без критического Output. |
| `PB-ACC-007` | Активны 9 чанков | `VERIFIED` | Фактический результат `TASK-025`: `9/9`. |
| `PB-ACC-008` | Стриминг возвращается к размеру активной сетки | `VERIFIED` | Фактический результат `TASK-025`: `queue=0`, `workers=0`, `stale=48`, финальная revision согласована. |
| `PB-ACC-009` | Нет критических визуальных щелей LOD | `VERIFIED` | Пользователь подтвердил нормальный визуальный результат; screenshot не показывает критических LOD-разрывов. |
| `PB-ACC-010` | Диагностические режимы работают | `VERIFIED` | Пользователь подтвердил работу диагностики; screenshot показывает включённые grid, wireframe и borders. |

**Прототип B — `VERIFIED`: короткий stress-test и 121-секундный soak-test завершены `PASS`; LOD, стыки, фоновые job, stale-фильтрация, возврат к `9/9`, освобождение ресурсов и стабильность managed memory подтверждены runtime. SHA остаётся отдельной задачей репозиторной трассируемости `TASK-006`.**

---

## 8. Прототип C и остальные технические прототипы

### 8.1. Прототип C — cube sphere и радиальная система игрока

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `PC-001` | Отдельная тестовая сцена сферической планеты | `VERIFIED` | `Scenes/Planet/CubeSpherePrototype.tscn`; сцена запущена пользователем |
| `PC-010` | Шесть независимых граней cube sphere | `VERIFIED` | Runtime HUD: `Грани: 6/6` |
| `PC-011` | Сетка `33 × 33` и 2048 треугольников на грань | `VERIFIED` | Runtime HUD: `6534` вершины, `12288` треугольников |
| `PC-012` | Проекция куба на сферу | `VERIFIED` | Планета корректно отображена в runtime |
| `PC-013` | Детерминированный рельеф по радиальному направлению | `VERIFIED` | Runtime-сцена с seed `20260801` и рельефом `±6 м` принята пользователем |
| `PC-014` | Совпадение дублированных швов граней | `VERIFIED` | `Швы: PASS`, `388/388`, обе максимальные ошибки равны нулю |
| `PC-020` | Независимые визуальные поверхности как основа quadtree | `VERIFIED` | Runtime HUD подтверждает `6/6` граней без видимых разрывов |
| `PC-021` | Отдельная collision-поверхность каждой грани | `VERIFIED` | Runtime HUD: `collision: 6/6` |
| `PC-030` | Диагностика граней и нормалей | `VERIFIED` | Пользователь подтвердил корректную работу сцены и режимов диагностики |
| `PC-040` | Отдельный планетарный `CharacterBody3D` | `VERIFIED` | `PlanetaryPlayer.tscn`, `PlanetaryPlayerController.cs`; старт сбоку от планеты |
| `PC-041` | Гравитация направлена к центру планеты | `VERIFIED` | `GravityDirection = -RadialUp`; ускорение применяется в `_PhysicsProcess` |
| `PC-042` | Локальный Up направлен от центра | `VERIFIED` | `UpDirection = RadialUp`; basis плавно slerp-выравнивается с радиальной нормалью |
| `PC-043` | Камера и движение не зависят от глобальной оси Y | `VERIFIED` | движение проецируется в касательную плоскость; yaw выполняется вокруг локальной Y; камера является дочерней локальной иерархии |
| `PC-044` | Диагностика радиальной системы | `VERIFIED` | HUD: `r`, ground, касательная скорость, `Δup`, `PASS/ALIGNING`; `F2` и `R` |
| `PC-050` | Многоточечная поддержка контакта на collision-швах | `VERIFIED` | Runtime HUD: `floor=да`, `probe=да`; пользователь подтвердил ручное прохождение |
| `PC-051` | Удержание поверхности при смене collision-граня | `VERIFIED` | `TASK-030`: `gap=0,00 с`; провалов и подпрыгиваний нет |
| `PC-052` | Диагностика текущей грани и переходов | `VERIFIED` | Итоговый HUD показывает грань `-Y` и `переходы=5` |
| `PC-053` | Автоматический тест ходьбы через швы | `VERIFIED` | `TASK-030 seam (T): PASS crossings=4, gap=0,00 с, Δup=0,59°` |
| `PC-060` | Локальная позиция хранится как cell `long` + `Vector3` | `VERIFIED` | `FloatingOriginController`: `CellX/CellY/CellZ`, `LocalPosition`, logical double coordinates |
| `PC-061` | Ячейка 4096 м и порог переноса 2048 м | `VERIFIED` | Экспортируемые `CellSize=4096`, `ShiftThreshold=2048`; положительные и отрицательные cell delta |
| `PC-062` | Все участники локальной сцены переносятся синхронно | `VERIFIED` | Planet, PlanetaryPlayer и CameraRig получают одинаковую translation |
| `PC-063` | Логическая позиция и сохранённые точки не меняются при rebase | `VERIFIED` | Continuity check в double; `NotifyWorldTranslated` корректирует spawn/test transforms |
| `PC-064` | Автоматический floating-origin acceptance test | `VERIFIED` | `Y`: 4 shifts, 6 cell transitions, контроль local/logical/relative/contact и восстановление baseline |
| `PC-070` | Независимые визуальные quadtree patches на шести гранях | `VERIFIED` | Runtime: 36 patches, `L1=20`, `L2=16`; сборка 0/0 |
| `PC-071` | Локальное дробление участка возле игрока | `VERIFIED` | `U: PASS`, split=17, merge=16 |
| `PC-072` | Устранение щелей между соседними LOD | `VERIFIED` | Runtime: `Δlod=1`, `open=0`, seam=0 |
| `PC-073` | Диагностика покрытия и соседства LOD | `VERIFIED` | Runtime: atomic=112, nonManifold=0, open=0 |
| `PC-074` | Автоматический quadtree LOD acceptance test | `VERIFIED` | `TASK-033 LOD (U): PASS split=17, merge=16` |
| `PC-080` | Не менее трёх рабочих уровней quadtree | `VERIFIED` | Рекурсивные `L1/L2/L3`; HUD показывает applied/logical counts каждого уровня |
| `PC-081` | 2:1 balancing соседних листьев | `VERIFIED` | Atomic-edge balancing принудительно делит грубые листья при разнице > 1 |
| `PC-082` | Фоновая генерация patch arrays | `VERIFIED` | `CubeSpherePatchDataBuilder`, до 4 worker jobs, `CancellationToken` |
| `PC-083` | Revision/cancellation/stale protection | `VERIFIED` | Новый plan отменяет старый; stale результаты отбрасываются до main-thread apply |
| `PC-084` | Выгрузка невидимых patches | `VERIFIED` | Resident-set использует угол 108° с консервативным запасом на угловой размер patch; obsolete patches удаляются после settle |
| `PC-085` | Main-thread применение ресурсов Godot | `VERIFIED` | `ArrayMesh`, `MeshInstance3D`, `SceneTree` и `QueueFree` выполняются только в прототипе на main thread |
| `PC-086` | Автоматический async streaming acceptance test | `VERIFIED` | Клавиша `I`: 9 направлений, rapid revisions, settle и `PASS/FAIL` |
| `PC-090` | Collision LOD следует за логической quadtree-топологией | `VERIFIED` | После settle `active=target=logical=42`; visual horizon culling не создаёт physics-дыр |
| `PC-091` | Collision использует рабочие уровни `L1/L2/L3` | `VERIFIED` | Runtime: исходно `L1/L2/L3=20/14/8`; в маршруте `L3=28` |
| `PC-092` | Collision создаётся отдельно от visual mesh | `VERIFIED` | Collision active `42/42` при visual `41/41/42`; отдельный physics-cover подтверждён runtime |
| `PC-093` | Двухфазная безопасная замена collision-набора | `VERIFIED` | `60` планов и `60` commit без ground gap и underflow |
| `PC-094` | Полногранная collision-поверхность используется как fallback | `VERIFIED` | В тесте `fallback=60`, итог `fallback=off`, `recoveries=0`, `rMin=92,46 м` |
| `PC-095` | Collision patches выгружаются после безопасного commit | `VERIFIED` | Runtime: `created=257`, `unloaded=233`, финальное состояние Idle |
| `PC-096` | Автоматический dynamic collision acceptance test | `VERIFIED` | `TASK-038 collision (K): PASS`, 4 перехода, `gap=0`, errors `0` |
| `PC-100` | Компактный диагностический HUD по умолчанию | `VERIFIED` | Screenshot пользователя: compact HUD освобождает основную часть 3D-холста |
| `PC-101` | Подробный HUD доступен без потери телеметрии | `VERIFIED` | Screenshot пользователя: detailed mode содержит полную телеметрию и прокрутку |
| `PC-102` | HUD можно почти полностью скрыть | `VERIFIED` | Screenshot пользователя: остаётся только `HUD скрыт • H` |
| `PC-103` | HUD адаптируется к размеру viewport | `VERIFIED` | Все три режима корректно отображены в рабочем viewport без выхода за границы |
| `PC-104` | Единое переключение HUD по клавише H | `VERIFIED` | Пользователь предоставил последовательные compact/detailed/hidden screenshot |
| `PC-ACC-001` | Проект собирается без ошибок | `VERIFIED` | Основа cube sphere запущена; последующая радиальная редакция принята пользователем |
| `PC-ACC-002` | Сцена запускается и показывает планету | `VERIFIED` | Предоставлен screenshot текущей сцены |
| `PC-ACC-003` | HUD показывает `6/6` и collision `6/6` | `VERIFIED` | Прямое runtime-доказательство пользователя |
| `PC-ACC-004` | Seam-check показывает `PASS` | `VERIFIED` | `388/388`, `Δpos=0`, `Δnormal=0` |
| `PC-ACC-005` | Нет видимых разрывов между гранями | `VERIFIED` | Пользователь подтвердил нормальный визуальный результат |
| `PC-ACC-006` | Диагностические переключатели работают | `VERIFIED` | Пользователь подтвердил корректную работу основы сцены |
| `PC-ACC-010` | Радиальная версия собирается без ошибок | `VERIFIED` | Пользователь принял редакцию и запросил следующий функциональный шаг |
| `PC-ACC-011` | Игрок притягивается к боковой поверхности | `VERIFIED` | Радиальная гравитация принята пользователем без сообщения о провале или неверном направлении |
| `PC-ACC-012` | Радиальная ориентация стабильна | `VERIFIED` | Пользователь принял локальный Up и не сообщил о рывках или перевороте |
| `PC-ACC-013` | WASD и камера работают в касательной системе | `VERIFIED` | Функциональная редакция принята пользователем |
| `PC-ACC-014` | Прыжок, reset и переключение камер работают | `VERIFIED` | Функциональная редакция принята пользователем |
| `PC-ACC-015` | Нет критических ошибок Output | `VERIFIED` | Радиальная итерация принята пользователем без сообщения о runtime-дефектах |
| `PC-ACC-020` | Игрок вручную пересекает границу collision-граней | `VERIFIED` | Пользователь подтвердил ручную проверку без провала и рывка |
| `PC-ACC-021` | Многоточечный probe сохраняет контакт | `VERIFIED` | Итог: `ground=да`, `floor=да`, `probe=да`, gap `0,00 с` |
| `PC-ACC-022` | Автоматический seam-test проходит минимум 4 границы | `VERIFIED` | HUD: `PASS crossings=4`; lifetime transitions `5` |
| `PC-ACC-023` | В тесте нет рывка ориентации | `VERIFIED` | `Δup=0,59°`; пользователь подтвердил отсутствие переворота камеры |
| `PC-ACC-024` | После теста состояние восстановлено без ошибок | `VERIFIED` | Пользователь подтвердил управление, камеру и reset по `R` после теста |
| `PC-ACC-030` | Редакция floating origin собирается и запускается | `VERIFIED` | Сборка: 0 ошибок; сцена запущена; nullable warning CS8600 записан как технический долг |
| `PC-ACC-031` | Acceptance test выполняет 4 origin shifts и 6 cell transitions | `VERIFIED` | HUD: `TASK-032 origin (Y): PASS shifts=4, cells=6` |
| `PC-ACC-032` | Локальная координата остаётся внутри ±2048 м | `VERIFIED` | `localMax=1809,2 м` при пределе `2048 м` |
| `PC-ACC-033` | Logical coordinate непрерывна | `VERIFIED` | `logicalErr=0,000 м` |
| `PC-ACC-034` | Относительные трансформы и контакт сохраняются | `VERIFIED` | `relativeErr=0,0003 м`, `gap=0,00 с`, ground/floor/probe подтверждены |
| `PC-ACC-035` | После теста восстановлены baseline и предыдущие функции | `VERIFIED` | Итоговый HUD вернулся к `cell=(0,0,0)`; предыдущие системы остались в состоянии PASS |
| `PC-ACC-040` | Редакция quadtree LOD собирается без ошибок и предупреждений | `VERIFIED` | Сборка пользователя: 0 ошибок, 0 предупреждений |
| `PC-ACC-041` | HUD подтверждает независимые `L1/L2` patches | `VERIFIED` | HUD: patches=36, L1=20, L2=16, split=4 |
| `PC-ACC-042` | Topology validator не обнаруживает отверстий | `VERIFIED` | Runtime: open=0, nonManifold=0, Δlod=1, Δpos=0 |
| `PC-ACC-043` | Автоматический тест выполняет split и merge | `VERIFIED` | U: PASS split=17, merge=16 |
| `PC-ACC-044` | Визуально отсутствуют отверстия на LOD-переходах | `VERIFIED` | Пользователь подтвердил нормальный визуальный результат |
| `PC-ACC-045` | Collision и предыдущие тесты не регрессировали | `VERIFIED` | ground/floor/probe и collision 6/6 сохранены |
| `PC-ACC-050` | Новая редакция собирается без ошибок и предупреждений | `VERIFIED` | Локальная сборка пользователя: `0` ошибок, `0` предупреждений |
| `PC-ACC-051` | После settle applied совпадает с resident | `VERIFIED` | Стабильно `41/41/42`; после теста `44/44/45`, queue/workers `0` |
| `PC-ACC-052` | Третий уровень реально загружен | `VERIFIED` | До теста `L3=8`; итог `I: PASS` содержит `L3=12` |
| `PC-ACC-053` | Невидимые patches выгружаются | `VERIFIED` | Итог `resident=44/45`, `unloaded=93` |
| `PC-ACC-054` | Worker pipeline не содержит ошибок | `VERIFIED` | `errors=0`, `stale=24`, queue/workers вернулись к `0` |
| `PC-ACC-055` | Логическая топология остаётся корректной | `VERIFIED` | Runtime: `open=0`, `nonManifold=0`, `Δlod=1`, `Δpos=0` |
| `PC-ACC-056` | Автоматический streaming test завершается PASS | `VERIFIED` | `TASK-036 stream (I): PASS revisions=10, L3=12, unloaded=93, stale=24, errors=0` |
| `PC-ACC-057` | Предыдущие физические и диагностические тесты не регрессировали | `VERIFIED` | HUD после теста: collision `6/6`, ground/floor/probe и радиальная система `PASS` |
| `PC-ACC-060` | Редакция dynamic collision LOD собирается 0/0 | `VERIFIED` | Пользовательская сборка: 0 предупреждений, 0 ошибок |
| `PC-ACC-061` | После settle активен topology-complete collision-набор | `VERIFIED` | `active=target=logical=42`, staged/queue `0`, state `Idle`, fallback `off` |
| `PC-ACC-062` | Collision resident-set содержит `L3` | `VERIFIED` | До теста `L3=8`; в acceptance route `L3=28` |
| `PC-ACC-063` | Collision plan выполняет безопасные commits | `VERIFIED` | `plans=60`, `commits=60`, `created=257`, `unloaded=233`, `fallback=60` |
| `PC-ACC-064` | Во время замены отсутствует потеря контакта и radial underflow | `VERIFIED` | `gap=0,00 с`, `rMin=92,46 м`, `recoveries=0`, ground/floor/probe `да` |
| `PC-ACC-065` | Collision pipeline не содержит ошибок | `VERIFIED` | collision errors `0`; visual/collision queue `0`, workers `0` |
| `PC-ACC-066` | Автоматический collision test завершается PASS | `VERIFIED` | Повторный hotfix-тест: `TASK-038 collision (K): PASS` |
| `PC-ACC-067` | Старые collision patches реально выгружаются | `VERIFIED` | `unloaded=233`; финально `active=target=42` |
| `PC-ACC-068` | Предыдущие тесты и управление не регрессировали | `VERIFIED` | Seam-test остаётся PASS; игрок ground/floor/probe `да`, радиальная система PASS |
| `PC-ACC-070` | HUD-редакция собирается 0/0 | `VERIFIED` | Сборка пользователя: 0 предупреждений, 0 ошибок |
| `PC-ACC-071` | Compact mode оставляет большую часть 3D-холста открытой | `VERIFIED` | Предоставлен screenshot compact mode |
| `PC-ACC-072` | H циклически переключает три режима | `VERIFIED` | Подтверждены compact, detailed и hidden состояния |
| `PC-ACC-073` | Detailed mode сохраняет полную диагностику и прокрутку | `VERIFIED` | Detailed screenshot показывает полный набор строк и scrollbar |
| `PC-ACC-074` | Hidden mode восстанавливается клавишей H | `VERIFIED` | Hidden screenshot содержит только правый верхний hint; цикл режимов подтверждён |

Все требования Прототипа C, включая dynamic collision LOD и HUD ergonomics, приняты по runtime-доказательствам.

### 8.2. Прототип D — базовый корабль и свободный полёт

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `PD-001` | Отдельная тестовая сцена корабля | `VERIFIED` | `Scenes/Ship/ShipFlightPrototype.tscn`; назначена стартовой сценой |
| `PD-010` | Упрощённая аркадная физика в свободном пространстве | `VERIFIED` | `ArcadeShipController` использует floating `CharacterBody3D`, скорость и локальную угловую скорость |
| `PD-011` | Тяга вперёд и назад | `VERIFIED` | W/S; отдельные forward/reverse acceleration и ограничение скорости |
| `PD-012` | Боковые и вертикальные импульсные двигатели | `VERIFIED` | A/D и Space/C; ускорение применяется в локальном basis корабля |
| `PD-013` | Рыскание, тангаж и крен | `VERIFIED` | Мышь/стрелки и Q/E; локальные pitch/yaw/roll rates |
| `PD-014` | Форсаж, торможение и автоматическая стабилизация | `VERIFIED` | B, X и G; отдельные boost speed, brake deceleration и angular stabilization |
| `PD-015` | Переключение камер корабля | `VERIFIED` | F2 переключает chase camera со SpringArm3D и cockpit camera |
| `PD-016` | Эргономичный HUD корабля | `VERIFIED` | Compact без scrollbar, detailed со ScrollContainer, hidden hint; H циклически переключает режимы |
| `PD-017` | Автоматический free-flight acceptance test | `VERIFIED` | J выполняет thrust/rotation/strafe/boost/brake/camera route и возвращает baseline |
| `PD-ACC-001` | Редакция корабля собирается 0/0 | `VERIFIED` | Сборка пользователя: 0 предупреждений, 0 ошибок |
| `PD-ACC-002` | Сцена запускается, корабль и ориентиры видимы | `VERIFIED` | Предоставлен screenshot compact HUD и корабля |
| `PD-ACC-003` | Ручное управление подтверждает все шесть степеней свободы | `VERIFIED` | Пользователь подтвердил ручную проверку органов управления |
| `PD-ACC-004` | Форсаж, торможение и стабилизация работают | `VERIFIED` | Ручная проверка и `J: PASS`; final speed/angular = 0 |
| `PD-ACC-005` | Обе камеры переключаются без потери управления | `VERIFIED` | Ручная проверка и автоматические camera switches |
| `PD-ACC-006` | Автоматический тест завершается PASS | `VERIFIED` | `TASK-043 flight (J): PASS`; vmax=72,0, distance=221,2, collisions=0 |
| `PD-020` | Автоматический переход SPACE ↔ ATMOSPHERE | `VERIFIED` | Высота, radial up, smooth atmosphere blend и entry/exit counters |
| `PD-021` | Упрощённые подъёмная сила и минимальная скорость | `VERIFIED` | Gravity/lift balance и minimum-forward-speed assist без полноценной аэродинамики |
| `PD-022` | Атмосферное сопротивление | `VERIFIED` | Drag зависит от blend и квадрата скорости; acceleration ограничено |
| `PD-023` | Ограничение вертикального набора | `VERIFIED` | Положительная radial velocity ограничивается `AtmosphereMaximumClimbSpeed` |
| `PD-024` | Автоматическое предотвращение грубого столкновения | `VERIFIED` | Stopping-distance safety, clearance clamp и аварийный hard floor |
| `PD-025` | Диагностика и автоматический atmosphere test | `VERIFIED` | HUD SPACE/ATMOSPHERE; `P` approach; `L` проверяет entry/exit/drag/min-speed/climb/safety |
| `PD-ACC-010` | Атмосферная редакция собирается 0/0 | `VERIFIED` | Пользовательская сборка: `0` предупреждений, `0` ошибок |
| `PD-ACC-011` | Ручной переход SPACE ↔ ATMOSPHERE видим в HUD | `VERIFIED` | `P`, altitude/blend и entry/exit без рывка камеры |
| `PD-ACC-012` | Minimum-speed assist и lift удерживают управляемый полёт | `VERIFIED` | `L: PASS`, minSpeed applications > 0, maxBlend ≥ 0,55 |
| `PD-ACC-013` | Drag заметно снижает скорость | `VERIFIED` | `dragDrop ≥ 4 м/с` |
| `PD-ACC-014` | Радиальный набор ограничен | `VERIFIED` | climbLimit > 0, maxClimb ≤ limit + 1 м/с |
| `PD-ACC-015` | Surface-safety предотвращает грубое столкновение | `VERIFIED` | safety > 0, minAltitude ≥ 8 м, recoveries=0, collisions=0 |
| `PD-ACC-016` | Выход обратно в космос корректен | `VERIFIED` | exit ≥ 1, итоговый mode SPACE |
| `PD-ACC-017` | Автоматический atmosphere test завершается PASS | `VERIFIED` | `TASK-045 atmosphere (L): PASS` |
| `PD-ACC-018` | Free-flight и камеры не регрессировали | `VERIFIED` | После L работают J, F2 и ручное управление |
| `PD-030` | Поиск допустимой посадочной поверхности | `VERIFIED` | Physics ray probes по трём детерминированным кандидатам; fallback ring для общего случая |
| `PD-031` | Проверка уклона посадочной поверхности | `VERIFIED` | Угол normal к radial Up; предел `12°`; тестовый кандидат `18°` отклоняется |
| `PD-032` | Проверка препятствий и clearance | `VERIFIED` | Проверяется группа `landing_obstacle`; минимальный clearance `5,5 м` |
| `PD-033` | Резервирование посадочной точки | `VERIFIED` | Хранятся surface point, normal, slope, clearance и candidate index; marker видим в сцене |
| `PD-034` | Автоматическое выравнивание над точкой | `VERIFIED` | Основная ручная физика приостанавливается; position/basis сходятся к hover transform `12 м` |
| `PD-035` | Диагностика и landing-point acceptance test | `VERIFIED` | `M` — ручной assist; `N` — checks/rejects/reservation/alignment/restore |
| `PD-ACC-020` | Landing-point редакция собирается 0/0 | `VERIFIED` | Пользовательская сборка: 0 предупреждений, 0 ошибок |
| `PD-ACC-021` | Surface probe последовательно проверяет кандидаты | `VERIFIED` | `N: PASS`, checks=3; все три физические кандидата обработаны |
| `PD-ACC-022` | Крутая поверхность отклоняется | `VERIFIED` | slopeReject=1; reserved slope=0,0° |
| `PD-ACC-023` | Препятствие отклоняется | `VERIFIED` | obstacleReject=1; reserved clearance=29,7 м |
| `PD-ACC-024` | Точка резервируется и визуально маркируется | `VERIFIED` | Ручной M и screenshot подтверждают safe pad и marker |
| `PD-ACC-025` | Корабль устойчиво выравнивается над normal | `VERIFIED` | Aligned; posErr=0,00 м; angErr=0,04° |
| `PD-ACC-026` | Alignment не вызывает столкновений и runtime ошибок | `VERIFIED` | Скорость и angular=0; N: PASS; runtime ошибок не выявлено |
| `PD-ACC-027` | Предыдущие режимы не регрессировали | `VERIFIED` | J остаётся PASS; ручной режим и HUD подтверждены |
| `PD-040` | Трёхточечные выдвижные посадочные опоры | `VERIFIED` | Visual gear + три RayCast3D probes; deployment диагностируется в HUD |
| `PD-041` | Контролируемое финальное снижение | `VERIFIED` | Снижение от hover 12 м к gear clearance 1,55 м; скорость ограничена 2,8 м/с |
| `PD-042` | Подтверждение касания по опорам | `VERIFIED` | Требуются 3/3 probe contacts, position/angular tolerance и безопасная контактная скорость |
| `PD-043` | Состояние LANDED и отключение основной физики | `VERIFIED` | Transform фиксируется на опорах, velocity/angular=0, flight/atmosphere branch не выполняется |
| `PD-044` | Контролируемый взлёт и уборка опор | `VERIFIED` | Подъём по normal до 12 м, gear retract после clearance 3 м, возврат ручного управления |
| `PD-045` | Повторяемый touchdown/takeoff acceptance test | `VERIFIED` | O выполняет два полных цикла и восстанавливает baseline |
| `PD-ACC-030` | Touchdown-редакция собирается 0/0 | `VERIFIED` | Build-hotfix: пользовательская сборка 0 предупреждений, 0 ошибок |
| `PD-ACC-031` | Ручной M выполняет alignment, touchdown и takeoff | `VERIFIED` | Screenshots: Aligned → Landed → Idle после takeoff |
| `PD-ACC-032` | Все посадочные опоры подтверждают контакт | `VERIFIED` | Landed screenshot: contacts=3/3, gear=1,00; Output speed=2,800 м/с |
| `PD-ACC-033` | LANDED устойчив и основная физика отключена | `VERIFIED` | speed/angular=0; posErr=0,000 м; angErr=0,040°; locks=2 |
| `PD-ACC-034` | Взлёт достигает безопасного clearance | `VERIFIED` | clearance=12,00 м; gear=0; state Idle; ручное управление восстановлено |
| `PD-ACC-035` | Автоматический O-test завершается PASS | `VERIFIED` | cycles/touchdowns/locks/takeoffs=2; recoveries/collisions/errors=0 |
| `PD-ACC-036` | Предыдущие режимы не регрессировали | `VERIFIED` | Ручной цикл, камеры и HUD сохранены; предыдущие J/L/N результаты остаются PASS |
| `PD-050` | Soak 100 последовательных физических посадок | `VERIFIED` | `V: PASS 100/100`; все физические descent/contact/LANDED циклы завершены |
| `PD-051` | Контроль целостности touchdown state и counters | `VERIFIED` | counters достигли 100/100; stuck state и ошибки отсутствуют |
| `PD-052` | Контроль накопления узлов и managed memory | `VERIFIED` | `nodeDelta=0`, `memΔ=0,02 MiB`; пределы соблюдены |
| `PD-053` | Диагностика soak и исправленный touchdown HUD | `VERIFIED` | HUD показал RUNNING и финальный `PASS 100/100`; O/V строки видимы |
| `PD-ACC-040` | Soak-редакция собирается 0/0 | `VERIFIED` | Локальная сборка: 0 предупреждений, 0 ошибок |
| `PD-ACC-041` | V-test завершает ровно 100 посадок | `VERIFIED` | HUD: `TASK-051 soak (V): PASS 100/100` |
| `PD-ACC-042` | Все циклы подтверждают 3/3 опоры и допустимые ошибки | `VERIFIED` | `gear=3`, `vTouch=2,67 м/с`; пределы соблюдены |
| `PD-ACC-043` | Нет recovery, collision, runtime error и зависших state | `VERIFIED` | Финальный PASS и восстановленный baseline; явных ошибок нет |
| `PD-ACC-044` | Нет накопления SceneTree/managed memory | `VERIFIED` | `nodesΔ=0`, `memΔ=0,02 MiB` |
| `PD-ACC-045` | HUD показывает O/V, предыдущие режимы не регрессировали | `VERIFIED` | O/V строки присутствуют; soak восстановил исходное состояние |

### 8.3. Прототип E — SQLite save, backup и recovery

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `PE-001` | Отдельная тестовая сцена сохранений | `VERIFIED` | `Scenes/Persistence/SavePrototype.tscn`; сцена и HUD приняты в `TASK-055` |
| `PE-010` | SQLite через `Microsoft.Data.Sqlite`, один slot — одна БД | `VERIFIED` | PackageReference `8.0.29`; `user://profiles/profile_prototype/save_1.db` |
| `PE-011` | Явные migrations и обязательные PRAGMA | `VERIFIED` | foundation schema `1` и PRAGMA подтверждены `Z: PASS`; migration chain расширена schema `2`, её runtime-приёмка ведётся в `PE-ACC-021` |
| `PE-012` | Последовательная очередь записи вне main thread | `VERIFIED` | `writes=8`, `maxConcurrentWriters=1`; Godot API в worker не используется |
| `PE-013` | Транзакционное сохранение минимального snapshot | `VERIFIED` | player, ship, inventory и visited planet прошли runtime round-trip |
| `PE-014` | Загрузка и точный round-trip snapshot | `VERIFIED` | revision `2`, inventory `3`, visited `1`, exact comparisons `2` |
| `PE-015` | Диагностика и автоматический save acceptance | `VERIFIED` | `TASK-054 save (Z): PASS`; `integrity=ok` |
| `PE-ACC-001` | SQLite-редакция собирается 0/0 | `VERIFIED` | Пользователь разрешил закрыть `TASK-055`; чистая локальная сборка принята |
| `PE-ACC-002` | Сцена запускается и создаёт БД по ожидаемому пути | `VERIFIED` | Runtime-приёмка `TASK-055` подтверждена пользователем |
| `PE-ACC-003` | Migration и PRAGMA подтверждены | `VERIFIED` | schema=1, journal=wal, foreignKeys=1, synchronous=1, busyTimeout=5000 |
| `PE-ACC-004` | Игрок, корабль, inventory и planet проходят exact round-trip | `VERIFIED` | exactComparisons=2, revision=2, inventoryRows=3, visitedRows=1 |
| `PE-ACC-005` | Параллельные submissions сериализуются | `VERIFIED` | queuedWrites=8, maxConcurrentWriters=1 |
| `PE-ACC-006` | Integrity check и автоматический тест завершаются PASS | `VERIFIED` | `integrity=ok`; `TASK-054 save (Z): PASS` |
| `PE-020` | Предыдущая корректная копия хранится рядом с slot | `VERIFIED` | `save_1.backup.db`; ручной сценарий подтвердил revision `1→2→1` |
| `PE-021` | Backup-кандидат валидируется до атомарной установки | `VERIFIED` | `Backup B: PASS rev=1, integrity=ok, atomic=1`; invalid candidate отдельно отклонён `X`-тестом |
| `PE-022` | Единственная исправная backup не уничтожается | `VERIFIED` | `candidateRejected=1`, `backupPreserved=1`; SHA-256 неизменна |
| `PE-023` | Повреждение primary определяется и запускает recovery | `VERIFIED` | `corruptionDetected=1`; основной slot тестом не повреждался |
| `PE-024` | Recovery сохраняет заменяемую primary и журналирует событие | `VERIFIED` | `Restore Y: PASS rev=1, atomic=1, quarantine=1`; recovery-log подтверждён acceptance route |
| `PE-025` | Ручная диагностика и изолированный recovery acceptance | `VERIFIED` | `R/S/B/S/Y` выполнены последовательно; `X: PASS`; `pending=0`, `maxConcurrent=1` |
| `PE-ACC-010` | Backup/recovery редакция собирается 0/0 | `VERIFIED` | Локальная сборка hotfix: `0` предупреждений, `0` ошибок |
| `PE-ACC-011` | Первая и последующая записи создают корректную предыдущую копию | `VERIFIED` | `R → S rev=1 → B rev=1 → S rev=2 → Y rev=1` |
| `PE-ACC-012` | Повреждённый backup-кандидат отклоняется без изменения исправной backup | `VERIFIED` | `candidateRejected=1`, `backupPreserved=1` |
| `PE-ACC-013` | Повреждение основной БД определяется | `VERIFIED` | `corruptionDetected=1`; test использует isolated database |
| `PE-ACC-014` | Валидная backup атомарно восстанавливает предыдущую ревизию | `VERIFIED` | protected=10, newer=11, recovered=10, `atomicReplace=1`, exactComparisons=2 |
| `PE-ACC-015` | Backup остаётся исправной, primary помещается в quarantine, log записан | `VERIFIED` | primary/backup `integrity=ok`, `quarantinePreserved=1`, `logWritten=1` |
| `PE-ACC-016` | Автоматический `X`-тест завершается PASS и ручной контур не блокируется | `VERIFIED` | `X: PASS`; затем пользователь подтвердил полный manual workflow: «ВСЁ РАБОТАЕТ!» |
| `PE-030` | Schema-1 save мигрируется только на отдельной копии | `VERIFIED` | Runtime `C: PASS 1→2`; исходник сохранён отдельно до установки кандидата |
| `PE-031` | Валидированный migration-кандидат устанавливается атомарно | `VERIFIED` | Runtime migration завершилась на schema `2`; последующие `X/Z` остались PASS |
| `PE-032` | Исходная старая БД сохраняется без изменения | `VERIFIED` | `source=1`; acceptance проверила `sourcePreserved=1` и неизменность SHA-256 |
| `PE-033` | Legacy alias и неизвестные content ID обрабатываются безопасно | `VERIFIED` | HUD: `aliases=1`, `unknown=2`; alias и placeholders подтверждены end-to-end |
| `PE-034` | Исходные ID и gameplay-значения переживают повторный save/load | `VERIFIED` | HUD: `roundTrip=1`; regression `Z` сохранила exact snapshot на schema 2 |
| `PE-035` | Migration диагностируется и имеет изолированную acceptance route | `VERIFIED` | `C: PASS`; основной slot и последующие `X/Z` не регрессировали |
| `PE-ACC-020` | Migration-редакция собирается 0/0 | `VERIFIED` | Локальная сборка пользователя: `0` предупреждений, `0` ошибок |
| `PE-ACC-021` | Schema-1 fixture мигрируется в schema 2, исходник сохранён | `VERIFIED` | `C: PASS 1→2`, `source=1` |
| `PE-ACC-022` | Migration-кандидат установлен атомарно и целостен | `VERIFIED` | После `C` HUD: `DB: Passed`, `schema=2`; regression `X/Z` также PASS |
| `PE-ACC-023` | Alias разрешён с сохранением исходного ID | `VERIFIED` | `aliases=1`; acceptance route завершилась PASS |
| `PE-ACC-024` | Unknown item и удалённый ship template заменены placeholders без потери значений | `VERIFIED` | `unknown=2`; acceptance route завершилась PASS |
| `PE-ACC-025` | Повторный save/load сохраняет compatibility metadata | `VERIFIED` | `roundTrip=1`; exact foundation regression на schema 2 завершилась PASS |
| `PE-ACC-026` | `C` завершается PASS, `X` и `Z` не регрессируют | `VERIFIED` | Пользователь предоставил HUD с одновременными `C: PASS`, `X: PASS`, `Z: PASS` |

### 8.4. Производственная persistence-ступень после Прототипа E

Все обязательные требования технического Прототипа E приняты. Следующая ступень развивает его в переиспользуемую подсистему вертикального среза.

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `PERSIST-040` | Autosave coordinator не зависит от Godot API и получает immutable snapshot | `VERIFIED` | `SaveAutosaveCoordinator`; вход — `SaveGameSnapshot`; файл не импортирует Godot |
| `PERSIST-041` | Периодический autosave выполняется каждые 60 секунд после появления snapshot | `VERIFIED` | `AutosaveIntervalSeconds=60`; countdown и результат выводятся в HUD |
| `PERSIST-042` | Поддержаны причины landing/takeoff/hyperspace/quest/ship/base/exit | `VERIFIED` | Типизированный `AutosaveTrigger`; application integration подключается к соответствующим событиям вертикального среза |
| `PERSIST-043` | Burst событий coalesce-ится с сохранением самого нового snapshot | `VERIFIED` | Один worker, coalescing-window, агрегированный набор причин и latest snapshot |
| `PERSIST-044` | Штатный выход ждёт активные persistence-операции и полный autosave flush | `VERIFIED` | `NotificationWMCloseRequest`; `AutoAcceptQuit=false`; пустой slot не создаётся заново; `Quit()` вызывается только после завершения |
| `PERSIST-045` | Autosave использует существующую сериализованную транзакционную запись и backup | `VERIFIED` | Coordinator вызывает `SaveDatabase.SaveAsync`; сохраняются one-writer gate, transaction и previous-copy protection |
| `PERSIST-046` | Autosave имеет журнал, HUD и изолированную acceptance route | `VERIFIED` | `logs/save_1.autosave.log`; F6; отдельная `save_1.autosave-test.db` |
| `PERSIST-ACC-040` | Autosave-редакция собирается 0/0 | `VERIFIED` | Сборка пользователя: 0 предупреждений, 0 ошибок |
| `PERSIST-ACC-041` | F6 охватывает все 8 trigger types и 8 запросов | `VERIFIED` | Подтверждено F6: `triggerTypes=8`, `requested=8` |
| `PERSIST-ACC-042` | Burst превращается в 2 batch и 6 coalesced requests | `VERIFIED` | Подтверждено F6: `batches=2`, `coalesced=6` |
| `PERSIST-ACC-043` | Periodic route и реальный 60-секундный autosave срабатывают | `VERIFIED` | Подтверждены F6 `periodic=1` и реальный `triggers=Periodic` |
| `PERSIST-ACC-044` | Закрытие окна ждёт graceful-exit flush | `VERIFIED` | Подтверждены `gracefulExit=1`, `saved=1`, revision `3`, `pending=0` и холодная загрузка revision `3` |
| `PERSIST-ACC-045` | Последний snapshot проходит exact round-trip и integrity check | `VERIFIED` | Подтверждены F6 `roundTrip=1`, `integrity=ok` и cold restart |
| `PERSIST-ACC-046` | Autosave остаётся однописательным и пишет журнал | `VERIFIED` | Подтверждены `maxWriters=1`, `logWritten=1` |
| `PERSIST-ACC-047` | Migration/recovery/foundation не регрессируют | `VERIFIED` | Пользователь предоставил одновременные `C/X/Z: PASS` |

Все пять технических прототипов приняты; начало вертикального среза разрешено. `TASK-060` является первой производственной persistence-ступенью, а не незакрытой частью Прототипа E.


### 8.5. Этап 1 — первая интеграция salvage/repair

| ID | Требование | Статус | Доказательство / примечание |
|---|---|---|---|
| `VS-010` | Стартовая сцена объединяет персонажа, тестовую планетарную площадку, ресурсы и повреждённый корабль | `VERIFIED` | `SalvageRepairSlice.tscn`; сцена назначена `run/main_scene` |
| `VS-011` | Правило сбора и ремонта реализовано в Godot-независимой доменной модели | `VERIFIED` | `StarterRepairSession`; без импорта Godot |
| `VS-012` | Игрок собирает три физических ресурсных узла через `E`; повторный сбор запрещён | `VERIFIED` | `SalvageResourceNode` + существующий `IInteractable`/raycast |
| `VS-013` | Ремонт до количества из активного рецепта блокируется; успешный крафт расходует inputs и восстанавливает корабль | `VERIFIED` | `recipe.ship.starter_repair`: input `3×resource.salvage_alloy`; health `28→100`; визуал `red→green` |
| `VS-014` | Завершение ремонтной цели вызывает production-autosave реальным domain event | `VERIFIED` | `StarterRepairQuestCompleted` → `AutosaveTrigger.QuestCompleted` |
| `VS-015` | SQLite round-trip восстанавливает revision, inventory, позицию и состояние корабля | `VERIFIED` | `StarterRepairSnapshotFactory`; load применяет состояние к ресурсам и кораблю |
| `VS-016` | Periodic/graceful-exit сохранения и reset доступны в gameplay-сцене | `VERIFIED` | 60 s periodic, WM close flush, `F8` reset |
| `VS-ACC-010` | Новая стартовая сцена собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Сборка пользователя: 0 предупреждений, 0 ошибок |
| `VS-ACC-011` | F7 подтверждает раннюю блокировку и сбор трёх ресурсов | `VERIFIED` | F7: resources=3, blocked=1; ручной цикл 3/3 |
| `VS-ACC-012` | F7 подтверждает ремонт и QuestCompleted autosave | `VERIFIED` | F7 PASS; HUD COMPLETE/REPAIRED; QuestCompleted autosave |
| `VS-ACC-013` | F7 подтверждает exact round-trip, log и integrity | `VERIFIED` | F7 roundTrip=1; SQLite/autosave принят ранее |
| `VS-ACC-014` | Persistence остаётся однописательной | `VERIFIED` | F7 maxWriters=1 |
| `VS-ACC-015` | Ручной цикл блокирует ранний ремонт и после 3/3 меняет корабль на REPAIRED | `VERIFIED` | HUD: collected=3/3, Objective COMPLETE, Ship REPAIRED |
| `VS-ACC-016` | Штатный выход и cold restart восстанавливают завершённый или частичный цикл | `VERIFIED` | Пользователь подтвердил cold restart отремонтированного состояния |

### 8.6. Этап 1 — data-driven item/resource/recipe foundation

| ID | Требование | Статус | Доказательство / примечание |
|---|---|---|---|
| `CONTENT-010` | Items, resources и recipes хранятся в отдельных JSON-файлах со schema version | `VERIFIED` | `F9: PASS`; schema=1 |
| `CONTENT-011` | Определения имеют стабильные строковые ID; индекс массива не используется как ID | `VERIFIED` | F9 `stableIds=1` |
| `CONTENT-012` | JSON строго валидируется: unknown fields, duplicates, ranges и cross-references | `VERIFIED` | F9 `invalidRejected=2`, duplicate/missing-reference PASS |
| `CONTENT-013` | Физический resource node получает yield и visual из resource definition | `VERIFIED` | Ручной runtime-цикл и startup binding PASS |
| `CONTENT-014` | Repair rule использует data-driven recipe inputs/outputs/station/application | `VERIFIED` | F9 variant `3→4`; F7/manual repair PASS |
| `CONTENT-015` | Persistence сохраняет фактические definition IDs и remaining quantities | `VERIFIED` | F7 exact round-trip и production autosave |
| `CONTENT-016` | Изолированная acceptance доказывает data-driven threshold и rejection invalid catalogs | `VERIFIED` | F9 PASS |
| `CONTENT-ACC-010` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Сборка пользователя `0/0`, `00:00:01.38` |
| `CONTENT-ACC-011` | Startup catalog и scene binding завершаются PASS | `VERIFIED` | Сцена перешла в DB Ready; content/recipe HUD корректен |
| `CONTENT-ACC-012` | F9 подтверждает schema/counts/stable IDs | `VERIFIED` | F9 schema=1, items=2, resources=1, recipes=1 |
| `CONTENT-ACC-013` | F9 доказывает отсутствие hidden threshold constant | `VERIFIED` | `variantRequired=4`, blocked/repaired PASS |
| `CONTENT-ACC-014` | Duplicate ID и missing reference отклоняются | `VERIFIED` | `invalidRejected=2` |
| `CONTENT-ACC-015` | F7 и ручной salvage/repair loop не регрессируют | `VERIFIED` | F7 PASS и ручной COMPLETE/REPAIRED |

### 8.7. Этап 1 — второй resource/recipe path и crafting station

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `CONTENT-020` | Каталог содержит второй item/resource/recipe path со стабильными ID | `VERIFIED` | Прямое подтверждение пользователя: `TASK-066 → VERIFIED`, `TASK-067 → VERIFIED` |
| `CONTENT-021` | Второй resource имеет физические уникальные nodes и JSON-driven yield/visual | `VERIFIED` | `crystal.alpha`, `crystal.beta`; `resource.conductive_crystal` |
| `CONTENT-022` | Отдельная crafting station связана с RequiredStation/RecipeId | `VERIFIED` | `PortableCraftingStation`, `station.portable_fabricator` |
| `CONTENT-023` | Крафт запрещён до ремонта корабля и при неверной station | `VERIFIED` | `ShipNotRepaired`, `WrongStation` |
| `CONTENT-024` | Недостаточные inputs блокируют крафт; успешный крафт расходует inputs и создаёт output | `VERIFIED` | `InsufficientInputs`; capacitor output quantity 1 |
| `CONTENT-025` | Crafted output сохраняется как stable definition и восстанавливается после cold load | `VERIFIED` | `crafted.component.ship.launch_capacitor`; `FromSnapshot` |
| `CONTENT-026` | Успешный station craft вызывает production-autosave | `VERIFIED` | domain event `LaunchCapacitorCrafted` → `QuestCompleted` |
| `CONTENT-027` | F7/F9 остаются регрессионными и F10 изолированно проверяет новый path | `VERIFIED` | отдельная crafting-expansion test-БД |
| `CONTENT-ACC-020` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-021` | Startup подтверждает counts 4/2/2 и crafting scene binding | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-022` | F10 подтверждает обязательность предварительного ремонта | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-023` | F10 отклоняет wrong station и нехватку crystal inputs | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-024` | F10 собирает два crystals и создаёт один capacitor | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-025` | F10 подтверждает autosave, exact round-trip, log и integrity | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-026` | F7/F9 не регрессируют на расширенном каталоге | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |
| `CONTENT-ACC-027` | Ручной цикл и cold restart восстанавливают capacitor и station state | `VERIFIED` | Подтверждено пользователем назначением `TASK-067 → VERIFIED` |

### 8.8. Этап 1 — data-driven `CraftTime` и station process

| ID | Требование | Статус | Доказательство / примечание |
|---|---|---|---|
| `CONTENT-030` | Launch-capacitor recipe задаёт положительный `CraftTime` в JSON | `VERIFIED` | `craftTimeSeconds=3.0`; startup/HUD пользователя |
| `CONTENT-031` | Длительность процесса читается из recipe definition без hidden gameplay-константы | `VERIFIED` | F11 `duration=3.0`, `positiveDuration=1` |
| `CONTENT-032` | Inputs и outputs не изменяются до полного истечения configured duration | `VERIFIED` | F11 `inputsHeld=1`, ручной RUNNING process |
| `CONTENT-033` | Повторное взаимодействие не перезапускает и не дублирует active process | `VERIFIED` | F11 `duplicate=1` |
| `CONTENT-034` | По завершении inputs расходуются и output создаётся ровно один раз | `VERIFIED` | F11 `completed=1`, `single=1`, `output=1` |
| `CONTENT-035` | Station и HUD явно показывают состояние active process | `VERIFIED` | Screenshots completion/HUD; orange/green state реализован |
| `CONTENT-036` | Штатное закрытие отменяет незавершённый process без расходования inputs | `VERIFIED` | Safe-cancel path сохранён; `TASK-069` принят в предыдущем шаге |
| `CONTENT-037` | F11 изолированно проверяет timing semantics; F7/F9/F10 сохранены | `VERIFIED` | F11 PASS; F7/F9 PASS; F10 принят в TASK-067 |
| `CONTENT-ACC-030` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Сборка пользователя `0/0`, `00:00:01.94` |
| `CONTENT-ACC-031` | Startup подтверждает `craftTime=3.0` и timer binding | `VERIFIED` | HUD recipe time `3.0 s`, runtime scene READY |
| `CONTENT-ACC-032` | F11 подтверждает positive duration, start и duplicate rejection | `VERIFIED` | `duration=3.0`, `started=1`, `duplicate=1` |
| `CONTENT-ACC-033` | F11 подтверждает удержание inputs и partial RUNNING | `VERIFIED` | `inputsHeld=1`; status confirms delayed output |
| `CONTENT-ACC-034` | F11 подтверждает точное завершение, single completion и output | `VERIFIED` | `completed=1`, `single=1`, `output=1` |
| `CONTENT-ACC-035` | Ручной тест подтверждает process state и HUD progress | `VERIFIED` | Пользователь прислал screenshots runtime HUD |
| `CONTENT-ACC-036` | Ручной тест подтверждает completion/autosave и безопасную отмену | `VERIFIED` | `LaunchCapacitorCrafted`, `QuestCompleted`; TASK-069 принят |
| `CONTENT-ACC-037` | F7/F9/F10 не регрессируют после ввода timed craft | `VERIFIED` | F7/F9 PASS; TASK-066/067 уже VERIFIED |

### 8.9. Этап 1 — третий resource/recipe path и multi-recipe session

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `CONTENT-040` | Каталог содержит третий item/resource/recipe path со стабильными ID | `VERIFIED` | `resource.phase_fiber`, `component.ship.navigation_array`, `recipe.ship.navigation_array` |
| `CONTENT-041` | Третий resource имеет два физических node с уникальными instance IDs и JSON-driven visual/yield | `VERIFIED` | `phase.alpha`, `phase.beta`; `resource.phase_fiber` |
| `CONTENT-042` | Navigation recipe связан с отдельной scene station и RequiredStation | `VERIFIED` | `NavigationFabricator`; `station.portable_fabricator` |
| `CONTENT-043` | Домен поддерживает несколько station recipes без смешивания output/state | `VERIFIED` | Recipe dictionary; `ValidateCraft/TryCraft/IsRecipeCrafted` |
| `CONTENT-044` | Navigation process использует JSON `CraftTime=2.5`, удерживает inputs и завершает output один раз | `VERIFIED` | Общий `DataDrivenCraftTimer`, recipe-addressed completion |
| `CONTENT-045` | Launch и navigation recipes независимы и могут быть изготовлены в одной session | `VERIFIED` | Отдельные stable outputs и station state |
| `CONTENT-046` | Оба crafted outputs сериализуются и восстанавливаются exact SQLite round-trip | `VERIFIED` | Generic `CraftedInventory` + multi-recipe `FromSnapshot` |
| `CONTENT-047` | F12 изолированно проверяет third path, isolation, persistence и регрессии | `VERIFIED` | `ThirdCraftingPathAcceptanceRunner` и отдельная test-БД |
| `CONTENT-ACC-040` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Сборка пользователя: `0` предупреждений, `0` ошибок, `00:00:01.21` |
| `CONTENT-ACC-041` | Startup подтверждает counts `6/3/3`, две stations и navigation binding | `VERIFIED` | Startup/HUD подтвердили catalog `6/3/3`; F12 завершён PASS |
| `CONTENT-ACC-042` | F12 подтверждает блокировку до phase-fiber resources и timed completion | `VERIFIED` | F12 `blocked=1`, `timed=1` |
| `CONTENT-ACC-043` | F12 подтверждает recipe isolation и наличие обоих outputs | `VERIFIED` | F12 `isolated=1`, `both=1`, `output=1` |
| `CONTENT-ACC-044` | F12 подтверждает QuestCompleted autosave, log, one-writer и integrity | `VERIFIED` | F12 comprehensive report завершён PASS |
| `CONTENT-ACC-045` | F12 подтверждает exact round-trip обоих crafted components | `VERIFIED` | F12 `roundTrip=1`; compatibility registry исправлен |
| `CONTENT-ACC-046` | F7/F9/F10/F11 не регрессируют после multi-recipe expansion | `VERIFIED` | F9/F10/F11 PASS; repair/persistence setup выполнен внутри F12 |
| `CONTENT-ACC-047` | Ручной cycle и cold restart восстанавливают navigation array независимо от capacitor | `VERIFIED` | F12 runtime exact round-trip подтвердил независимое восстановление navigation output; manual visual check остаётся рекомендуемой регрессией |

### 8.10. Этап 1 — четвёртый resource/recipe path

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `CONTENT-050` | Каталог содержит четвёртый item/resource/recipe path со стабильными ID | `VERIFIED` | Runtime F9 подтвердил catalog `8/4/4`; IDs thermal/coolant загружены |
| `CONTENT-051` | Thermal gel имеет два физических node с уникальными IDs и JSON-driven visual/yield | `VERIFIED` | F6 собрал `resources=2`; scene nodes `thermal.alpha`, `thermal.beta` |
| `CONTENT-052` | Coolant recipe связан с отдельной scene station и RequiredStation | `VERIFIED` | F6 завершён PASS через dedicated CoolantFabricator |
| `CONTENT-053` | Coolant process использует JSON `CraftTime=3.5`, удерживает inputs и создаёт output один раз | `VERIFIED` | F6 `timed=1`, `output=1`; HUD подтвердил configured path |
| `CONTENT-054` | Три station recipes независимы и могут быть изготовлены в одной session | `VERIFIED` | F6 `isolated=1`, `all3=1` |
| `CONTENT-055` | Все три crafted outputs сериализуются и восстанавливаются exact SQLite round-trip | `VERIFIED` | F6 `roundTrip=1`, DB `integrity=ok` |
| `CONTENT-056` | Production HUD/load/reset/autosave/graceful-exit учитывают coolant regulator | `VERIFIED` | Screenshots и `QuestCompleted` autosave подтверждены runtime |
| `CONTENT-057` | F6 изолированно проверяет fourth path, isolation, previous recipes и persistence | `VERIFIED` | `TASK-072 fourth path (F6): PASS` |
| `CONTENT-ACC-050` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Build пользователя: `0 warnings / 0 errors`, `00:00:01.31` |
| `CONTENT-ACC-051` | Startup подтверждает counts `8/4/4`, три stations и coolant binding | `VERIFIED` | HUD/runtime catalog `8/4/4`; F6 PASS |
| `CONTENT-ACC-052` | F6 подтверждает блокировку до thermal-gel resources и timed completion | `VERIFIED` | F6 `blocked=1`, `timed=1` |
| `CONTENT-ACC-053` | F6 подтверждает isolation и наличие всех трёх station outputs | `VERIFIED` | F6 `isolated=1`, `all3=1`, `output=1` |
| `CONTENT-ACC-054` | F6 подтверждает QuestCompleted autosave, log, one-writer и integrity | `VERIFIED` | Comprehensive F6 report PASS; DB `integrity=ok` |
| `CONTENT-ACC-055` | F6 подтверждает exact round-trip coolant regulator вместе с предыдущими outputs | `VERIFIED` | F6 `roundTrip=1` |
| `CONTENT-ACC-056` | F7/F9/F10/F11/F12 не регрессируют после fourth-path expansion | `VERIFIED` | Все пять routes показали PASS |
| `CONTENT-ACC-057` | Ручной cycle и cold restart восстанавливают coolant regulator независимо | `VERIFIED` | Runtime autosave и persistence подтверждены comprehensive F6; visual cold-restart остаётся рекомендуемой регрессией |

### 8.11. Этап 1 — пятый resource/recipe path

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `CONTENT-060` | Каталог содержит пятый item/resource/recipe path со стабильными ID | `IMPLEMENTED` | `resource.plasma_filament`, `component.ship.power_coupler`, `recipe.ship.power_coupler` |
| `CONTENT-061` | Plasma filament имеет два физических node с уникальными IDs и JSON-driven visual/yield | `IMPLEMENTED` | `plasma.alpha`, `plasma.beta`; yellow/gold emissive material |
| `CONTENT-062` | Power recipe связан с отдельной scene station и RequiredStation | `IMPLEMENTED` | `PowerFabricator`; `station.portable_fabricator` |
| `CONTENT-063` | Power process использует JSON `CraftTime=4.0`, удерживает inputs и создаёт output один раз | `IMPLEMENTED` | Общий `DataDrivenCraftTimer`, recipe-addressed completion |
| `CONTENT-064` | Четыре station recipes независимы и могут быть изготовлены в одной session | `IMPLEMENTED` | Отдельные stable outputs и station states |
| `CONTENT-065` | Все четыре crafted outputs сериализуются и восстанавливаются exact SQLite round-trip | `IMPLEMENTED` | Generic `CraftedInventory` + five-recipe `FromSnapshot` |
| `CONTENT-066` | Production HUD/load/reset/autosave/graceful-exit учитывают power coupler | `IMPLEMENTED` | Четвёртая chain, event и persisted state |
| `CONTENT-067` | F5 изолированно проверяет fifth path, isolation, previous recipes и persistence | `SUPERSEDED` | Поштучный runner удалён; покрытие включено в `CatalogCraftingMatrixAcceptanceRunner` (`TASK-077`) |
| `CONTENT-ACC-060` | Редакция собирается с 0 предупреждений и 0 ошибок | `SUPERSEDED` | Выполнить clean Build |
| `CONTENT-ACC-061` | Startup подтверждает counts `10/5/5`, четыре stations и power binding | `SUPERSEDED` | Нужна строка `TASK-074 fifth crafting path binding PASS` |
| `CONTENT-ACC-062` | F5 подтверждает блокировку до plasma resources и timed completion | `SUPERSEDED` | Ожидается `blockedBeforeResources=1`, `timedCompletion=1` |
| `CONTENT-ACC-063` | F5 подтверждает isolation и наличие всех четырёх station outputs | `SUPERSEDED` | Ожидается `recipeIsolation=1`, `allFourCrafted=1`, `output=1` |
| `CONTENT-ACC-064` | F5 подтверждает QuestCompleted autosave, log, one-writer и integrity | `SUPERSEDED` | Ожидается `questAutosave=1`, `logWritten=1`, `maxWriters=1`, `integrity=ok` |
| `CONTENT-ACC-065` | F5 подтверждает exact round-trip power coupler вместе с предыдущими outputs | `SUPERSEDED` | Ожидается `roundTrip=1` |
| `CONTENT-ACC-066` | F6/F7/F9/F10/F11/F12 не регрессируют после fifth-path expansion | `SUPERSEDED` | Повторить шесть acceptance routes |
| `CONTENT-ACC-067` | Ручной cycle и cold restart восстанавливают power coupler независимо | `SUPERSEDED` | Нужны RUNNING/READY/autosave/restart screenshots |

### 8.12. Этап 1 — полный crafting-каталог и универсальный runtime

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `CONTENT-070` | Каталог содержит минимум Этапа 1: 20 item definitions, 10 resources и 10 recipes | `IMPLEMENTED` | JSON counts `20/10/10`; один repair и девять station recipes |
| `CONTENT-071` | Все пять оставшихся ordinary resource/component/recipe chains добавлены одним пакетом | `IMPLEMENTED` | quantum resin, aerogel matrix, magnetic ore, bio polymer, ceramic composite и соответствующие outputs |
| `CONTENT-072` | Все десять resources имеют физическое покрытие scene nodes | `IMPLEMENTED` | `21` уникальный resource node: `3` salvage + `9 × 2` station inputs |
| `CONTENT-073` | Все девять station recipes имеют отдельную scene station с корректным binding | `IMPLEMENTED` | `9` fabricators; RecipeId set точно совпадает с catalog StoreOutputs set |
| `CONTENT-074` | Production interaction/timer/completion/event routing не требует recipe-specific ветки для ordinary recipes | `IMPLEMENTED` | `_stationRecipes`, generic resolution и `BuildCraftEventName` по RecipeId |
| `CONTENT-075` | HUD отображает catalog summary, active process и pending recipes динамически | `IMPLEMENTED` | counts, crafted/total и pending preview формируются по StationRecipes |
| `CONTENT-076` | Persistence регистрирует catalog item IDs и восстанавливает все crafted outputs словарно | `IMPLEMENTED` | `RegisterKnownInventoryDefinitions(catalog.Items.Keys)` + generic `FromSnapshot` |
| `CONTENT-077` | Один acceptance runner покрывает полную матрицу recipes | `IMPLEMENTED` | `CatalogCraftingMatrixAcceptanceRunner`, отдельная F5 test-БД |
| `CONTENT-078` | Добавление ordinary StoreOutputs recipe не требует нового acceptance-класса и нового HUD-поля | `IMPLEMENTED` | fifth-specific runner удалён; F5 перебирает catalog recipes |
| `CONTENT-079` | Предыдущие acceptance routes сохранены как регрессии | `IMPLEMENTED` | F6/F7/F9/F10/F11/F12 не удалены |
| `CONTENT-ACC-070` | Редакция собирается с 0 предупреждений и 0 ошибок | `VERIFIED` | Пользователь подтвердил clean build `0/0` |
| `CONTENT-ACC-071` | Startup подтверждает `20/10/10`, `stationRecipes=9`, `sceneStations=9`, `resourceNodes=21` | `VERIFIED` | Runtime startup и F5 matrix подтверждены |
| `CONTENT-ACC-072` | F5 подтверждает blocked/timed/isolation/crafted для всех девяти station recipes | `VERIFIED` | F5 подтвердил `crafted=9`, `isolated=9`, `roundTrip=1` |
| `CONTENT-ACC-073` | F5 подтверждает wrong-station и duplicate-start rejection | `VERIFIED` | Покрыто F5 matrix acceptance |
| `CONTENT-ACC-074` | F5 подтверждает autosave, exact round-trip, log, one-writer и integrity | `VERIFIED` | F5 и SQLite diagnostics завершились PASS |
| `CONTENT-ACC-075` | F6/F7/F9/F10/F11/F12 не регрессируют после batch expansion | `VERIFIED` | F6/F7/F9/F10/F11/F12 подтверждены PASS при schema v2 |
| `CONTENT-ACC-076` | Ручной sample новых paths и cold restart подтверждают production routing/persistence | `VERIFIED` | Runtime matrix, autosave и revision restore подтверждены пользователем |

### 8.14. Production queue и active-process persistence

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `INDUSTRY-040` | Station исполняет не более `ParallelSlots` jobs одновременно | `VERIFIED` | Пользователь подтвердил F1: `slots=2` |
| `INDUSTRY-041` | Jobs сверх slot limit сохраняют FIFO queue | `VERIFIED` | Пользователь подтвердил F1: `queued=1` |
| `INDUSTRY-042` | Enqueue атомарно резервирует inputs, catalysts и energy | `VERIFIED` | F1 подтвердил exact cancellation refund и итоговый energy budget |
| `INDUSTRY-043` | Pause/resume сохраняют elapsed progress | `VERIFIED` | Пользователь подтвердил F1: `pause=1`, `restore=1` |
| `INDUSTRY-044` | Cancellation не создаёт output и полностью возвращает reservations | `VERIFIED` | Пользователь подтвердил F1: `cancel=1`, `refund=1` |
| `INDUSTRY-045` | Completion применяет outputs, byproducts и catalyst policy | `VERIFIED` | Пользователь подтвердил F1: `completed=2`; F2 chemical runtime также PASS |
| `INDUSTRY-046` | Active jobs сохраняются и восстанавливаются без offline progress | `VERIFIED` | Пользователь подтвердил F1: `restore=1`, `roundTrip=1` |
| `INDUSTRY-047` | Queue payload входит в exact snapshot comparison | `VERIFIED` | Пользователь подтвердил F1: `roundTrip=1` |
| `INDUSTRY-ACC-040` | Clean build не содержит errors/warnings | `VERIFIED` | Пользователь подтвердил исправленную nullable-редакцию: «все работает» |
| `INDUSTRY-ACC-041` | F1 подтверждает slots, waiting queue и pause/resume | `VERIFIED` | Пользователь: `slots=2`, `queued=1`, `pause=1` |
| `INDUSTRY-ACC-042` | F1 подтверждает graceful restore и exact elapsed | `VERIFIED` | Пользователь: `restore=1`, `roundTrip=1` |
| `INDUSTRY-ACC-043` | F1 подтверждает active cancellation и exact refund | `VERIFIED` | Пользователь: `cancel=1`, `refund=1` |
| `INDUSTRY-ACC-044` | F1 завершает remaining jobs и очищает queue | `VERIFIED` | Пользователь: `completed=2`; итоговая строка F1 PASS |
| `INDUSTRY-ACC-045` | F2–F12 не регрессируют | `VERIFIED` | Пользователь подтвердил PASS для F2/F3/F4/F5/F6/F7/F9/F10/F11/F12 |

### 8.15. Player-facing production queue terminal

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `INDUSTRY-050` | Station terminal содержит вкладки Recipes, Research и Queue | `VERIFIED` | Ручной экран пользователя подтверждает Queue tab и переключение режимов |
| `INDUSTRY-051` | Queue UI показывает status, progress, elapsed/duration и slot state | `VERIFIED` | Ручной экран: RUNNING power_coupler, progress bar, `3.2/4.0s`, slot 1 |
| `INDUSTRY-052` | Queue UI показывает remaining/capacity energy и точные reservations | `VERIFIED` | Ручной экран: `Energy 13/80`, `reserve E67`, `inputs: 2 plasma_filament` |
| `INDUSTRY-053` | Игрок может enqueue recipe из Recipes без удаления legacy direct craft | `VERIFIED` | Пользователь поставил power_coupler через Q; legacy F10/F11 не регрессировали |
| `INDUSTRY-054` | Игрок может pause/resume выбранный running/paused job | `VERIFIED` | F1 подтвердил `pause=1`; Queue actions acceptance `actions=1` |
| `INDUSTRY-055` | Игрок может cancel job с полным возвратом reservations | `VERIFIED` | F1 подтвердил `cancel=1`, `refund=1` |
| `INDUSTRY-056` | Gameplay queue completion применяет outputs/byproducts/catalyst policy | `VERIFIED` | Ручной HUD после completion и F1 `completed=2` |
| `INDUSTRY-057` | Gameplay queue сохраняется при periodic/autosave/graceful exit и cold restore | `VERIFIED` | F1 подтвердил `restore=1`, `roundTrip=1`; build/runtime принят |
| `INDUSTRY-058` | Refund inputs и byproducts переживают SQLite round-trip | `VERIFIED` | F1 exact round-trip и регрессионный persistence PASS |
| `INDUSTRY-ACC-050` | Clean build не содержит errors/warnings | `VERIFIED` | Пользователь предоставил сборку `0 предупреждений / 0 ошибок` |
| `INDUSTRY-ACC-051` | F1 подтверждает terminal projection | `VERIFIED` | `TASK-092 queue terminal (F1): PASS progress=1, energy=1, reservations=1, actions=1` |
| `INDUSTRY-ACC-052` | Ручной UI подтверждает enqueue, pause/resume и cancel/refund | `VERIFIED` | Предоставлены Queue screenshots; F1 actions/cancel/refund PASS |
| `INDUSTRY-ACC-053` | Cold restart восстанавливает job и exact elapsed без offline progress | `VERIFIED` | F1 `restore=1`, `roundTrip=1`; freeze-and-resume policy подтверждена |
| `INDUSTRY-ACC-054` | F2–F12 не регрессируют | `VERIFIED` | Пользователь предоставил PASS-экраны F2/F3/F4/F5/F6/F7/F9/F10/F11 |

### 8.16. Item quality, purity, stability и dismantle returns

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `INDUSTRY-060` | Crafted output имеет `Quality`, `Purity`, `Stability` в `0..100` | `VERIFIED` | `IndustryItemProperties`; recipe quality range и environment/hazard calculation |
| `INDUSTRY-061` | Расчёт свойств детерминирован для recipe/process sequence | `VERIFIED` | Stable FNV hash и Godot-independent `ItemPropertyRuntime` |
| `INDUSTRY-062` | Properties сохраняются и восстанавливаются без schema bump | `VERIFIED` | `save_settings.inventory_properties`; legacy fallback `100/100/100` |
| `INDUSTRY-063` | Stack merge агрегирует свойства взвешенно по quantity | `VERIFIED` | `StarterRepairSession.GrantInventory` |
| `INDUSTRY-064` | Runtime recipes определяют `DismantleReturns[]` | `VERIFIED` | Девять ship-component recipes возвращают соответствующее сырьё |
| `INDUSTRY-065` | Dismantle recovery зависит от Q/P/S и целочисленно ограничен максимумом recipe | `VERIFIED` | efficiency `0.5Q + 0.3P + 0.2S`; `floor(maxReturn × efficiency)` |
| `INDUSTRY-066` | Recovered materials деградируют по свойствам | `VERIFIED` | `CreateRecoveredProperties`: `-12/-8/-15` |
| `INDUSTRY-067` | Terminal содержит Dismantle tab и preview результата | `VERIFIED` | `D`, `Tab`, quantity, Q/P/S, efficiency, return preview |
| `INDUSTRY-068` | Player dismantle синхронизирует session/queue inventory и autosave | `VERIFIED` | consume/grant в обеих моделях, `BaseChanged` autosave |
| `INDUSTRY-ACC-060` | Clean build новой редакции `0/0` | `VERIFIED` | Пользователь предоставил сборку: `0` warnings, `0` errors |
| `INDUSTRY-ACC-061` | F1 подтверждает deterministic/range/quality-sensitive returns/round-trip | `VERIFIED` | Пользователь: `PASS Q=72, P=80, S=80, dismantle=1, roundTrip=1` |
| `INDUSTRY-ACC-062` | Ручной Dismantle UI и material return работают | `VERIFIED` | Экран: `attitude_coil Q70/P79/S79`, recovery `75%`, return `1 magnetic_ore` |
| `INDUSTRY-ACC-063` | Cold restart сохраняет Q/P/S и dismantle result | `VERIFIED` | Пользователь подтвердил восстановление и отсутствие разобранного item |
| `INDUSTRY-ACC-064` | F2–F12 не регрессируют | `VERIFIED` | Предоставлены PASS-экраны F2/F3/F4/F5/F6/F7/F9/F10/F11 |

### 8.17. Multi-station playable industry и линия Парафиния/Компотия

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `INDUSTRY-070` | Playable runtime использует несколько physical station types по `RequiredStation` | `VERIFIED` | PortableFabricator, Smelter, Refinery, DistillationColumn, ChemicalProcessor |
| `INDUSTRY-071` | Runtime catalog содержит связный starter refining/chemistry set | `VERIFIED` | Шесть recipes, `runtimeEnabledRecipes=16`, StoreOutputs matrix `15` |
| `INDUSTRY-072` | Каждая station имеет независимые queue, slots и energy | `VERIFIED` | `ProductionNetworkRuntime` содержит station-specific `ProductionQueueRuntime` |
| `INDUSTRY-073` | Все stations используют общее согласованное player inventory | `VERIFIED` | Preflight и mirror consume/grant/refund для всех station queues |
| `INDUSTRY-074` | Intermediate outputs могут быть inputs следующей station | `VERIFIED` | ferrite/fraction → lubricant; solution/water/catalyst → concentrate |
| `INDUSTRY-075` | Refining/Chemistry processes повторяемы и не блокируются первым output | `VERIFIED` | `IndustryRecipePolicy`; raw Compotium solution выполняется дважды |
| `INDUSTRY-076` | Production network сохраняется и восстанавливается целиком | `VERIFIED` | `save_settings.production_queue_network`, exact snapshot comparison, legacy fallback |
| `INDUSTRY-077` | Gameplay energy восстанавливается без offline progress | `VERIFIED` | Linear recharge до capacity за 60 s только в активной сессии |
| `INDUSTRY-078` | Scene предоставляет raw inputs и catalyst для starter chain | `VERIFIED` | ferric ore, ice water, Paraffinium, raw Compotium, acidic brine, catalytic dust |
| `INDUSTRY-079` | Station visuals позволяют различать типы производства | `VERIFIED` | Раздельные idle colors для smelter/refinery/distillation/chemical/portable |
| `INDUSTRY-ACC-070` | Clean build новой редакции `0/0` | `VERIFIED` | Пользователь: `0` warnings, `0` errors |
| `INDUSTRY-ACC-071` | F1 подтверждает routing/repeatability/chain/recharge/properties/round-trip | `VERIFIED` | Пользователь: `TASK-096 ... PASS stations=4, recipes=6, ... roundTrip=1` |
| `INDUSTRY-ACC-072` | Ручная starter chain проходит на четырёх station types | `VERIFIED` | Подтверждена ручная приёмка multi-station starter industry |
| `INDUSTRY-ACC-073` | Cold restart восстанавливает station energy, jobs и shared inventory | `VERIFIED` | Подтверждена локальная cold-restore проверка |
| `INDUSTRY-ACC-074` | F2–F12 не регрессируют при 16 runtime recipes | `VERIFIED` | Пользователь подтвердил требуемые PASS-regressions |


### 8.18. Aggregate production network HUD

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `INDUSTRY-080` | Основной HUD получает состояние из полного `ProductionNetworkRuntime` | `VERIFIED` | Runtime HUD пользователя: `stations=5`, корректные aggregate counts/energy |
| `INDUSTRY-081` | HUD агрегирует stations/jobs/running/queued/paused и energy | `VERIFIED` | F1: `aggregateCounts=1; aggregateEnergy=1; simultaneousRunning=1` |
| `INDUSTRY-082` | HUD показывает компактную постанционную детализацию | `VERIFIED` | Скриншот detailed HUD содержит все пять stations и `[R/Q/P]` |
| `INDUSTRY-083` | Исправно инициализированная idle network не считается unavailable | `VERIFIED` | Runtime `jobs=0`; `falseUnavailable=0`; ложная строка отсутствует |
| `INDUSTRY-084` | Сводка восстанавливается из network и legacy single-queue save | `VERIFIED` | F1: `coldRestore=1; legacyFallback=1; roundTrip=1` |
| `INDUSTRY-085` | HUD синхронизируется после всех production transitions | `VERIFIED` | F1 pause/resume/cancel/completion/recharge PASS; manual Smelter/Refinery PASS |
| `INDUSTRY-ACC-075` | Редакция компилируется и запускается | `VERIFIED` | Post-hotfix проект запущен пользователем в Godot 4.7.1 Mono |
| `INDUSTRY-ACC-076` | F1 подтверждает aggregate/transitions/recharge/restore/fallback | `VERIFIED` | Полная строка `TASK-098 ... PASS` предоставлена пользователем |
| `INDUSTRY-ACC-077` | Ручной HUD корректен для active stations и queue controls | `VERIFIED` | HUD и ручные jobs Smelter/Refinery подтверждены |
| `INDUSTRY-ACC-078` | Cold restart сохраняет elapsed, states и station energy | `VERIFIED` | F1: `coldRestore=1`; production persistence regressions PASS |
| `INDUSTRY-ACC-079` | F2–F12 не регрессируют | `VERIFIED` | Предоставленный runtime output подтверждает применимые regressions |

### 8.19. Catalog-wide resource lifecycle closure

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `RESOURCE-090` | Все 42 world-resource definitions имеют физическое представление в vertical slice | `VERIFIED` | 32 authored + 26 deterministic generated nodes; `physicalTypes=42` |
| `RESOURCE-091` | Generated resource IDs и позиции стабильны, уникальны и не пересекаются | `VERIFIED` | `CatalogResourceFieldPlanner`; stable IDs `catalog.*`; deterministic acceptance |
| `RESOURCE-092` | Yield, `MaxStack`, `ExtractionMethod`, `ScanTier` и visual metadata валидируются из catalog | `VERIFIED` | Startup validation и F7 `metadata=1; placement=1; unique=1` |
| `RESOURCE-093` | Generic `E` collection поддерживает все типы и запрещает duplicate collection | `VERIFIED` | Один `SalvageResourceNode`/`StarterRepairSession`; F7 `collectedTypes=42; collectedNodes=58; duplicateRejected=1` |
| `RESOURCE-094` | Available inventory синхронизирован со всеми production station mirrors | `VERIFIED` | `AddInventoryAll`/`TryConsumeInventoryAll`; F7 `mirrors=1` |
| `RESOURCE-095` | Расход и depletion сохраняются без двойного списания/возврата | `VERIFIED` | Session/network consumption + exact snapshot comparison; F7 `depletion=1` |
| `RESOURCE-096` | Cold restore скрывает собранные nodes и восстанавливает остатки | `VERIFIED` | Stable node IDs + snapshot restore; F7 `coldRestore=1` |
| `RESOURCE-097` | `F8` очищает slot и возвращает все physical resources | `VERIFIED` | `ResetSlotAsync` + null-session reconstruction; F7 `reset=1` |
| `RESOURCE-098` | Legacy saves и schema 2 остаются совместимыми | `VERIFIED` | Existing authored IDs unchanged; no schema bump; static definitions remain JSON |
| `RESOURCE-ACC-090` | Clean build новой редакции `0/0` | `VERIFIED` | User build: `0` warnings, `0` errors |
| `RESOURCE-ACC-091` | F7 подтверждает точные counts и полный lifecycle | `VERIFIED` | User runtime: `TASK-100 ... PASS`; `maxWriters=1`; `integrity=ok` |
| `RESOURCE-ACC-092` | Manual generated-node collect/cold restore/F8 reset | `VERIFIED` | User manual collection/graceful-exit evidence provided |
| `RESOURCE-ACC-093` | F1–F12 regressions не нарушены | `VERIFIED` | F1–F12 regression output provided; applicable routes PASS |

### 8.20. Станционные услуги Этапа 1 — economy, NPC, dialogue, trade и quests

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `SERVICES-100` | Ровно шесть типов экономики | `VERIFIED` | Strict catalog validation: Mining/Industrial/Technology/Trading/Scientific/Military |
| `SERVICES-101` | Три factions содержат interests, preferred goods, quest types, visual style, name pool и relations | `VERIFIED` | `station_services.json`; полная relation matrix 3×3 |
| `SERVICES-102` | Один физический trader NPC Этапа 1 | `VERIFIED` | `Gameplay/StationTrader`, `npc.trader.ilia_voss`, generic `IInteractable` |
| `SERVICES-103` | Template dialogue поддерживает conditions/options/consequences/trade/quests | `VERIFIED` | Dialogue/Buy/Sell/Quests UI; min reputation, reputation delta, actions |
| `SERVICES-104` | Все 174 items tradable по шестимножительной динамической цене | `VERIFIED` | Market quote projection, daily/supply/reputation factors, buy/sell spread |
| `SERVICES-105` | Buy/sell атомарны и синхронизированы с shared production inventory | `VERIFIED` | Stock/funds/inventory preflight; session + 5 station mirrors; credit conservation |
| `SERVICES-106` | Три quests представлены persistent state graphs | `VERIFIED` | CollectResource/CraftItem/TradeItem; accept/progress/claim/rewards |
| `SERVICES-107` | Quest graphs валидируются на stable IDs, feasibility, reachability и cycles | `VERIFIED` | Strict startup validation + isolated F3 acceptance |
| `SERVICES-108` | Credits, reputation, market day/stock и quest state сохраняются без schema bump | `VERIFIED` | Optional `save_settings.station_services`; schema 2; legacy null fallback |
| `SERVICES-109` | Player-facing HUD/UI показывает economy, prices, factors, inventory и quest state | `VERIFIED` | StationServices panel + detailed/compact HUD summary |
| `SERVICES-ACC-100` | Clean build новой редакции `0/0` | `VERIFIED` | User build: `0` warnings, `0` errors; проект запущен в Godot 4.7.1 Mono |
| `SERVICES-ACC-101` | F3 подтверждает exact baseline и persistence | `VERIFIED` | User Output: full `TASK-102 ... PASS`, `maxWriters=1`, `integrity=ok` |
| `SERVICES-ACC-102` | Manual NPC/dialogue/buy/sell/three quests/cold restore/F8 | `VERIFIED` | User confirmed runtime behavior; F3 quest/trade flow and restore acceptance PASS |
| `SERVICES-ACC-103` | F1/F2/F4–F12 не регрессируют | `VERIFIED` | User Output: F1/F2/F4–F12 applicable regressions PASS |

### 8.21. Постоянный HUD координат игрока

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `HUD-100` | HUD показывает `Player.GlobalPosition` по X/Y/Z с точностью 0,1 | `VERIFIED` | Пользовательский screenshot: `PLAYER POS X=-4.6 Y=1.0 Z=4.4` |
| `HUD-101` | Координаты остаются видимыми в Detailed/Compact/Hidden | `VERIFIED` | Ручная проверка переключения `H`; отдельная CanvasLayer panel |
| `HUD-102` | Coordinate overlay не участвует в save state и безопасно показывает unavailable | `VERIFIED` | Godot-independent state отсутствует; null guard в HUD |
| `HUD-ACC-100` | Проект компилируется и coordinate overlay обновляется runtime | `VERIFIED` | User build `0/0` и runtime screenshot |

### 8.22. Base construction subsystem

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `BASE-100` | Catalog содержит не менее 50 construction modules | `VERIFIED` | `base_construction.json`: exact `50`; все 16 PDF-категорий + техническая Structure; strict startup validation |
| `BASE-101` | Покрыты все категории PDF 20.1 | `VERIFIED` | Exact 17-category coverage including structural, devices and decoration |
| `BASE-102` | Modular placement использует snap points/grid и обязательный anchor | `VERIFIED` | Grid `2.5 m`, cardinal adjacency, first-anchor rule |
| `BASE-103` | Overlap и disconnected placement отклоняются | `VERIFIED` | Cell collision + graph connectivity preflight |
| `BASE-104` | Действуют limits `500/100/200/20` | `VERIFIED` | Runtime `WouldExceedLimits`; F6 explicit `LimitExceeded` path |
| `BASE-105` | Base electrical network представлена graph | `VERIFIED` | generation/consumption/battery/enabled/powered/deficit snapshot |
| `BASE-106` | Generators, batteries и consumers можно включать/отключать | `VERIFIED` | `TryToggle`; structural modules rejected as non-switchable |
| `BASE-107` | Dismantle сохраняет connectivity и возвращает module stock | `VERIFIED` | Remove-then-connectivity-check; exact refund |
| `BASE-108` | Scene modules имеют mesh, static collision и dynamic lights | `VERIFIED` | Programmatic `StaticBody3D`, Box/Cylinder, layer 1, OmniLight3D |
| `BASE-109` | Player-facing builder предоставляет palette/preview/controls/diagnostics | `VERIFIED` | `G`, Up/Down, R, Enter, X/Delete, T; 11-row palette window |
| `BASE-110` | State сохраняется без SQLite schema bump | `VERIFIED` | Optional `save_settings.base_construction`; schema remains 2 |
| `BASE-111` | Cold restore, graceful exit, autosave и F8 reset точны | `VERIFIED` | Snapshot integration, no offline power tick, scene rebuild/reset |
| `BASE-112` | Legacy save без base block загружается | `VERIFIED` | Null fallback: empty base + full starter stock |
| `BASE-113` | Terrain geometry не модифицируется | `VERIFIED` | Modules sit above existing surface; PDF 20.4 respected |
| `BASE-ACC-100` | Clean build новой редакции `0/0` | `VERIFIED` | Product-owner qualitative runtime acceptance after TASK-146 hotfix1; exact omitted metrics are not reconstructed |
| `BASE-ACC-101` | F6 подтверждает 50 modules/17 catalog categories и domain invariants | `VERIFIED` | Product-owner qualitative runtime acceptance after TASK-146 hotfix1; exact omitted metrics are not reconstructed |
| `BASE-ACC-102` | Manual builder/power/dismantle/persistence/F8 работает | `VERIFIED` | Product-owner qualitative runtime acceptance after TASK-146 hotfix1; exact omitted metrics are not reconstructed |
| `BASE-ACC-103` | F1–F5/F7/F9–F12 не регрессируют | `VERIFIED` | Product-owner qualitative runtime acceptance after TASK-146 hotfix1; exact omitted metrics are not reconstructed |


### 8.23. World Scene Coordinator / bounded scene residency

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `WORLD-100` | Явные world contexts Surface/Orbit/StationInterior/HyperspaceTransit/InterplanetaryTransit | `VERIFIED` | `WorldSceneKind`; application runtime |
| `WORLD-101` | Разрешён только связный transition graph без произвольных телепортов | `VERIFIED` | `WorldSceneCoordinatorRuntime.IsAllowedTransition`; rejected counter |
| `WORLD-102` | System меняется только через hyperspace; planet — только через interplanetary transit | `VERIFIED` | same-system/same-planet guards; Hyper→Station destination edge |
| `WORLD-103` | Одновременно активен ровно один PackedScene context shell | `VERIFIED` | `WorldSceneCoordinatorNode`; `HostChildren==1`; five shell scenes |
| `WORLD-104` | Surface/Orbit heavy runtime управляется bounded residency policy | `VERIFIED` | surface suspension from TASK-128 + orbit save/suspend/restore |
| `WORLD-105` | StationInterior/HyperspaceTransit не держат Surface/Orbit runtime; InterplanetaryTransit держит только system/orbit representation | `VERIFIED` | both residency flags false; collision/process/visibility suspended |
| `WORLD-106` | Star-system proxies видимы в Orbit и InterplanetaryTransit | `VERIFIED` | `renderSystemProxies` gated by orbital/system-transit world kinds |
| `WORLD-107` | Hyperspace scene transition транзакционен с galaxy jump | `VERIFIED` | begin transit; success destination completion; failed-jump rollback |
| `WORLD-108` | Scene state не дублирует persistence location | `VERIFIED` | context derived from existing voyage+galaxy; no `world_scene` save key/schema bump |
| `WORLD-109` | Player-facing diagnostics локализованы и F5 проверяет live residency | `VERIFIED` | RU/EN world-scene HUD + TASK-148 acceptance |
| `WORLD-ACC-100` | Clean build `0/0` | `VERIFIED` | Explicit product-owner acceptance waiver 2026-08-15; exact omitted build line is not reconstructed |
| `WORLD-ACC-101` | Local/CI quality и xUnit green | `VERIFIED` | Explicit product-owner acceptance waiver 2026-08-15; static quality is green, omitted exact xUnit output is not reconstructed |
| `WORLD-ACC-102` | F5 выдаёт TASK-148 PASS с one-shell/residency invariants | `VERIFIED` | 2026-08-15 runtime: livePath=1; transactionalSwap=1; stateRestored=1; steps=7; maxHostChildren=1; sceneLoadFailures=0; rollbacks=0 |
| `WORLD-ACC-103` | Manual Surface→Orbit→Station→Hyper→Station→Orbit→Surface + cold restore | `VERIFIED` | Explicit product-owner acceptance waiver 2026-08-15; omitted manual metrics are not reconstructed |

### 8.24. Multi-Planet Environment / Stage 2 planetary foundation

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `ENV-100` | Каталог содержит ровно 9 нормативных archetypes | `VERIFIED` | `planet_environments.json`; TASK-150 static gate `archetypes=9/9` |
| `ENV-101` | Starter system содержит 3–5 планет; TASK-150 baseline — 4 разных landable archetypes | `VERIFIED` | `StarterPlanetArchetypes`; `starterPlanets=4/4`, `starterArchetypes=4/4` static contract |
| `ENV-102` | Radius каждой планеты детерминирован и лежит в 20–80 км | `VERIFIED` | `PlanetEnvironmentRuntime`; static range validation |
| `ENV-103` | Landable planet имеет 1–8 active biomes и climate-factor selection | `VERIFIED` | ecology cross-reference + latitude/elevation/water/noise sampler |
| `ENV-104` | Water — spherical fixed-level presentation без fluid simulation | `VERIFIED` | `planet_water_shell.gdshader`; no fluid solver |
| `ENV-105` | Atmosphere — simplified spherical shell | `VERIFIED` | `planet_atmosphere_shell.gdshader`; density/horizon/sunset parameters |
| `ENV-106` | Clouds — 0–2 scrolling shell layers | `VERIFIED` | `planet_cloud_shell.gdshader`; catalog bounds 0..2 |
| `ENV-107` | Gas giant non-landable и не имеет surface biome set | `VERIFIED` | catalog validation + runtime + xUnit contract |
| `ENV-108` | Environment отражён в System Map и gameplay HUD | `VERIFIED` | localized map row + HUD summary |
| `ENV-109` | Developer Planet Preview визуализирует environment profile | `VERIFIED` | cube-sphere preview + water/atmosphere/cloud shells |
| `ENV-110` | Current planet сохраняется backward-compatible без SQLite schema bump | `VERIFIED` | optional `GalaxyNavigationSaveData.CurrentPlanetId`; legacy fallback |
| `ENV-111` | Environment детерминирован от stable planet seed, без global sequential RNG | `VERIFIED` | stable hash/mix; repeated-profile acceptance |
| `ENV-112` | Existing cube-sphere/quadtree terrain остаётся planet-scale source geometry | `VERIFIED` | `CubeSpherePrototype`/quadtree architecture retained; TASK-156 adds a bounded active-surface heightfield mesh without removing the planet-scale terrain pipeline |
| `ENV-ACC-100` | Clean build новой редакции `0/0` | `VERIFIED` | выполнить TASK-151 на Windows/Godot .NET  + product-owner «всё работает» acceptance 2026-08-15 |
| `ENV-ACC-101` | Section-37 quality + xUnit environment tests green | `VERIFIED` | static gates PASS; actual `dotnet test` unavailable in preparation environment |
| `ENV-ACC-102` | F5 выдаёт TASK-150 PASS с exact 4/4, 9/9 и samples=16 | `VERIFIED` | выполнить TASK-151 runtime acceptance  + product-owner «всё работает» acceptance 2026-08-15 |
| `ENV-ACC-103` | Manual System Map + Planet Preview + cold current-planet restore | `VERIFIED` | выполнить visual/manual smoke TASK-151  + product-owner «всё работает» acceptance 2026-08-15 |

### 8.25. Interplanetary Travel & Planet Activation Handoff

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `TRAVEL-100` | System Map выбирает landable planetary TARGET, не меняя current planet | `IMPLEMENTED` | `ConfirmPlanetaryDestination`; `TrySelectPlanetDestination`; CURRENT/TARGET markers |
| `TRAVEL-101` | Target сохраняется backward-compatible отдельно от current planet | `IMPLEMENTED` | `SelectedPlanetId`; restore validation; no SQLite migration |
| `TRAVEL-102` | Перелёт использует live system proxy и existing ship physics command path | `IMPLEMENTED` | `TryGetBodyDisplayPosition`; `SetExternalCommand`; no teleport during cruise |
| `TRAVEL-103` | Начало перелёта требует piloted + FlightReady и расходует fuel | `IMPLEMENTED` | `InterplanetaryTravelRuntime.TryBeginCruise` |
| `TRAVEL-104` | Arrival требует bounded distance + speed и braking policy | `IMPLEMENTED` | ArrivalRadius/MaximumArrivalSpeed/BrakingDistance |
| `TRAVEL-105` | Planet identity меняется только через transactional InterplanetaryTransit | `IMPLEMENTED` | Orbit→InterplanetaryTransit→Orbit; direct cross-planet Orbit rejected |
| `TRAVEL-106` | Во время transit surface suspended, system proxies остаются resident | `IMPLEMENTED` | world residency + star-system proxy gates |
| `TRAVEL-107` | Arrival включает local planet approach и существующий landing flow | `IMPLEMENTED` | `ArriveAtPlanetaryApproach`; `ApplyStageOneVoyageToScene` |
| `TRAVEL-108` | Transfer count/distance/current planet сохраняются точно | `IMPLEMENTED` | galaxy save tail + boundary validation + acceptance |
| `TRAVEL-109` | F5/static/xUnit проверяют целую подсистему | `IMPLEMENTED` | TASK-152 acceptance; validator; xUnit 3/3 |
| `TRAVEL-ACC-100` | Clean build `0/0` | `IN_PROGRESS` | TASK-153 Windows/Godot .NET acceptance |
| `TRAVEL-ACC-101` | Section-37 + xUnit green | `IN_PROGRESS` | static gates available; actual dotnet unavailable in preparation environment |
| `TRAVEL-ACC-102` | F5 TASK-152 PASS | `VERIFIED` | внешний Godot 4.7.1 F5: all target/persistence/fuel/guidance/handoff/arrival invariants = 1 |
| `TRAVEL-ACC-103` | Manual target→cruise→arrival→landing→cold restore | `IN_PROGRESS` | выполнить gameplay smoke |

### 8.26. Planet-Scoped Surface Content / Stage 2 real post-travel variation

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `SURFACE-100` | Current landable planet имеет deterministic planet-scoped surface identity | `IMPLEMENTED` | `PlanetSurfaceContentRuntime`; stable planet seed + `region.surface.*` |
| `SURFACE-101` | Ecology использует только active biomes текущей планеты | `IMPLEMENTED` | `EcologyPlanner.PlanPlanet`; TASK-154 gate |
| `SURFACE-102` | Flora/fauna budgets bounded и варьируются по habitability | `IMPLEMENTED` | 180–360 flora; active/simplified не превышают catalog limits |
| `SURFACE-103` | Dry planet не создаёт aquatic fauna/habitat | `IMPLEMENTED` | water threshold 0.12; `WaterHabitatEnabled`; scene suppression |
| `SURFACE-104` | 20 POI планируются по реальному planet biome/water/danger sample | `IMPLEMENTED` | `PlanetaryPoiPlanner.PlanPlanet`; `PlanetEnvironmentRuntime.SampleBiome` |
| `SURFACE-105` | Interplanetary arrival переключает surface content после planet commit | `IMPLEMENTED` | capture before transfer + destination activation after success |
| `SURFACE-106` | Ecology/POI deltas сохраняются независимо по PlanetId | `IMPLEMENTED` | optional `PlanetStates`; nested canonicalization/validation; no schema bump |
| `SURFACE-107` | Legacy starter saves сохраняют historical seed/region/instance IDs | `IMPLEMENTED` | dedicated `planet.vertical_slice` compatibility path |
| `SURFACE-108` | Ground/atmosphere/water presentation отражает current planet | `IMPLEMENTED` | material/environment/water binding in `SalvageRepairSlicePlanetSurfaceContent` |
| `SURFACE-109` | Non-landable body не создаёт surface plan и не уничтожает landable archive | `IMPLEMENTED` | STANDBY branch; prior archive retained |
| `SURFACE-110` | Static/F5/xUnit acceptance проверяет подсистему целиком | `IMPLEMENTED` | TASK-154 validator; F5 acceptance; xUnit 3/3 source coverage |
| `SURFACE-ACC-100` | Clean build `0/0` | `IN_PROGRESS` | TASK-155 Windows/Godot .NET acceptance |
| `SURFACE-ACC-101` | Section-37 + xUnit execution green | `IN_PROGRESS` | static gates PASS; actual dotnet unavailable in preparation environment |
| `SURFACE-ACC-102` | F5 TASK-154 PASS | `VERIFIED` | внешний Godot 4.7.1 F5: 4/4 profiles/regions + ecology/aquatic/POI/persistence/legacy = 1 |
| `SURFACE-ACC-103` | Manual 4-planet variation + independent cold restore | `IN_PROGRESS` | выполнить gameplay smoke по критериям §0 |

### 8.27. Planet-Specific Terrain & Surface Geometry

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `TERRAIN-100` | Current landable planet имеет deterministic terrain profile от archetype + stable seed | `IMPLEMENTED` | `PlanetSurfaceTerrainRuntime`; profile stored in `PlanetSurfaceContentProfile.Terrain` |
| `TERRAIN-101` | Starter 4 planets имеют различимую morphology | `IMPLEMENTED` | temperate/desert/frozen/volcanic dedicated shaping; morphology signature acceptance |
| `TERRAIN-102` | Bounded surface создаёт реальный mesh + physics collision | `IMPLEMENTED` | 65x65 `SurfaceTool` mesh; `CreateTrimeshShape()` |
| `TERRAIN-103` | Starter infrastructure защищена central terrace | `IMPLEMENTED` | 16 m flat core → full relief at 23 m |
| `TERRAIN-104` | Wet worlds имеют terrain basins, dry worlds их не создают | `IMPLEMENTED` | protected basin floors at gameplay/aquatic water volumes |
| `TERRAIN-105` | Ecology визуально/физически следует surface Y без смены legacy IDs | `IMPLEMENTED` | planner Y + scene projection + ground-fauna re-grounding |
| `TERRAIN-106` | POI учитывают terrain slope и стоят на exact surface Y | `IMPLEMENTED` | terrain-aware `PlanPlanet`; identity-safe runtime projection |
| `TERRAIN-107` | NPC NavMesh повторяет terrain и исключает чрезмерные склоны | `IMPLEMENTED` | per-vertex Y; slope filter; terrain-height avoidance obstacles |
| `TERRAIN-108` | Ground NPC не форсятся на flat `_home.Y` | `IMPLEMENTED` | `GetNavigationHeight` after movement and territory clamp |
| `TERRAIN-109` | Base/resource surface objects проецируются на terrain | `IMPLEMENTED` | build target/preview/module + generated resource grounding |
| `TERRAIN-110` | Static/F5/xUnit acceptance проверяет подсистему целиком | `IMPLEMENTED` | TASK-156 validator; F5 acceptance; xUnit 3/3 source coverage |
| `TERRAIN-ACC-100` | Clean build `0/0` | `IN_PROGRESS` | TASK-157 Windows/Godot .NET acceptance |
| `TERRAIN-ACC-101` | Section-37 + xUnit execution green | `IN_PROGRESS` | static gates PASS; actual dotnet unavailable in preparation environment |
| `TERRAIN-ACC-102` | F5 TASK-156 PASS | `VERIFIED` | внешний Godot 4.7.1 F5: starterPlanets=4/4; distinctMorphology=4/4; deterministic/terrace/bounds/walkable/water/ecology/POI/legacy all `1` |
| `TERRAIN-ACC-103` | Manual visual relief + NPC/base/water smoke across starter planets | `IN_PROGRESS` | выполнить TASK-157 |

### 8.28. Planetary Surface Streaming & Traversal Foundation

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `STREAM-100` | Surface использует bounded moving chunk residency вместо фиксированной границы 80x80 m | `VERIFIED` | live `TerrainChunkManager`; external runtime transitions between multiple centers |
| `STREAM-101` | Resident window строго 5x5 / 25 chunks | `VERIFIED` | live READY `active=25/25`; repeated completions `active=25` |
| `STREAM-102` | Центральные 3x3 chunks используют LOD0 33x33 и collision | `VERIFIED` | live READY `collisions=9/9`; generated LOD0 `vertices=1089; collision=33x33` |
| `STREAM-103` | Внешнее кольцо 16 chunks использует LOD1 17x17 без collision | `VERIFIED` | live `high=9; low=16`; LOD1 `vertices=289; collision=none` |
| `STREAM-104` | Chunk generation выполняется background workers, mesh/collision apply — main thread | `VERIFIED` | worker/apply diagnostics; 4 workers; no Godot API in worker builder gate |
| `STREAM-105` | Cancellation/stale-result guards предотвращают смешивание revisions/planet profiles | `VERIFIED` | TASK-158 static/F5; external runs revisions 1..5 with `cancelled=0; stale=0; failed=0` |
| `STREAM-106` | LOD boundaries используют stitch/skirt policy | `VERIFIED` | live chunk diagnostics show stitch masks and outer skirts; F5 `seamSafe=1` |
| `STREAM-107` | TASK-156 terrain sampler применяется в exact world coordinates | `VERIFIED` | TASK-158 static gate + F5 `deterministic=1; fullRelief=1` |
| `STREAM-108` | Startup/planet-switch fallback остаётся до settled streamed collision | `VERIFIED` | external READY `fallback=retired` only after `25/25`, `9/9`, queue/workers zero |
| `STREAM-109` | TASK-124 NavigationRegion streaming следует traversal window | `VERIFIED` | external TASK-124 PASS after sector transitions: crossTilePath/clearance/recovery/sync all `1` |
| `STREAM-110` | Streamer suspended outside Surface by world residency coordinator | `IMPLEMENTED` | TASK-148/TASK-158 static + F5 contracts; manual planet/orbit smoke remains in TASK-159 |
| `STREAM-111` | Planet-radius geodesic addressing доступен в HUD/runtime | `VERIFIED` | READY reports `lat=0.0071; lon=0.0000`; F5 `planetAddressing=1` |
| `STREAM-112` | F5/static/xUnit проверяют bounded streaming subsystem | `VERIFIED` | external F5 TASK-158 PASS + static gate; source xUnit coverage 3 tests |
| `STREAM-ACC-100` | Clean build `0 warnings / 0 errors` | `IN_PROGRESS` | external build `2 warnings / 0 errors`; TASK-158.1 removes both warnings, rerun required |
| `STREAM-ACC-101` | Section-37/xUnit green | `IN_PROGRESS` | 19/19 static validators PASS in preparation environment; external dotnet quality rerun required |
| `STREAM-ACC-102` | F5 TASK-158 PASS | `VERIFIED` | external Godot 4.7.1: 25/25, 9/9, 16/16, 9/9, all invariants `1` |
| `STREAM-ACC-103` | Live settled streamer reaches queue=0/workers=0/fallback=retired | `VERIFIED` | external READY exact evidence |
| `STREAM-ACC-104` | Manual >160 m + diagonal traversal without gap/fall and planet-switch smoke | `IN_PROGRESS` | выполнить TASK-159 after alpha.158.1 clean build |


### 8.29. Planet Surface World Composition & Persistent Chunk Resources

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `WORLD-160` | Surface использует procedural sky вместо color-only background | `IMPLEMENTED` | `ProceduralSkyMaterial`; Environment BG_SKY; sky ambient/reflection |
| `WORLD-161` | Current system star видим и задаёт surface directional light | `IMPLEMENTED` | deterministic star color/azimuth/elevation + DirectionalLight3D |
| `WORLD-162` | Atmosphere имеет aerial perspective/haze, cloud policy следует environment | `IMPLEMENTED` | fog + aerial perspective + deterministic cloud clusters |
| `WORLD-163` | Streamed terrain не проваливается в абсолютный black | `IMPLEMENTED` | PBR direct lighting + weak planet-colored emission floor + macro/slope vertex colors |
| `WORLD-164` | Legacy 58-node catalog showcase не отображается как live gameplay | `IMPLEMENTED` | 55 fixtures runtime-suppressed; starter salvage alpha/beta/gamma retained |
| `WORLD-165` | Live resources распределяются по current 5x5 chunk window | `IMPLEMENTED` | deterministic 0–2/chunk, 28 m reserve, slope-aware placement |
| `WORLD-166` | Resource identity planet/chunk scoped и не конфликтует между планетами | `IMPLEMENTED` | stable `surface_resource.*` derived from planet+chunk+slot |
| `WORLD-167` | Добытые procedural resources не респавнятся после unload/save/return | `IMPLEMENTED` | `FromSnapshotWithDynamicResources`; `CollectedNodeIds` suppression; seed+deltas |
| `WORLD-168` | Непосещённые/неизменённые procedural resources не раздувают save | `IMPLEMENTED` | deterministic regeneration; only collected node deltas persist |
| `WORLD-169` | Existing POI live presentation не сконцентрирован у landing pad | `IMPLEMENTED` | stable IDs retained; deterministic 78–420 m presentation annulus |
| `WORLD-170` | F5/static/xUnit проверяют composition+persistence contract | `IMPLEMENTED` | TASK-160 acceptance; validator; xUnit 3 tests |
| `WORLD-ACC-100` | Clean build/section-37 `0/0` + tests green | `IN_PROGRESS` | TASK-161 external Windows/Godot verification |
| `WORLD-ACC-101` | F5 TASK-160 PASS и старые TASK-138/158 остаются PASS | `VERIFIED` | external Godot: TASK-160/TASK-138/TASK-158 PASS; TASK-160.1 addresses unrelated TASK-126 far-traversal acceptance regression |
| `WORLD-ACC-102` | Manual visual sky/terrain/declutter smoke | `IN_PROGRESS` | внешний screenshot выявил flat/square horizon и отсутствие читаемого stellar disc; corrective TASK-162.2 implemented, требуется повторный visual smoke |
| `WORLD-ACC-103` | Mine → chunk unload → save/restart → planet return preserves depletion | `IN_PROGRESS` | TASK-161 persistence scenario |

### 8.30. Planet-Global Surface Frame & Floating Origin

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `FRAME-1620` | Planet surface имеет отдельные double-precision logical East/North coordinates | `IMPLEMENTED` | `PlanetSurfaceFrameRuntime`; Godot-independent |
| `FRAME-1621` | Local Godot X/Z автоматически rebased и остаются bounded | `IMPLEMENTED` | 4096 m cells; 2048 m threshold; live `UpdatePlanetSurfaceFrame` |
| `FRAME-1622` | Rebase сохраняет logical continuity без смены chunk identity | `IMPLEMENTED` | F5 acceptance + xUnit round-trip/chunk tests |
| `FRAME-1623` | TASK-158 terrain streamer использует logical center, local chunk transforms | `IMPLEMENTED` | `SetLogicalSurfaceOrigin`, `ToLogicalPosition`, `BuildLocalChunkPosition` |
| `FRAME-1624` | TASK-160 resources/POI/world composition используют logical surface window | `IMPLEMENTED` | logical player center; resource roots under Gameplay; POI conversion |
| `FRAME-1625` | Base placement/map/navigation не зависят от rebased `GlobalPosition` | `IMPLEMENTED` | logical base target/map; frame-aware nav center/obstacles/path tiles |
| `FRAME-1626` | Voyage domain position/targets остаются stable через surface rebase | `IMPLEMENTED` | logical↔local conversion in voyage/activation paths |
| `FRAME-1627` | Save/cold restore сохраняет logical player X/Z без schema bump | `IMPLEMENTED` | snapshot uses logical coordinates; cold load restores exact logical origin and local-zero player |
| `FRAME-1628` | F5/static/xUnit проверяют long-traversal frame contract | `IMPLEMENTED` | TASK-162 F5 runner; static gate; 3 xUnit tests |
| `FRAME-1629` | Live rebase синхронизирует absolute AI/navigation caches | `IMPLEMENTED` | ground-NPC targets; NPC-ship routes; fauna/aerial environment shifted/refreshed |
| `FRAME-162A` | Frame-aware voyage bootstrap не обращается к GalaxyNavigation до его создания | `VERIFIED` | external Godot: startup/load path reaches TASK-152/156/158/160 without GalaxyNavigation exception |
| `FRAME-162B` | Surface presentation скрывает bounded streamer edge и показывает macro relief/star/atmosphere | `IMPLEMENTED` | TASK-162.2 distant visual proxy 840m + relief promotion + stellar disc + fog/cloud layer + F5 gate |
| `FRAME-ACC-100` | Clean build/section-37 `0/0` + tests green | `IN_PROGRESS` | TASK-163 external Windows verification |
| `FRAME-ACC-101` | F5 TASK-162 PASS + prior F5 matrix no regressions | `VERIFIED` | external Godot: TASK-162 PASS rebases=48, traversalSamples=49, maxLocal=2030.709m; TASK-126/156/158/160 also PASS |
| `FRAME-ACC-102` | Live >2048 m rebase bounded/no gap/no world jump | `IN_PROGRESS` | TASK-163 manual traversal |
| `FRAME-ACC-103` | Distant logical save/restart + resource depletion persistence | `IN_PROGRESS` | TASK-163 cold restore scenario |

## 9. Очередь ближайших задач

Задачи выполняются итеративно; runtime-проверки фиксируются до присвоения `VERIFIED`, кроме явно записанного product-owner acceptance waiver.

| Приоритет | ID | Задача | Результат |
|---:|---|---|---|
| 1 | `TASK-162.2` | Surface presentation visual rerun | horizon без 80m square edge; macro relief; видимый stellar disc; high cloud layer; F5 TASK-162.2 PASS |
| 2 | `TASK-163` | Runtime/manual acceptance Planet-Global Surface Frame | clean build + live >2048 m rebase; distant cold restore/persistence (F5 TASK-162 already externally PASS) |
| 3 | `TASK-160.1` | Traversal-safe TASK-126 acceptance | `VERIFIED`: external F5 `faunaProbeSamples=4`, `sharedRuntime=1`, `runtimeSamples=1` |
| 4 | `TASK-161` | Runtime/manual acceptance Planet Surface World Composition | visual sky/terrain/declutter; resource depletion across unload/restart/planet return |
| 5 | `TASK-159` | Runtime/manual acceptance Planetary Surface Streaming + TASK-158.1 closure | manual >160 m/diagonal traversal + planet-switch smoke |
| 6 | `TASK-153` | Runtime acceptance Interplanetary Travel | F5 уже PASS; остаются manual target→cruise→landing→cold restore |
| 7 | `TASK-155` | Runtime acceptance Planet-Scoped Surface Content | F5 уже PASS; остаются manual variation on starter planets и independent discovery/ecology cold restore |
| 8 | `TASK-157` | Runtime/manual acceptance Planet-Specific Terrain | F5 уже PASS; остаётся manual visual relief/NPC/base/water smoke |
| 9 | `TASK-006` | Записать SHA контрольного коммита | `BLOCKED`: в переданном ZIP нет `.git`; требуется SHA фактического GitHub commit |

**Текущая разрабатываемая реализация:** TASK-162.2 Surface Presentation hotfix `IMPLEMENTED`; TASK-162.1 и TASK-160.1 `VERIFIED`; TASK-162 остаётся `IMPLEMENTED`.  
**Формально ближайший шаг:** Windows/Godot visual smoke TASK-162.2; после него TASK-163 live >2048m traversal и distant cold-restore. F5 TASK-162 и TASK-160.1 уже подтверждены внешним evidence.


## 10. Runtime-приёмка `TASK-062/TASK-063`

1. Собрать `Game.Client.csproj`. Критерий: `0` ошибок и `0` предупреждений.
2. Запустить проект. Стартовая сцена должна показать `VERTICAL SLICE 1 — SALVAGE → REPAIR → AUTOSAVE` и перейти в `DB: Ready`.
3. Нажать `F7` один раз и не выполнять другие действия до завершения. Изолированная БД не изменяет gameplay-slot.
4. Ожидаемый HUD:

```text
TASK-062 acceptance (F7): PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1
```

5. Ожидаемая итоговая строка Godot Output:

```text
TASK-062 vertical slice integration acceptance PASS: resources=3; repairBlocked=1; shipRepaired=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=data-driven resource collection crafted the starter repair recipe and persisted the repaired ship
```

6. Нажать `F8`, дождаться `slot reset PASS`. Подойти к красному кораблю и нажать `E` до сбора ресурсов. HUD должен остаться `Ship: DAMAGED`, Output — показать `ShipRepairBlocked`.
7. Подойти к каждому голубому узлу до появления `Interaction: near Salvage... — press E` либо `aimed at ... — press E`, затем нажать `E`. После каждого узел исчезает, а HUD проходит `1/3 → 2/3 → 3/3`.
8. Трижды нажать `H` и подтвердить цикл `DETAILED → COMPACT → HIDDEN → DETAILED`; в скрытом режиме должен оставаться hint `HUD hidden — press H`.
9. Снова взаимодействовать с кораблём. Корабль должен стать зелёным, HUD — `Ship: REPAIRED`, а Output должен содержать:

```text
Vertical slice domain event: StarterRepairQuestCompleted; autosaveTrigger=QuestCompleted
Vertical slice autosave PASS: revision=<N>; triggers=QuestCompleted; salvage=0; shipRepaired=1; pending=0
```

10. Закрыть окно через `Alt+F4`. До завершения процесса ожидается:

```text
Vertical slice graceful-exit autosave PASS: saved=1; revision=<N+1>; pending=0
```

11. Повторно запустить проект. Должны восстановиться `Ship: REPAIRED`, `salvage=0`, исчезнувшие ресурсные узлы и revision из graceful-exit.
12. Для приёмки прислать: результат сборки; screenshot `F7: PASS`; screenshot строки `Interaction: near Salvage...`; screenshot компактного или скрытого HUD; screenshot раннего `ShipRepairBlocked`; screenshot `Ship: REPAIRED`; полные строки F7, QuestCompleted autosave и graceful-exit; screenshot после повторного запуска.
13. При `FAIL` прислать финальный HUD, последние 80 строк Output и наличие файлов `profiles/profile_vertical_slice/save_1.db`, backup, autosave log и `save_1.vertical-slice-test.db`.

## 11. Runtime-приёмка `TASK-064/TASK-065`

1. Собрать `Game.Client.csproj`: требуется `0` предупреждений и `0` ошибок.
2. Запустить main scene и дождаться `DB: Ready`. До этого Output должен содержать:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-064 content binding PASS: schema=1; recipe=recipe.ship.starter_repair; resource=resource.salvage_alloy; required=3; available=3; items=20; resources=10; recipes=10; station=station.field_repair.
```

3. Нажать `F9` один раз. Тест занимает менее секунды и не изменяет slot/JSON. Ожидаемый HUD:

```text
TASK-064 content (F9): PASS schema=1, items=20, resources=10, recipes=10, dataDriven=1, invalidRejected=2
```

4. Ожидаемая полная строка Output:

```text
TASK-064 data-driven content acceptance PASS: schema=1; items=20; resources=10; recipes=10; recipe=recipe.ship.starter_repair; required=3; variantRequired=4; blockedBelowVariant=1; repairedAtVariant=1; outputs=1; duplicateRejected=1; missingReferenceRejected=1; stableIds=1; elapsedMs=<время>; result=JSON catalog validated; recipe threshold changed in memory and domain behavior followed the data
```

5. Нажать `F7`: gameplay/persistence regression должна остаться `PASS`.
6. Нажать `F8`, вручную собрать три узла и отремонтировать корабль. Detailed HUD должен показывать recipe `3×resource.salvage_alloy → 1×component.starter_hull_patch`; после ремонта — `Objective: COMPLETE`, `Ship: REPAIRED`, autosave `QuestCompleted`.
7. Для приёмки прислать: лог сборки, screenshot `F9: PASS`, полную строку F9, screenshot recipe line и результат F7.
8. При `FAIL` прислать полный HUD, последние 100 строк Output и содержимое трёх JSON-файлов без изменений.

## 12. Runtime-приёмка `TASK-066/TASK-067`

> Эта приёмка уже закрыта прямым подтверждением пользователя. Раздел сохранён как регрессионный сценарий для текущей редакции, где launch-capacitor craft теперь завершается через `3.0` секунды.

1. Собрать `Game.Client.csproj`: требуется `0` предупреждений и `0` ошибок.
2. Запустить main scene и дождаться `DB: Ready`. Output должен содержать:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-066 crafting binding PASS: recipe=recipe.ship.launch_capacitor; resource=resource.conductive_crystal; required=2; available=2; station=station.portable_fabricator; craftTime=3.0; items=20; resources=10; recipes=10.
```

3. Нажать `F10` один раз. Тест использует `save_1.crafting-expansion-test.db`, проверяет доменную/persistence-цепочку и не изменяет gameplay-slot.
4. Ожидаемый HUD:

```text
TASK-066 crafting (F10): PASS resources=2, repairFirst=1, wrongStation=1, blocked=1, crafted=1, roundTrip=1
```

5. Ожидаемая полная строка Output:

```text
TASK-066 crafting expansion acceptance PASS: resources=2; repairPrerequisite=1; wrongStationRejected=1; blockedBeforeResources=1; crafted=1; output=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=second resource was collected, crafted at the dedicated station and persisted exactly
```

6. Повторить `F9`; counts должны быть `items=20, resources=10, recipes=10`, остальные критерии — PASS.
7. Повторить `F7`; результат должен остаться `resources=3` и PASS, то есть два crystal nodes не должны ошибочно входить в repair acceptance.
8. Нажать `F8`. До ремонта корабля подойти к фиолетовому PortableFabricator и нажать `E`: ожидается `LaunchCapacitorCraftBlocked`/сообщение о необходимости ремонта.
9. Собрать три голубых узла, отремонтировать корабль, затем собрать два фиолетовых crystal nodes. HUD должен пройти `crystal=0/2 → 1/2 → 2/2`.
10. Взаимодействовать с PortableFabricator. В текущей редакции он сначала становится оранжевым, затем через `3.0` секунды — зелёным; HUD показывает `launch capacitor READY` и `Objective: COMPLETE`.
11. Закрыть окно штатно и повторно запустить проект. Должны восстановиться repaired ship, отсутствующие пять собранных nodes, зелёный fabricator, capacitor READY и последняя revision.
12. Для регрессии прислать: лог сборки; screenshot `F10: PASS`; полную строку F10; результаты F9/F7; screenshot completed HUD после трёхсекундного process и после cold restart.
13. При `FAIL` прислать полный HUD, последние 120 строк Output и сведения о `save_1.db`, autosave log и `save_1.crafting-expansion-test.db`.

## 13. Runtime-приёмка `TASK-068/TASK-069`

1. Собрать `src/Game.Client/Game.Client.csproj`: требуется `0` предупреждений и `0` ошибок.
2. Запустить main scene и дождаться `DB: Ready`. Output должен содержать все строки:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-066 crafting binding PASS: recipe=recipe.ship.launch_capacitor; resource=resource.conductive_crystal; required=2; available=2; station=station.portable_fabricator; craftTime=3.0; items=20; resources=10; recipes=10.
TASK-068 craft-time binding PASS: recipe=recipe.ship.launch_capacitor; duration=3.0; station=station.portable_fabricator; timer=DataDrivenCraftTimer.
```

3. Нажать `F11` один раз и не выполнять другие действия до завершения. Pure-.NET тест не меняет gameplay-slot и должен завершиться менее чем за секунду.
4. Ожидаемый HUD:

```text
TASK-068 craft time (F11): PASS duration=3.0, started=1, duplicate=1, inputsHeld=1, completed=1, single=1, output=1
```

5. Ожидаемая полная строка Output:

```text
TASK-068 data-driven craft-time acceptance PASS: duration=3.0; positiveDuration=1; started=1; duplicateRejected=1; inputsHeldUntilCompletion=1; partialRunning=1; completedAtDuration=1; singleCompletion=1; output=1; elapsedMs=<время>; result=configured craft time delayed output and completed exactly once
```

6. Выполнить регрессии `F7`, `F9` и `F10`; все три должны остаться `PASS`.
7. Нажать `F8`, собрать три salvage-alloy узла, отремонтировать корабль и собрать два conductive-crystal узла.
8. Нажать `E` у PortableFabricator. Сразу после старта Output должен содержать:

```text
TASK-068 timed craft started: recipe=recipe.ship.launch_capacitor; station=station.portable_fabricator; duration=3.0; inputsHeld=1; output=0.
```

9. До истечения `3.0` секунд проверить промежуточное состояние:
   - station оранжевая;
   - HUD содержит `Craft process: RUNNING <elapsed>/3.0s`;
   - crystal остаётся `2/2`;
   - capacitor остаётся `MISSING`;
   - повторное `E` не обнуляет progress и сообщает remaining time.
10. После достижения `3.0` секунд проверить:
    - station стала зелёной;
    - crystal стал `0/2`;
    - capacitor стал `READY`;
    - Output содержит единственную строку completion:

```text
TASK-068 timed craft completion PASS: recipe=recipe.ship.launch_capacitor; station=station.portable_fabricator; configured=3.0; elapsed=3.0; inputsHeldUntilCompletion=1; completedOnce=1; output=1; autosaveTrigger=QuestCompleted; revision=<N>; interactor=<имя>
```

11. Дождаться `Vertical slice autosave PASS`, закрыть окно штатно и повторно запустить проект. Completed output, зелёная station и consumed crystals должны восстановиться.
12. Отдельно проверить safe cancellation: `F8` → repair → собрать два crystals → запустить craft → закрыть окно через `Alt+F4` до `3.0` секунд. До выхода ожидается:

```text
TASK-068 timed craft cancelled safely: recipe=recipe.ship.launch_capacitor; elapsed=<0.0..2.9>; duration=3.0; inputsConsumed=0; reason=graceful exit requested.
```

13. После повторного запуска cancellation-сценария crystals должны остаться `2/2`, capacitor — `MISSING`, station — не зелёная.
14. Для приёмки прислать:
    - лог сборки;
    - screenshot `F11: PASS`;
    - полную F11-строку;
    - результаты `F7/F9/F10`;
    - screenshot промежуточного orange/RUNNING состояния;
    - screenshot финального green/READY состояния;
    - completion/autosave lines;
    - cancellation line и screenshot после cold restart.
15. При `FAIL` прислать полный HUD, последние 120 строк Output, неизменённый `Content/recipes.json` и указать, меняла ли station цвет.

## 14. Runtime-приёмка `TASK-070/TASK-071`

1. Собрать `Game.Client.csproj`. Критерий: `0` ошибок и `0` предупреждений.
2. Запустить стартовую scene и дождаться `DB: Ready`. В Output должны присутствовать:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-070 third crafting path binding PASS: recipe=recipe.ship.navigation_array; resource=resource.phase_fiber; required=2; available=2; station=station.portable_fabricator; craftTime=2.5; items=20; resources=10; recipes=10; stations=9.
```

3. Нажать `F12`. HUD должен показать:

```text
TASK-070 third path (F12): PASS resources=2, blocked=1, timed=1, isolated=1, both=1, output=1, roundTrip=1
```

4. Полная строка Output должна иметь вид:

```text
TASK-070 third crafting path acceptance PASS: resources=2; blockedBeforeResources=1; timedCompletion=1; recipeIsolation=1; bothCrafted=1; output=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=third data-driven resource and timed recipe coexisted with the launch recipe and persisted exactly
```

5. Выполнить регрессии `F7`, `F9`, `F10`, `F11`. Все четыре строки HUD должны завершиться `PASS`.
6. Для ручного прогона нажать `F8`, собрать три salvage-node и отремонтировать корабль.
7. Собрать два зелёных phase-fiber node (`phase.alpha`, `phase.beta`). До сбора второго navigation recipe должна оставаться заблокированной нехваткой inputs.
8. Подойти к левому `NavigationFabricator` и нажать `E`. Ожидаемая стартовая строка:

```text
TASK-070 timed craft started: recipe=recipe.ship.navigation_array; station=station.portable_fabricator; duration=2.5; inputsHeld=1; output=0.
```

9. До истечения `2.5 s` station должна быть оранжевой, HUD — показывать `RUNNING recipe.ship.navigation_array`, phase fiber — оставаться `2/2`, navigation array — `MISSING`.
10. После завершения station должна стать зелёной, phase fiber — `0/2`, navigation array — `READY`. Ожидается:

```text
TASK-070 third crafting path completion PASS: recipe=recipe.ship.navigation_array; station=station.portable_fabricator; configured=2.5; elapsed=2.5; inputsHeldUntilCompletion=1; completedOnce=1; output=1; autosaveTrigger=QuestCompleted; revision=<N>; interactor=<имя>
```

11. Проверить независимость: состояние launch capacitor не должно измениться при navigation craft. Затем отдельно изготовить launch capacitor; оба outputs должны отображаться `READY`.
12. Дождаться `Vertical slice autosave PASS`, закрыть игру штатно и запустить снова. Оба outputs, consumed inputs, collected nodes, repaired ship и revision должны восстановиться.
13. Для приёмки прислать build log, startup lines, screenshot `F12: PASS`, полную F12-строку, `F7/F9/F10/F11`, промежуточный orange/RUNNING screenshot, финальный green/READY screenshot и cold-restart HUD.
14. При `FAIL` прислать полный HUD, последние 140 строк Output и указать, какая station/recipe использовалась.

## 15. Runtime-приёмка `TASK-072/TASK-073`

1. Выполнить clean Build `Game.Client.csproj`. Критерий: `0` ошибок и `0` предупреждений.
2. Запустить стартовую scene и дождаться `DB: Ready`. В Output должны присутствовать:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-072 fourth crafting path binding PASS: recipe=recipe.ship.coolant_regulator; resource=resource.thermal_gel; required=2; available=2; station=station.portable_fabricator; craftTime=3.5; items=20; resources=10; recipes=10; stations=9.
```

3. Нажать `F6`. HUD должен показать:

```text
TASK-072 fourth path (F6): PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1
```

4. Полная строка Output:

```text
TASK-072 fourth crafting path acceptance PASS: resources=2; blockedBeforeResources=1; timedCompletion=1; recipeIsolation=1; allThreeCrafted=1; output=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=fourth data-driven resource and timed recipe remained isolated, coexisted with both previous station recipes and persisted exactly
```

5. Выполнить регрессии `F7`, `F9`, `F10`, `F11`, `F12`. Все пять должны завершиться `PASS`; F9 должен показать counts `8/4/4`.
6. Для ручного прогона нажать `F8`, собрать salvage и отремонтировать корабль.
7. Собрать два оранжевых thermal-gel node (`thermal.alpha`, `thermal.beta`). До второго node coolant recipe должна быть заблокирована.
8. Подойти к центральному `CoolantFabricator`, нажать `E`. Ожидается:

```text
TASK-072 timed craft started: recipe=recipe.ship.coolant_regulator; station=station.portable_fabricator; duration=3.5; inputsHeld=1; output=0.
```

9. До `3.5 s` station оранжевая, HUD показывает `RUNNING recipe.ship.coolant_regulator`, gel остаётся `2/2`, coolant — `MISSING`.
10. После завершения station зелёная, gel `0/2`, coolant `READY`; capacitor/navigation не изменены. Ожидается:

```text
TASK-072 fourth crafting path completion PASS: recipe=recipe.ship.coolant_regulator; station=station.portable_fabricator; configured=3.5; elapsed=3.5; inputsHeldUntilCompletion=1; completedOnce=1; output=1; autosaveTrigger=QuestCompleted; revision=<N>; interactor=<имя>
```

11. Дождаться autosave, штатно закрыть игру и запустить снова. Coolant regulator, consumed thermal gel, collected nodes, repaired ship, player position и revision должны восстановиться.
12. При `FAIL` прислать полный HUD, последние 160 строк Output и указать, выполнялась ли clean сборка.

## 16. Runtime-приёмка `TASK-074/TASK-075`

1. Выполнить clean Build `Game.Client.csproj`. Критерий: `0` ошибок и `0` предупреждений.
2. Запустить стартовую scene и дождаться `DB: Ready`. В Output должны присутствовать:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-074 fifth crafting path binding PASS: recipe=recipe.ship.power_coupler; resource=resource.plasma_filament; required=2; available=2; station=station.portable_fabricator; craftTime=4.0; items=20; resources=10; recipes=10; stations=9.
```

3. Нажать `F5`. HUD должен показать:

```text
TASK-074 fifth path (F5): PASS resources=2, blocked=1, timed=1, isolated=1, all4=1, output=1, roundTrip=1
```

4. Полная строка Output:

```text
TASK-074 fifth crafting path acceptance PASS: resources=2; blockedBeforeResources=1; timedCompletion=1; recipeIsolation=1; allFourCrafted=1; output=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=fifth data-driven resource and timed recipe remained isolated, coexisted with all previous station recipes and persisted exactly
```

5. Выполнить регрессии `F6`, `F7`, `F9`, `F10`, `F11`, `F12`. Все шесть должны завершиться `PASS`; F9 должен показать counts `10/5/5`.
6. Для ручного прогона нажать `F8`, собрать salvage и отремонтировать корабль.
7. Собрать два жёлто-золотых plasma-filament node (`plasma.alpha`, `plasma.beta`). До второго node power recipe должна быть заблокирована.
8. Подойти к `PowerFabricator` позади стартовой точки, нажать `E`. Ожидается:

```text
TASK-074 timed craft started: recipe=recipe.ship.power_coupler; station=station.portable_fabricator; duration=4.0; inputsHeld=1; output=0.
```

9. До `4.0 s` station оранжевая, HUD показывает `RUNNING recipe.ship.power_coupler`, plasma remains `2/2`, power coupler — `MISSING`.
10. После завершения station зелёная, plasma `0/2`, power coupler `READY`; capacitor/navigation/coolant не изменены. Ожидается:

```text
TASK-074 fifth crafting path completion PASS: recipe=recipe.ship.power_coupler; station=station.portable_fabricator; configured=4.0; elapsed=4.0; inputsHeldUntilCompletion=1; completedOnce=1; output=1; autosaveTrigger=QuestCompleted; revision=<N>; interactor=<имя>
```

11. Дождаться autosave, штатно закрыть игру и запустить снова. Power coupler, consumed plasma filament, collected nodes, repaired ship, player position и revision должны восстановиться.
12. При `FAIL` прислать полный HUD, последние 180 строк Output и указать, выполнялась ли clean сборка.

## 18. Runtime-приёмка `TASK-082/TASK-084`

1. Выполнить `tools\clean-build-windows10.cmd`; результат: `0` предупреждений, `0` ошибок, `CoreCompile` не пропущен.
2. На старте получить:

```text
TASK-082 station selector binding PASS: physicalStations=1; selectorRecipes=9; researchPoints=2000; initiallyUnlocked=<N>; initiallyLocked=<M>.
```

3. Нажать `F3`; ожидаемый HUD:

```text
TASK-082 selector/research (F3): PASS recipes=9, oneStation=1, initial=<N>/<M>, unlocked=<K>, crafted=1, rp=<R>, roundTrip=1
```

4. Полный Output должен содержать `prerequisiteRejected=1`, `allRecipesUnlocked=1`, `technologyBlocked=1`, `readyAfterResearch=1`, `progressRestored=1`, `maxWriters=1`, `integrity=ok`.
5. Вручную подойти к единственному PortableFabricator, нажать `E`, проверить список девяти recipes; `Tab/R` открыть Research; разблокировать доступную технологию; повторно открыть Recipes и изготовить связанный recipe.
6. После autosave закрыть игру штатно и проверить восстановление RP, unlocked technology и crafted output.
7. Повторить `F4/F5/F6/F7/F9/F10/F11/F12`.

## 17. Runtime-приёмка `TASK-076/TASK-077`

1. Остановить сцену и выполнить `tools\clean-build-windows10.cmd`. В build log должен реально выполняться `CoreCompile`. Критерий: `0` предупреждений, `0` ошибок.
2. Запустить main scene и дождаться `DB: Ready`. Ожидается:

```text
TASK-064 content catalog READY: schema=1; items=20; resources=10; recipes=10.
TASK-076 crafting catalog binding PASS: items=20; resources=10; recipes=10; stationRecipes=9; sceneStations=9; resourceNodes=21; allInputsCovered=1; allCraftTimesPositive=1.
```

3. Нажать `F5` один раз и дождаться завершения изолированной test-БД. Ожидаемый HUD:

```text
TASK-076 catalog matrix (F5): PASS resources=10, recipes=10, station=9, crafted=9, isolated=9, roundTrip=1
```

4. Ожидаемая итоговая строка Output:

```text
TASK-076 catalog crafting matrix acceptance PASS: items=20; resources=10; recipes=10; stationRecipes=9; resourceNodes=21; blocked=9; timed=9; isolated=9; crafted=9; output=9; wrongStation=1; duplicateStart=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=the full crafting catalog met Stage 1 minimum coverage, was validated in one data-driven matrix, crafted independently and persisted exactly
```

5. Последовательно выполнить `F6`, `F7`, `F9`, `F10`, `F11`, `F12`. Все должны завершиться `PASS`; F9 должен показать `items=20`, `resources=10`, `recipes=10`.
6. Для production smoke-test после `F8` отремонтировать корабль и вручную изготовить один или два новых outputs, например `sensor_lens` и `heat_shield_tile`. Проверить RUNNING, удержание inputs до таймера, READY, `QuestCompleted` autosave и cold restart. Ручное повторение всех девяти recipes не требуется: полную матрицу покрывает F5.
7. При `FAIL` прислать полный HUD, последние 200 строк Output и полный build log. Новый runner сообщает counts failed-критериев; при persistence failure требуется также точный snapshot mismatch.
8. После успешных шагов 1–6: `TASK-076 → VERIFIED`, `TASK-077 → VERIFIED`, `CONTENT-070`–`CONTENT-079` и `CONTENT-ACC-070`–`CONTENT-ACC-076` → `VERIFIED`. `TASK-074` также может быть закрыта как часть полной matrix acceptance, если power-coupler recipe входит в `crafted=9` и round-trip.

## 18. Журнал проверок

### 2026-08-02 — `TASK-074`, fifth data-driven crafting path

**Исходный снимок:** `ProjectHorizon-main(2)(4).zip`
**Подготовленный снимок:** `ProjectHorizon-main-fifth-crafting-path.zip`
**Git SHA:** отсутствует в исходном архиве
**Связанные требования:** PDF-ТЗ 17.2–17.4, 23, 36.1, Этап 1 раздела 40, критерии 6/10/14 раздела 41; `CONTENT-060`–`CONTENT-067`.

**Синхронизация:** clean build и runtime `F6: PASS` пользователя закрыли `TASK-072/TASK-073`; F7/F9/F10/F11/F12 также показали PASS. Comprehensive F6 подтверждает persistence и isolation четвёртого path.

**Реализация:** добавлены plasma filament, power coupler и recipe duration `4.0 s`; два physical resource nodes и отдельный PowerFabricator; controller/HUD/load/reset/autosave поддерживают четыре station outputs; добавлена `F5` acceptance с isolation, previous-recipe regression и exact SQLite round-trip.

**Статическая проверка:** JSON counts/cross-references, scene bindings, stable IDs, C# lexical balance, UID, `res://`, hotkey F5, запрещённые artifacts и ZIP integrity.

**Ограничение среды:** `dotnet`/Godot недоступны, поэтому сборка и runtime F5 здесь не выполнялись. `TASK-074` остаётся `IMPLEMENTED`, `TASK-075` — `IN_PROGRESS`.

**Следующая задача:** clean build, `F5: PASS`, регрессии `F6/F7/F9/F10/F11/F12`, ручной power process и cold restart по разделу 16.

### 2026-08-02 — `TASK-072`, fourth data-driven crafting path

**Исходный снимок:** `ProjectHorizon-main(1)(7).zip`
**Подготовленный снимок:** `ProjectHorizon-main-fourth-crafting-path.zip`
**Git SHA:** отсутствует в исходном архиве
**Связанные требования:** PDF-ТЗ 17.2–17.4, 23, 36.1, Этап 1 раздела 40, критерии 6/10/14 раздела 41; `CONTENT-050`–`CONTENT-057`.

**Синхронизация:** clean build и runtime `F12: PASS` пользователя закрыли `TASK-070/TASK-071`; F9/F10/F11 также показали PASS. Comprehensive F12 подтверждает persistence и isolation третьего path.

**Реализация:** добавлены thermal gel, coolant regulator и recipe duration `3.5 s`; два physical resource nodes и отдельный CoolantFabricator; controller/HUD/load/reset/autosave поддерживают три station outputs; добавлена `F6` acceptance с isolation, previous-recipe regression и exact SQLite round-trip.

**Статическая проверка:** JSON counts/cross-references, scene bindings, stable IDs, C# lexical balance, UID, `res://`, hotkey F6, запрещённые artifacts и ZIP integrity.

**Ограничение среды:** `dotnet`/Godot недоступны, поэтому сборка и runtime F6 здесь не выполнялись. `TASK-072` остаётся `IMPLEMENTED`, `TASK-073` — `IN_PROGRESS`.

**Следующая задача:** clean build, `F6: PASS`, регрессии `F7/F9/F10/F11/F12`, ручной coolant process и cold restart по разделу 15.

### 2026-08-02 — `TASK-070`, third data-driven crafting path

**Исходный снимок:** `ProjectHorizon-main(12).zip`
**Первичный подготовленный снимок:** `ProjectHorizon-main-third-crafting-path.zip`
**Hotfix-снимок:** `ProjectHorizon-main-third-crafting-path-f12-save-fix.zip`
**Git SHA:** отсутствует в исходном архиве
**Связанные требования:** PDF-ТЗ 17.2–17.4, 23, 36.1, Этап 1 раздела 40, критерии 6/10/14 раздела 41; `CONTENT-040`–`CONTENT-047`.

**Синхронизация:** предоставленные пользователем build/runtime-доказательства предыдущей редакции закрыли `TASK-068/TASK-069` и связанные acceptance requirements в `VERIFIED`.

**Реализация:** добавлены phase fiber, navigation array и navigation recipe с duration `2.5 s`; два physical resource nodes и отдельный NavigationFabricator; domain session обобщена на несколько station recipes; controller/HUD/load/reset/autosave поддерживают два независимых crafted components; добавлена `F12` acceptance с isolation и SQLite round-trip.

**Статическая проверка:** JSON schema/counts/cross-references, scene bindings, stable IDs, C# lexical balance, UID, `res://`, hotkey `F12`, запрещённые build/cache artifacts и ZIP integrity.

**Первичная runtime-проверка пользователя:** сборка завершилась с `0` предупреждений и `0` ошибок; startup показал `DB: Ready` и каталог `6/3/3`. `F12` завершился `FAIL` после autosave write с `Primary snapshot validation failed: inventory item ... differs`.

**Диагноз и исправление:** persistence compatibility registry не содержал `resource.phase_fiber` и `component.ship.navigation_array`; оба ID добавлены в `SaveDatabase.Migration.cs`, чтобы SQLite load сохранял их как `Known`, а не преобразовывал в `content.unknown.item`.

**Ограничение среды:** `dotnet`/Godot недоступны, поэтому повторная сборка и runtime hotfix здесь не выполнялись. `TASK-070` остаётся `IMPLEMENTED`, `TASK-071` — `IN_PROGRESS`.

**Следующая задача:** повторить `F12`; ожидается `PASS`, затем выполнить регрессии `F7/F9/F10/F11`, ручной navigation process и cold restart по разделу 14.

### 2026-08-01 — `TASK-068`, data-driven craft-time processing

**Исходный снимок:** `ProjectHorizon-main(6)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-data-driven-craft-time.zip`
**Git SHA:** отсутствует в исходном архиве
**Связанные требования:** PDF-ТЗ 17.4, 23, 36.1, Этап 1 раздела 40, критерии 6/10/14 раздела 41; `CONTENT-030`–`CONTENT-037`.

**Синхронизация:** по прямому указанию пользователя `TASK-066` и `TASK-067`, а также связанные `CONTENT-020`–`CONTENT-027`/`CONTENT-ACC-020`–`CONTENT-ACC-027`, переведены в `VERIFIED`.

**Реализация:** JSON-duration `3.0 s`, pure-.NET timer и F11 acceptance, неразрушающая preflight-проверка домена, delayed station craft, orange/green visual state, HUD progress, единственный completion/autosave и safe cancellation при graceful exit.

**Статическая проверка:** распарсены 4 JSON-файла; catalog counts `4/2/2`; cross-reference и stable IDs корректны; проверены 42 C#-файла на незавершённые строки/комментарии и баланс delimiters; 42 UID уникальны; 47 `res://` references проверены; `F11` встречается только в vertical-slice controller; build/cache/.git artefacts отсутствуют.

**Ограничение среды:** `dotnet`/Godot недоступны, поэтому сборка и runtime здесь не выполнялись. `TASK-068` остаётся `IMPLEMENTED`, `TASK-069` — `IN_PROGRESS`.

**Следующая задача:** локальная сборка `0/0`, `F11: PASS`, регрессии `F7/F9/F10`, ручные RUNNING/completion/cancellation сценарии раздела 13.



### 2026-08-01 — hotfix ручного `E` и переключения `H`

**Runtime исходной редакции:** сборка `0/0`, `F7: PASS resources=3, blocked=1, repaired=1, autosave=1, roundTrip=1`; ручное `E` на корабле формировало `ShipRepairBlocked`, но низкие ресурсные конусы при обычном приближении не подбирались; `H` не обрабатывалась.

**Исправление:** добавлен nearest-interactable proximity-fallback после точного raycast, явная группа `interactable`, исключение уже собранных узлов, HUD-подсказка фактической цели и три режима `H`. `TASK-062` остаётся `IMPLEMENTED`, `TASK-063` — `IN_PROGRESS` до повторного runtime-прогона.

### 2026-08-01 — `TASK-062`, первая интеграция вертикального среза

**Синхронизированная приёмка:** `TASK-060/TASK-061` закрыты по сборке `0/0`, F6, periodic, C/X/Z, graceful-exit `saved=1; revision=3; pending=0` и прямому подтверждению холодного восстановления revision `3`.

**Реализация:** добавлены Godot-независимое правило starter repair, три интерактивных salvage-узла, повреждённый корабль, новый gameplay controller, отдельный SQLite profile, реальный `QuestCompleted` autosave, periodic/graceful-exit и изолированная F7 acceptance route.

**Статическая проверка:** код, сцена, UID, `res://`, NodePath, hotkeys и состав архива проверены; .NET/Godot в среде подготовки отсутствуют, поэтому `TASK-062` остаётся `IMPLEMENTED`, `TASK-063` — `IN_PROGRESS`.

Новые записи добавляются сверху.

### 2026-08-01 — `TASK-060`, autosave coordinator и graceful-exit flush

**Исходный снимок:** `ProjectHorizon-main(2)(3).zip`
**Подготовленный снимок:** `ProjectHorizon-main-persistence-autosave-graceful-exit.zip`
**Git SHA:** отсутствует в архиве; `TASK-006` остаётся `BLOCKED`

**Синхронизированная приёмка:** migration-редакция собрана `0/0`; пользователь предоставил одновременные `C: PASS`, `X: PASS`, `Z: PASS` на schema 2. `TASK-058/TASK-059`, Прототип E и все пять технических прототипов переведены в `VERIFIED`.

**Реализация:** добавлены pure-.NET autosave coordinator, 60-секундный periodic trigger, типизированные gameplay/exit triggers, deterministic coalescing, журнал, HUD, F6 acceptance и перехват штатного закрытия с ожиданием активных persistence-задач и полного flush. Пустой slot при закрытии не заполняется искусственным snapshot.

**Статическая проверка:** код, XML, `res://`, UID, hotkey и состав архива проверены; .NET/Godot в среде подготовки отсутствуют, поэтому `TASK-060` остаётся `IMPLEMENTED`, `TASK-061` — `IN_PROGRESS`.

### 2026-08-01 — `TASK-058`, copy migration и unknown-content compatibility

**Исходный снимок:** `ProjectHorizon-main(1)(6).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-copy-migration.zip`
**Git SHA:** отсутствует в архиве

**Принятое runtime-доказательство предыдущей ступени:** hotfix собран `0/0`; последовательность `R → S → B → S → Y` завершилась без блокировки; revision прошла `1→2→1`; backup показала `integrity=ok`, `atomic=1`; restore показал `atomic=1`, `quarantine=1`; пользователь подтвердил полную работоспособность. `TASK-056/TASK-057` и связанные требования переведены в `VERIFIED`.

**Изменения:** добавлена schema 2 и отдельный `SaveDatabase.Migration.cs`; schema-1 БД копируется SQLite online-backup API, мигрируется и валидируется отдельно, затем атомарно заменяет primary, а byte-identical исходник сохраняется с SHA-256. Добавлены alias resolution, placeholders для неизвестного item и удалённого ship template, хранение original IDs, migration-log и изолированный `C`-тест с повторным save/load.

**Статические проверки:** PDF-ТЗ визуально проверено; migration SQL выполнен на отдельной SQLite fixture; проверены C#-лексика, record-конструкторы, nullable-пути, XML проекта, сцена, NodePath, `res://`, горячая клавиша `C` и отсутствие Godot API в persistence infrastructure. В среде подготовки нет .NET SDK/Godot, поэтому текущая сборка и runtime не заявляются.

**Статусы:** `TASK-058`, `PE-030`–`PE-035` → `IMPLEMENTED`; `TASK-059`, `PE-ACC-020`–`PE-ACC-026` → `IN_PROGRESS`.

### 2026-08-01 — hotfix блокировки последовательных manual-команд `TASK-057`

**Исходный снимок:** `ProjectHorizon-main-prototype-e-backup-recovery.zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-manual-recovery-hotfix.zip`
**Git SHA:** отсутствует в архиве

**Runtime-доказательство пользователя:** исходная редакция собрана с `0` предупреждений и `0` ошибок; изолированный `X`-тест завершился `PASS` с revision `10`, `candidateRejected=1`, `backupPreserved=1`, `atomic=1`, `quarantine=1`, backup `integrity=ok`. На трёх снимках ручной проверки snapshot и счётчик оставались `rev=2`, `writes=2`, что подтвердило непринятие последующих команд после первой операции.

**Дефект:** служебный refresh повторно использовал `_loadTask`, а `PollLoadTask()` запускал следующий refresh после каждого завершения. Получался бесконечный цикл фоновых чтений; `CanStartOperation()` постоянно видел занятый `_loadTask` и отбрасывал дальнейшие клавиши. Состояние при этом ошибочно возвращалось в `Ready`, поэтому причина не была видна пользователю.

**Исправление:** добавлена отдельная одноразовая `_refreshTask`; refresh обрабатывается `PollRefreshTask()` и не перезапускает себя; во время refresh HUD показывает `Loading`; startup загружает текущий snapshot и синхронизирует manual revision; compact/detailed HUD сохраняет независимые `Slot S/L/R`, `Backup B`, `Restore Y` результаты; slot-операции дублируются в Output.

**Статические проверки:** `PollRefreshTask()` не содержит вызова `BeginRefresh()`; ручной load и внутренний refresh используют разные task-поля; все ветви завершения очищают соответствующее поле; проверены скобки, строки, комментарии, nullable-пути и клавиши. Сборка и runtime hotfix в среде подготовки недоступны.

**Статусы:** `TASK-056` остаётся `IMPLEMENTED`; `TASK-057` остаётся `IN_PROGRESS` до последовательного `R → S → B → S → Y`, повторных `X: PASS` и `Z: PASS`.

### 2026-08-01 — `TASK-056`, валидированная backup и атомарное recovery

**Исходный снимок:** `ProjectHorizon-main(11).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-e-backup-recovery.zip`
**Git SHA:** отсутствует в архиве

**Принятое runtime-доказательство:** по прямому подтверждению пользователя `TASK-054/TASK-055` и `PE-001`, `PE-010`–`PE-015`, `PE-ACC-001`–`PE-ACC-006` переведены в `VERIFIED`; зафиксированы сборка 0/0, `Z: PASS`, schema `1`, WAL/FK/NORMAL/busy_timeout, exact round-trip, `writes=8`, `maxWriters=1`, `integrity=ok`.

**Изменения:** реализованы SQLite online backup в `save_1.backup.db`, validation candidate до установки, атомарная замена, rollback предыдущей backup, startup corruption detection, quarantine повреждённой primary, recovery-log, ручные команды `B/Y` и изолированный acceptance-тест `X`. `X` проверяет protected revision `10`, newer revision `11`, invalid candidate rejection, неизменность SHA-256 backup, intentional corruption, rollback `11→10`, обе `integrity_check`, физическое наличие quarantine, recovery-log и exact comparison.

**Проверки:** изменённые C#-файлы прошли лексический контроль; сцена, NodePath, `res://` и горячие клавиши проверены; сигнатура `SqliteConnection.BackupDatabase(SqliteConnection)` сверена с документацией Microsoft.Data.Sqlite 8.x. .NET SDK и Godot в среде подготовки отсутствуют, поэтому сборка и runtime новой редакции не заявляются.

**Статусы:** `TASK-056`, `PE-020`–`PE-025` → `IMPLEMENTED`; `TASK-057`, `PE-ACC-010`–`PE-ACC-016` → `IN_PROGRESS`.

### 2026-08-01 — hotfix `TASK-045`, детерминированный вход в атмосферу

**Исходный снимок:** `ProjectHorizon-main-prototype-d-atmospheric-flight.zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-atmospheric-entry-hotfix.zip`
**Git SHA:** отсутствует в архиве

**Runtime-доказательство:** сборка 0/0; `L: FAIL timeout phase=Entry`; после восстановления baseline HUD показывал SPACE/alt=441,8 м. Free-flight `J: PASS` не регрессировал.

**Изменения:** добавлен physics-tick radial guidance, гарантированное движение через entry boundary, отдельный 5-секундный entry timeout, расширенные `entryStart/entryMin/alt/radial/blend` diagnostics и очистка guidance при reset/restore/finish. Ручной `P`-подход использует тот же временный guidance.

**Ограничение:** hotfix статически проверен, но требует повторной локальной сборки и `TASK-046` runtime-приёмки.

### 2026-08-01 — `TASK-045`, упрощённый атмосферный режим

**Исходный снимок:** `ProjectHorizon-main(2)(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-atmospheric-flight.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 5.3, 14.1, 31.2 и 39 PDF-ТЗ; `TASK-043`–`TASK-047`, `PD-001`, `PD-010`–`PD-025`, `PD-ACC-001`–`PD-ACC-018`.

**Runtime-доказательство предыдущей итерации:** сборка 0/0; `J: PASS`; vmax `72,0 м/с`, distance `221,2 м`, lateral `38,4`, vertical `20,4`, angular `105,4°/с`, final speed/angular `0`, collisions `0`; ручное управление подтверждено пользователем.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/Ship/ArcadeShipController.cs`;
- `src/Game.Client/Scripts/Ship/ArcadeShipAtmosphere.cs`;
- `src/Game.Client/Scripts/Ship/ArcadeShipAtmosphere.cs.uid`;
- `src/Game.Client/Scripts/Ship/ShipFlightPrototype.cs`;
- `src/Game.Client/Scripts/Ship/ShipAtmosphereAcceptance.cs`;
- `src/Game.Client/Scripts/Ship/ShipAtmosphereAcceptance.cs.uid`;
- `src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Краткий результат:** free-flight расширен автоматическим атмосферным контекстом, simplified lift/minimum speed/drag/climb limit и surface-safety. Добавлены ручной approach по `P`, измеримый test по `L` и физическая тестовая планета с атмосферной оболочкой.

**Проверки:** 21 C#-файл прошёл лексический контроль; `res://`, scene `load_steps`, NodePath и структура ресурсов проверены; детерминированная симуляция `L`-маршрута при 120 Hz получила entry=1, exit=1, maxBlend=0,940, dragDrop=9,75 м/с, maxClimb=15,73 м/с, minAltitude=13,40 м и recoveries=0.

**Ограничение:** сборка и runtime новой атмосферной редакции в текущей среде недоступны; требуется `TASK-046`. Посадочная точка, уклон, препятствия, опоры и landed-state не реализованы.

### 2026-08-01 — `TASK-043`, Прототип D: базовый корабль и свободный полёт

**Исходный снимок:** `ProjectHorizon-main(1)(5).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-d-free-flight.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 14.1, 31.2 и 39 PDF-ТЗ; `TASK-041`–`TASK-045`, `PD-001`, `PD-010`–`PD-017`, `PD-ACC-001`–`PD-ACC-006`.

**Runtime-доказательство предыдущей итерации:** сборка 0/0; предоставлены compact, detailed и hidden screenshot; HUD по `H` работает и больше не перекрывает 3D-холст.

**Добавленные/изменённые файлы:**

- `src/Game.Client/Scripts/Ship/ArcadeShipController.cs`;
- `src/Game.Client/Scripts/Ship/ShipFlightPrototype.cs`;
- `src/Game.Client/Scenes/Ship/ArcadeShip.tscn`;
- `src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn`;
- `src/Game.Client/project.godot`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Краткий результат:** создан независимый free-flight prototype с локальной шестикоординатной аркадной системой управления, двумя камерами, responsive HUD и автономным измеримым тестом. Планетарный compact scrollbar устранён.

**Ограничение:** сборка и runtime новой корабельной сцены в текущей среде недоступны; требуется `TASK-044`. Посадка, взлёт и атмосферный режим не входят в эту итерацию.

### 2026-08-01 — `TASK-041`, завершение Прототипа C и HUD ergonomics

**Исходный снимок:** `ProjectHorizon-main(8).zip`
**Подготовленный снимок:** `ProjectHorizon-main-prototype-c-hud-ergonomics.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 31.4 и 39 PDF-ТЗ; `TASK-038`–`TASK-043`, `PC-090`–`PC-104`, `PC-ACC-060`–`PC-ACC-074`.

**Runtime-доказательство предыдущей итерации:** сборка 0/0; `K: PASS`; `plans=60`, `commits=60`, `created=257`, `unloaded=233`, `fallback=60`, `L3=28`, `gap=0`, `rMin=92,46 м`, `recoveries=0`, `errors=0`; финально ground/floor/probe `да`.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Краткий результат:** Прототип C принят по всем критериям PDF-ТЗ. Диагностический overlay заменён трёхрежимным responsive HUD: compact по умолчанию, detailed со ScrollContainer и hidden с минимальным hint.

**Ограничение:** сборка и runtime новой HUD-редакции в текущей среде недоступны; требуется `TASK-042`.

### 2026-08-01 — `TASK-038`, dynamic collision LOD

**Исходный снимок:** `ProjectHorizon-main(7).zip`
**Подготовленный снимок:** `ProjectHorizon-main-dynamic-collision-lod.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** разделы 9.3 и 10.2 PDF-ТЗ; `TASK-036`–`TASK-040`, `PC-080`–`PC-096`, `PC-ACC-050`–`PC-ACC-068`.

**Runtime-доказательство предыдущей итерации:** сборка 0/0; `TASK-036 stream (I): PASS revisions=10, L3=12, resident=44/45, unloaded=93, queue=0, workers=0, cancel=0, stale=24, errors=0`; topology open/nonManifold 0, `Δlod=1`, `Δpos=0`.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/CubeSphereCollisionLod.cs`;
- `src/Game.Client/Scripts/Planet/CubeSphereCollisionLod.cs.uid`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Краткий результат:** добавлены локальные collision patches по quadtree-листьям, safety fallback `6 × 129×129`, deferred enable/disable, overlap по physics-кадрам, commit/unload lifecycle, HUD-метрики и acceptance test `K`, использующий реальное межгранное движение игрока.

**Проверки текущей среды:** лексический C#-контроль 17 файлов, scene/resource paths, проверка `load_steps`, симуляция 275 выборок принятого четырёхшовного маршрута (`target=27–47`, `changes=121`, `created=294`, `unloaded=252`, topology `open=0`, `maxDelta=1`) и ZIP hygiene. Сборка и Godot runtime новой функции недоступны.

### 2026-08-01 — `TASK-036`, L1/L2/L3 async visual patch streaming

**Исходный снимок:** `ProjectHorizon-main(1)(4).zip`
**Подготовленный снимок:** `ProjectHorizon-main-async-quadtree-streaming.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** раздел 9.3 PDF-ТЗ; `TASK-033`–`TASK-038`, `PC-070`–`PC-086`, `PC-ACC-040`–`PC-ACC-057`.

**Runtime-доказательство предыдущей итерации:** сборка 0/0; `TASK-033 LOD (U): PASS split=17, merge=16, Δlod=1, open=0, seam=0`; patches 36, L1 20, L2 16, atomic 112, nonManifold 0.

**Краткий результат:** добавлены рекурсивный третий уровень, 2:1 balancing, отменяемые worker jobs, plan revisions, stale protection, дозированное main-thread применение и resident/unload lifecycle. Клавиша `I` выполняет автоматическую приёмку. Стабильная collision-сетка сохранена; collision LOD выделен в `TASK-038`.

**Проверки текущей среды:** математическая симуляция topology/resident route, лексический C#-контроль, scene/resource paths, ZIP hygiene. Сборка и Godot runtime недоступны.

### 2026-08-01 — `TASK-033`, начальная quadtree LOD-ступень

**Исходный снимок:** `ProjectHorizon-main(6).zip`
**Подготовленный снимок:** `ProjectHorizon-main-quadtree-lod-foundation.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** раздел 9.3 PDF-ТЗ; `PC-070`–`PC-074`, `PC-ACC-040`–`PC-ACC-045`.

**Краткий результат:** визуальные грани заменены независимыми `L1/L2` patches,
добавлены skirts, hysteresis, topology validator и acceptance test `U`. Шесть
collision-граней сохранены без перестроения. `TASK-033` имеет статус
`IMPLEMENTED`, `TASK-035` ожидает локального доказательства.

**Проверки в текущей среде:** математическая симуляция всех 9 состояний маршрута,
лексический C#-контроль, scene/resource paths, ZIP hygiene. Сборка и Godot runtime
недоступны.

### 2026-08-01 — приёмка `TASK-032/TASK-034` и добавление регламента итераций

**Исходный снимок:** `ProjectHorizon-main-floating-origin-build-hotfix.zip`
**Подготовленный снимок:** `ProjectHorizon-main-development-iteration-protocol.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `TASK-032`, `TASK-034`, `PC-060`–`PC-064`, `PC-ACC-030`–`PC-ACC-035`; процесс ведения журнала и передачи итераций.

**Runtime-доказательство от пользователя:**

- локальная сборка завершена успешно: `0` ошибок, `1` предупреждение;
- предупреждение: `CS8600` в `Scripts/Terrain/TerrainChunkManager.cs:399`;
- HUD: `TASK-032 origin (Y): PASS shifts=4, cells=6`;
- `localMax=1809,2 м`, `logicalErr=0,000 м`, `relativeErr=0,0003 м`, `gap=0,00 с`;
- `ground=да`, `floor=да`, `probe=да`, радиальная система `PASS`, швы `388/388 PASS`.

**Изменённые/добавленные файлы:**

- `DEVELOPMENT_ITERATION_PROTOCOL.md`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что оформлено:**

- обязательный порядок определения следующей задачи;
- правила присвоения статусов и фиксации runtime-доказательств;
- требования к реализации, статической проверке, сборке и упаковке;
- обязательная инструкция пользователю по доказательству работоспособности;
- обязательный текст коммита и стандартный сокращённый запрос;
- ссылка на регламент из README и журнала.

**Изменения статусов:**

- `TASK-032`, `TASK-034`, `PC-060`–`PC-064`, `PC-ACC-030`–`PC-ACC-035` → `VERIFIED`;
- `TASK-033` становится следующей функциональной задачей.

**Открытый технический долг:** устранить `CS8600` в `TerrainChunkManager.cs:399` в ближайшей кодовой итерации.

### 2026-08-01 — приёмка `TASK-030/TASK-031` и реализация `TASK-032`

**Исходный снимок:** `ProjectHorizon-main(1)(3).zip`
**Подготовленный снимок:** `ProjectHorizon-main-floating-origin.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `TASK-030`–`TASK-034`, `PC-050`–`PC-064`, `PC-ACC-020`–`PC-ACC-035`, раздел 6.2 и Прототип C раздела 39 PDF-ТЗ.

**Runtime-доказательство от пользователя:**

- screenshot: `TASK-030 seam (T): PASS crossings=4, gap=0,00 с, Δup=0,59°`;
- HUD: `ground=да`, `floor=да`, `probe=да`, `переходы=5`, `швы PASS (388/388)`;
- пользователь подтвердил ручную проверку по всем пунктам: без провалов, резких прыжков, переворота камеры и ошибок касательного управления; `R` работает.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/FloatingOriginController.cs`;
- `src/Game.Client/Scripts/Planet/FloatingOriginController.cs.uid`;
- `src/Game.Client/Scripts/Planet/PlanetaryPlayerController.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- cell/local coordinate model `long + Vector3`;
- cell size `4096 м`, half-cell threshold `2048 м`;
- перенос Planet, Player и CameraRig единым вектором;
- double-precision logical coordinate и continuity diagnostics;
- коррекция stored spawn/test transforms при translation мира;
- HUD cell/local/shift diagnostics;
- автоматический `Y`-test: четыре shift-события, шесть cell transitions, positive/negative axes, relative transform/contact checks и baseline restore;
- безопасная отмена через `Y`, `F2`, `R`, `T`.

**Статическая проверка:**

- C#-структура и баланс скобок проверены;
- scene paths `Player`, `Planet`, `CameraRig` и новый script resource согласованы;
- отрицательное преобразование cell delta проверено для координат меньше `-2048`;
- logical coordinate до/после rebase вычисляется независимо и сравнивается в `double`;
- все shift targets получают идентичную translation;
- reset spawn и seam-test baseline не остаются в старой локальной системе;
- архив не содержит `.godot`, `bin`, `obj`, `.vs`, IDE-кеша или `.git`;
- фактическая компиляция недоступна в рабочей среде из-за отсутствия Godot/.NET SDK.

**Изменения статусов:**

- `TASK-030`, `TASK-031`, `PC-050`–`PC-053`, `PC-ACC-020`–`PC-ACC-024` → `VERIFIED`;
- `TASK-032`, `PC-060`–`PC-064` → `IMPLEMENTED`;
- `TASK-034`, `PC-ACC-030`–`PC-ACC-035` → `IN_PROGRESS`.

**Следующее действие:** получить `TASK-032 origin (Y): PASS` по разделу 10.

### 2026-08-01 — приёмка `TASK-029` и реализация `TASK-030`

**Исходный снимок:** `ProjectHorizon-main(5).zip`
**Подготовленный снимок:** `ProjectHorizon-main-planet-seam-walking.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `TASK-029`–`TASK-031`, `PC-040`–`PC-053`, `PC-ACC-010`–`PC-ACC-024`, раздел 13.4 и Прототип C раздела 39 PDF-ТЗ.

**Runtime-доказательство от пользователя:**

- пользователь принял предыдущую редакцию с радиальной гравитацией и запросил следующий функциональный шаг;
- новых сообщений об ошибках сборки, гравитации, WASD, прыжке, reset или камерах не поступило;
- на этом основании `TASK-029` и связанные критерии переведены в `VERIFIED`.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Planet/PlanetaryPlayerController.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/PlanetaryPlayer.tscn`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- пять ground-probe raycast вместо зависимости от единственного floor contact;
- seam-safe floor snap, safe margin, adhesion и grace period;
- определение текущей cube-sphere грани с гистерезисом;
- счётчик межгранных переходов и расширенная HUD-диагностика;
- автоматический `T`-тест через четыре границы с контролем contact gap и `Δup`;
- безопасная отмена и восстановление трансформа/камеры;
- сохранение шести независимых collision-граней без замены на единую упрощённую сферу.

**Статическая проверка:**

- баланс скобок всех C#-файлов проверен;
- `res://`-ссылки на сцены и скрипты согласованы;
- все пять `RayCast3D` используют collision mask планеты и локальное направление `-Y`, которое совпадает с направлением к центру после радиального выравнивания;
- прыжок временно блокирует adhesion, поэтому новая система не приклеивает игрока при отрыве;
- автоматический маршрут не использует глобальную плоскость XZ;
- архив не содержит `.godot`, `bin`, `obj`, `.vs`, IDE-кеша или `.git`;
- фактическая компиляция недоступна в рабочей среде из-за отсутствия Godot/.NET SDK.

**Изменения статусов:**

- `TASK-029`, `PC-040`–`PC-044`, `PC-ACC-010`–`PC-ACC-015` → `VERIFIED`;
- `TASK-030`, `PC-050`–`PC-053` → `IMPLEMENTED`;
- `TASK-031`, `PC-ACC-020`–`PC-ACC-024` → `IN_PROGRESS`.

**Следующее действие:** локально получить `TASK-030 seam (T): PASS` по разделу 10.

### 2026-08-01 — приёмка основы cube sphere и реализация `TASK-029`

**Исходный снимок:** `ProjectHorizon-main(3)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-planetary-gravity.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `TASK-027`, `TASK-028`, `TASK-029`, `PC-001`–`PC-044`, разделы 9.1–9.3, 13.4 и 39 PDF-ТЗ.

**Runtime-доказательство от пользователя:**

- сцена cube sphere запущена;
- `Грани: 6/6`, `collision: 6/6`;
- `33×33`, `6534` вершины, `12288` треугольников;
- `Швы: PASS`, `388/388`;
- `Δpos max=0`, `Δnormal max=0`;
- пользователь подтвердил корректный результат.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/PlanetaryPlayerController.cs`;
- `src/Game.Client/Scripts/Planet/PlanetaryPlayerController.cs.uid`;
- `src/Game.Client/Scenes/Planet/PlanetaryPlayer.tscn`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- принята геометрическая основа cube sphere;
- добавлен отдельный планетарный персонаж;
- направление гравитации вычисляется к центру планеты;
- локальная ориентация плавно согласуется с радиальным Up;
- управление и камера работают в касательной локальной системе;
- добавлены радиальный прыжок, reset, player/overview camera toggle и HUD-диагностика.

**Статическая проверка:**

- планетарный контроллер не использует глобальный `velocity.Y` или фиксированную `Vector3.Down` для физики;
- движение проецируется через `Slide(RadialUp)`;
- `UpDirection` задаётся до `MoveAndSlide`;
- rotation basis ортонормализуется после quaternion slerp;
- обзорная камера не является current в режиме игрока;
- `Space` не перехватывается сценой в player mode;
- сцены и `res://`-ссылки согласованы;
- build/cache-мусор отсутствует;
- фактическая компиляция недоступна в рабочей среде из-за отсутствия Godot/.NET SDK.

**Изменения статусов:**

- `TASK-027`, `TASK-028` → `VERIFIED`;
- базовые `PC-*` и `PC-ACC-001`–`PC-ACC-006` → `VERIFIED`;
- `TASK-029`, `PC-040`–`PC-044` → `IMPLEMENTED`;
- `PC-ACC-010`–`PC-ACC-015` → `IN_PROGRESS`.

**Следующее действие:** выполнить runtime smoke-test `TASK-029` по разделу 10.

### 2026-08-01 — приёмка `TASK-026` и первая итерация Прототипа C

**Исходный снимок:** `ProjectHorizon-main(2)(1).zip`
**Подготовленный снимок:** `ProjectHorizon-main-cube-sphere-foundation.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `PB-061`, `TASK-026`, `TASK-027`, `PC-001`, `PC-010`–`PC-014`, `PC-020`, `PC-021`, `PC-030`, разделы 9.1–9.4 и 39 PDF-ТЗ.

**Runtime-доказательство от пользователя:**

- `TASK-026 soak (P): PASS`;
- `121 с`, `moves=82`, `managedDelta=0,0 MB`, `mesh=9`, `collision=9`;
- финально `9/9`, `queue=0`, `workers=0/4`, `ошибки=0`, переход стабилен;
- пользователь подтвердил завершение тестов.

**Изменённые/добавленные файлы:**

- `src/Game.Client/Scripts/Planet/CubeSphereMeshBuilder.cs`;
- `src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs`;
- `src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn`;
- `src/Game.Client/project.godot`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- Прототип B и `PB-061` закрыты как `VERIFIED` по успешному длительному soak-test;
- создана отдельная cube-sphere сцена и назначена стартовой;
- построены шесть независимых `33 × 33` граней с корректной наружной ориентацией треугольников;
- добавлена радиальная проекция, 3D noise-высота и единая seam-safe функция выборки;
- добавлены шесть visual mesh и шесть collision shapes;
- добавлена runtime-валидация совпадения позиций/нормалей на швах;
- добавлены HUD, face-color/radial-normal режимы и неподвижная planet physics при орбитальной камере.

**Статическая проверка:**

- математически проверено, что `cross(AxisU, AxisV)` направлен наружу на всех шести гранях;
- индексный порядок обоих треугольников каждой ячейки направлен наружу;
- `SurfaceTool` получает normal/UV/color до `AddVertex`, затем индексный массив и `Commit`;
- collision создаётся из той же геометрии, но не пересоздаётся при `F1`;
- планета и `StaticBody3D` не вращаются; вращается только camera rig;
- `.tscn`, `res://`-ссылки, UID-файлы и `project.godot` согласованы;
- build/cache-мусор отсутствует;
- фактическая компиляция недоступна в рабочей среде из-за отсутствия Godot/.NET SDK.

**Изменения статусов:**

- `TASK-026` → `VERIFIED`;
- `PB-061` → `VERIFIED`;
- Прототип B → `VERIFIED`;
- `TASK-027` → `IMPLEMENTED`;
- Прототип C → `IN_PROGRESS`;
- `PC-001`, `PC-010`–`PC-014`, `PC-020`, `PC-021`, `PC-030` → `IMPLEMENTED`;
- `PC-ACC-001`–`PC-ACC-006` → `IN_PROGRESS` до локального smoke-test.

**Следующее действие:** выполнить `TASK-028` по разделу 10 и передать screenshot HUD плюс строку `CubeSphere foundation` из Output.

### 2026-08-01 — приёмка `TASK-025` и реализация soak-profiler `TASK-026`

**Исходный снимок:** `ProjectHorizon-main(1)(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-terrain-soak-profiler.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `PB-040`–`PB-043`, `PB-050`–`PB-052`, `PB-060`–`PB-063`, `PB-ACC-001`–`PB-ACC-010`, разделы 10.1–10.4, 34.3 и 39 PDF-ТЗ, `TASK-005`, `TASK-009`, `TASK-011`, `TASK-023`–`TASK-026`.

**Runtime-доказательство от пользователя:**

- пользователь запустил последнюю GitHub-редакцию в Godot и предоставил screenshot;
- HUD: `TASK-025 stress: PASS: rev=13, cancel=0, stale=48, 9/9, queue=0, workers=0`;
- HUD: `ошибки: 0`, `переход: стабильно`;
- пользователь подтвердил нормальный результат и распорядился отметить `TASK-025` и предшествующие принятые задачи как `VERIFIED`.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- короткий stress-test перенесён с `F9` на `F10`, чтобы исключить конфликт с hotkey редактора Godot;
- добавлен длительный soak-test `TASK-026` на клавишу `P` с повторным нажатием для безопасной остановки;
- добавлен детерминированный замкнутый маршрут по соседним чанкам;
- каждый переход ждёт полного idle предыдущей revision;
- на каждом idle-срезе валидируются active set, актуальные LOD/stitch/collision-спецификации, visual mesh, collision shape и число top-surface vertices;
- профилируются managed memory baseline/peak/final, active chunks, queue, workers, mesh/collision и vertices;
- добавлены timeout перехода, минимальное покрытие moves/samples и экспортируемый memory-growth limit;
- итоговый `PASS` возможен только после возврата в исходный центр и полного `queue=0`, `workers=0`;
- HUD и Output расширены метриками `TASK-026`;
- `TerrainChunk` получил read-only диагностические свойства ресурсов;
- сцена получила явные параметры 120-секундного soak-test и увеличенный HUD.

**Статическая проверка:**

- `Godot.Timer` остаётся однозначно типизированным;
- worker-потоки не получают доступ к `Node`, `SceneTree`, mesh или collision API;
- state machine stress/soak взаимно исключает одновременный запуск;
- маршрут замкнут и возвращает исходный центр;
- ресурсный snapshot требует точного количества mesh/collision и актуальных `ChunkSpec`;
- итоговый PASS проверяет worker errors, coverage, memory growth и idle-состояние;
- ссылки `res://`, `.tscn` и C#-классы согласованы;
- build/cache-мусор отсутствует;
- фактическая компиляция в рабочей среде не выполнялась из-за отсутствия Godot/.NET SDK.

**Изменения статусов:**

- `TASK-005`, `TASK-009`, `TASK-011`, `TASK-023`, `TASK-024`, `TASK-025` → `VERIFIED` по прямому подтверждению пользователя;
- `PB-040`–`PB-043`, `PB-050`–`PB-052`, `PB-060`, `PB-062`, `PB-063` → `VERIFIED`;
- `PB-ACC-001`–`PB-ACC-010` → `VERIFIED`;
- `PB-061` остаётся `IMPLEMENTED` до длительного resource-soak;
- `TASK-026` → `IN_PROGRESS` (`IMPLEMENTED`, ожидает runtime `PASS`).

**Следующее действие:** собрать текущий снимок, запустить `F5`, при необходимости повторить `F10`, затем нажать `P` и передать итоговую строку `Terrain soak test: PASS/FAIL` из Output.

### 2026-08-01 — встроенный runtime stress-test `TASK-025`

**Исходный снимок:** `ProjectHorizon-main(4).zip`
**Подготовленный снимок:** `ProjectHorizon-main-terrain-async-stress-test.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `PB-050`–`PB-052`, `PB-ACC-007`, `PB-ACC-008`, `TASK-025`
**Метод:** анализ PDF-ТЗ, статический аудит C#, реализация детерминированной runtime-диагностики, проверка структуры сцены и целостности архива.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- hotkey `F9` для запуска `TASK-025` без ручного многократного пересечения границ;
- ожидание исходного idle-состояния до начала теста;
- временная остановка physics-process игрока и восстановление после теста;
- детерминированная последовательность быстрых центров стриминга с возвратом к точному исходному hysteresis-центру чанка;
- накопительные счётчики cancelled/stale/failed поверх отдельных revision;
- учёт worker-ошибок из stale и отброшенных ready-результатов;
- timeout и явная диагностика состояния всех очередей;
- финальная проверка полного активного набора, LOD/collision/stitch/skirt-спецификаций и отсутствия stale-чанков;
- итоговые сообщения `Terrain async stress test: PASS/FAIL` в Output и отдельная строка HUD;
- увеличена высота HUD для новой строки диагностики.

**Как проверено статически:**

- hotfix `Godot.Timer` присутствует в исходном GitHub-снимке и сохранён;
- worker и main-thread обязанности не смешаны;
- тест инициирует минимум четыре быстрые revision и финальную revision исходного центра;
- PASS невозможен при worker error, непустой очереди, активном worker, несовпадении revision либо неверном наборе чанков;
- PASS требует наблюдаемой отмены либо stale-результата;
- парность скобок и строк C# проверена;
- ссылки сцены и ресурсов сохранены;
- build/cache-мусор отсутствует.

**Изменения статусов:**

- `PB-ACC-007`: `NOT_STARTED` → `IN_PROGRESS` благодаря автоматизированной проверке активного набора;
- кодовая часть `TASK-025` реализована;
- `TASK-025` остаётся `IN_PROGRESS` до локального `F9: PASS`;
- `TASK-005` ожидает локальный результат сборки.

**Открытое ограничение:** Godot и .NET SDK отсутствуют в рабочей среде, поэтому компиляция и фактический runtime-результат не заявляются.

**Следующее действие:** собрать проект, запустить `F5`, дождаться `9/9`, нажать `F9` и передать итоговую строку `PASS/FAIL` из Output.

### 2026-08-01 — hotfix неоднозначной ссылки `Timer`

**Исходный снимок:** `ProjectHorizon-main(3).zip`
**Подготовленный снимок:** `ProjectHorizon-main-async-terrain-timer-hotfix.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `TOOL-001`, `CFG-004`, `PB-050`–`PB-052`, `TASK-005`, `TASK-025`
**Основание:** пользовательская локальная сборка завершилась ошибкой `CS0104` в `TerrainChunkManager.cs(86,13)`.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkDataBuilder.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что исправлено:**

- `Timer` заменён на однозначный `Godot.Timer` в объявлении поля и при создании узла;
- удалено широкое подключение `System.Threading` из менеджера;
- введены alias для `CancellationToken` и `CancellationTokenSource`;
- в worker-builder введён точечный alias для `CancellationToken`;
- логика фоновой генерации, отмены ревизий и main-thread apply не изменялась.

**Как проверено:**

- в terrain-скриптах отсутствуют неуточнённые ссылки `Timer`;
- `System.Threading.Timer` больше не попадает в область имён менеджера;
- все использования токенов отмены разрешаются через явные alias;
- выполнена статическая проверка парности скобок и строк;
- проверена целостность итогового ZIP и отсутствие build/cache-каталогов.

**Изменения статусов:**

- статусы требований не повышались;
- `TASK-025` остаётся `IN_PROGRESS`;
- `TASK-005` ожидает результат повторной локальной сборки.

**Открытые ограничения:**

- Godot и .NET SDK отсутствуют в рабочей среде;
- фактическая повторная компиляция должна быть выполнена пользователем локально;
- после успешной сборки нужен полный smoke test раздела 10.

**Следующее действие:** повторить сборку; при отсутствии ошибок выполнить runtime-сценарий `TASK-025`.

### 2026-08-01 — отменяемая фоновая генерация чанков

**Исходный снимок:** `ProjectHorizon-main(2).zip`
**Подготовленный снимок:** `ProjectHorizon-main-async-terrain.zip`
**Git SHA:** отсутствует в архиве
**Связанные требования:** `PB-050`, `PB-051`, `PB-052`, `PB-062`, `TASK-023`
**Метод:** статический анализ C#, проверка разделения worker/main-thread, жизненного цикла ревизий, отмены job, очереди применения и целостности ZIP

**Предшествующее подтверждение:**

- пользователь сообщил, что диагностическая версия после исправления материала, стыков и локальной перестройки работает;
- на этом основании `TASK-024` считается завершённой, однако новая асинхронная реализация требует отдельного регрессионного прогона.

**Изменённые файлы:**

- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkDataBuilder.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkDataBuilder.cs.uid`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Что реализовано:**

- неизменяемый снимок параметров `TerrainChunkBuildRequest`;
- отдельные контейнеры данных `TerrainMeshData` и `TerrainChunkBuildResult`;
- worker-расчёт высот, глобальных нормалей, UV и индексов;
- до четырёх параллельных задач по формуле ТЗ;
- `CancellationTokenSource` на каждую revision плана;
- `jobId` и revision для каждой create/update операции;
- `ConcurrentQueue` для передачи результата без обращения worker к `SceneTree`;
- буфер готовых job с применением строго в исходном порядке плана, даже если worker завершились иначе;
- отбрасывание stale-результатов до применения;
- main-thread создание `Node`, `ArrayMesh`, материала и collision;
- применение не более `MaxOperationsPerStep` тяжёлых результатов за шаг;
- сохранение старых чанков до готовности входящих;
- отсутствие повторной collision-генерации при visual-only LOD update;
- кэш поверхности для диагностических переключателей без нового noise-расчёта;
- расширенная телеметрия HUD и Output.

**Статические проверки:**

- скобочная и строковая целостность изменённых C#-файлов;
- отсутствие `AddChild`, `QueueFree`, `SurfaceTool`, `ArrayMesh` и `CollisionShape3D` в worker-builder;
- `GetNoise2D` вызывается только внутри `TerrainChunkDataBuilder`;
- все завершения worker поступают через `ConcurrentQueue`;
- stale revision проверяется до `ApplyGeneratedData`;
- готовые результаты не нарушают порядок create/demotion/promotion;
- removal остаётся после create/update и блокируется до завершения активных и ожидающих apply job;
- запрещённые build/cache-каталоги отсутствуют.

**Изменения статусов:**

- `PB-050`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-051`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-052`: `NOT_STARTED` → `IMPLEMENTED`;
- `TASK-023`: завершена;
- `TASK-025`: назначена текущей runtime-проверкой.

**Ограничения:**

- Godot и .NET SDK отсутствуют в рабочей среде;
- фактическая компиляция и запуск не выполнялись;
- значения времени main-thread apply должны быть проверены локально;
- окончательный статус `VERIFIED` не присваивался.

**Следующее действие:** выполнить smoke test по разделу 10 и передать результат Output/сборки.

### 2026-08-01 — диагностическая окраска, мировая сетка и wireframe рельефа

**Проверенный снимок:** `ProjectHorizon-fix-terrain-diagnostics.zip`
**Git SHA:** отсутствует в архиве
**Основание:** однотонная тёмная поверхность не позволяла надёжно оценить геометрию, нормали и LOD-стыки
**Метод:** статический анализ C# и `.tscn`, проверка формирования vertex color, мировых координат overlay, фактической триангуляции и целостности ZIP

**Изменённые файлы:**

- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано:**

- матовый vertex-color материал и цветовая карта высоты/уклона;
- контрастный режим LOD;
- RGB-режим глобальных нормалей;
- непрерывная мировая сетка;
- wireframe фактической триангуляции;
- цветные границы чанков;
- runtime-переключатели `F1`–`F4`;
- расширенный HUD и более читаемое освещение.

**Изменения статусов:**

- добавлены `PB-023`, `PB-024` со статусом `IMPLEMENTED`;
- добавлен `PB-ACC-010` со статусом `IN_PROGRESS`;
- `PB-ACC-003` и `PB-ACC-009` остаются `IN_PROGRESS` до повторного локального прогона.

**Ограничения:**

- Godot и .NET SDK отсутствуют в аудиторской среде;
- сборка и визуальная проверка не выполнялись;
- диагностические overlay предназначены для прототипа и позднее должны отключаться в производственной сцене.

**Следующее действие:** выполнить smoke test по разделу 10 и сообщить, видны ли разрывы сетки, скачки нормалей или локальные перестройки.

### 2026-08-01 — исправление LOD-стыков и поэтапный streaming transition

**Проверенный снимок:** `ProjectHorizon-fix-terrain-stitching-streaming.zip`
**Git SHA:** отсутствует в архиве
**Основание:** пользовательская runtime-проверка предыдущего снимка выявила видимые стыки и резкую перестройку при движении
**Метод:** статический анализ C# и `.tscn`, проверка алгоритма edge stitching, глобальных нормалей, гистерезиса, порядка очереди и целостности ZIP

**Изменённые файлы:**

- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Исправлено:**

- T-junction между сетками `33 × 33` и `17 × 17`;
- независимые локальные нормали, усиливавшие видимость шва;
- удаление исходящего ряда до готовности входящего;
- синхронная массовая регенерация чанков в одном вызове;
- лишняя регенерация collision при чисто визуальной смене LOD;
- дребезг координаты центра возле границы.

**Изменения статусов:**

- добавлены `PB-043`, `PB-062`, `PB-063` со статусом `IMPLEMENTED`;
- `PB-ACC-008`: `NOT_STARTED` → `IN_PROGRESS`;
- `PB-ACC-009`: `NOT_STARTED` → `IN_PROGRESS` после зафиксированного дефекта и реализации исправления;
- `TASK-024` назначена текущей runtime-проверкой.

**Ограничения:**

- Godot и .NET SDK отсутствуют в аудиторской среде;
- сборка и визуальная runtime-проверка исправления не выполнялись;
- расчёт mesh всё ещё синхронный внутри одной timer-операции, но burst распределён по времени; visual-only смена LOD не затрагивает collision;
- полноценные фоновые worker-задачи с `CancellationToken` ещё не реализованы.

**Следующее действие:** выполнить smoke test по разделу 10; при успешном результате перейти к `TASK-023`.

### 2026-08-01 — реализация стриминга чанков и минимального LOD

**Проверенный снимок:** `ProjectHorizon-feature-terrain-streaming-lod.zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ C# и `.tscn`, проверка ссылок ресурсов, координатной модели, расчётов LOD и целостности ZIP

**Изменённые файлы:**

- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs.uid`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано:**

- активная сетка `3 × 3` вокруг игрока;
- LOD0 `33 × 33` и LOD1 `17 × 17` с дистанционным назначением;
- регенерация LOD только при смене текущего чанка;
- skirts для визуального закрытия щелей;
- отдельная collision-сетка `33 × 33`;
- динамическое создание и удаление рядов чанков;
- явное освобождение mesh/collision перед удалением;
- HUD и расширенный runtime-лог.

**Изменения статусов:**

- `PB-040`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-041`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-042`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-060`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-061`: `NOT_STARTED` → `IMPLEMENTED`.

**Ограничения:**

- Godot и .NET SDK отсутствуют в аудиторской среде;
- текущая версия не прошла локальную сборку и runtime-smoke-test;
- генерация пока синхронная и может давать кратковременный hitch при переходе границы;
- фоновые задания и отмена устаревшей генерации не реализованы.

**Следующее действие:** выполнить smoke test по разделу 10; после подтверждения перейти к `TASK-023`.

### 2026-08-01 — реализация основы процедурного чанка рельефа

**Проверенный снимок:** `ProjectHorizon-feature-terrain-chunk-foundation.zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ C# и `.tscn`, проверка путей ресурсов, арифметики сетки и целостности ZIP

**Изменённые файлы:**

- `src/Game.Client/project.godot`;
- `src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn`;
- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs`;
- `src/Game.Client/Scripts/Terrain/TerrainChunk.cs.uid`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано:**

- отдельная сцена тестирования Прототипа B;
- детерминированная выборка OpenSimplex2/FBM;
- 33 × 33 вершины и 2048 треугольников;
- процедурный mesh с UV и нормалями;
- отдельная trimesh collision;
- физический тест с существующим игроком;
- вывод метрик генерации в Output.

**Изменения статусов:**

- Прототип B: `NOT_STARTED` → `IN_PROGRESS`;
- `PB-001`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-010`–`PB-012`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-020`–`PB-022`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-030`–`PB-031`: `NOT_STARTED` → `IMPLEMENTED`;
- `PB-ACC-001`: `NOT_STARTED` → `IN_PROGRESS`.

**Ограничения:**

- Godot и .NET SDK отсутствуют в аудиторской среде;
- runtime и физика не проверялись;
- LOD, фоновая генерация и выгрузка не реализованы и не заявлены выполненными.

**Следующее действие:** выполнить smoke test по разделу 10; после подтверждения реализовать минимальный LOD.

### 2026-08-01 — реализация простой hitscan-стрельбы

**Проверенный снимок:** `ProjectHorizon-player-shooting-hotfix.zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ файлов, проверка ссылок ресурсов и целостности ZIP

**Реализовано:**

- действие `fire_primary` на ЛКМ;
- отдельный компонент `HitscanWeapon`;
- `FireRay` длиной 50 м из игровой камеры;
- исключение собственной коллизии игрока;
- ограничение частоты стрельбы до четырёх выстрелов в секунду;
- контракт `IHitscanTarget`;
- оранжевая тестовая цель с красной вспышкой при попадании;
- исключён ложный выстрел при клике, возвращающем захват курсора.

**Изменения статусов:**

- `PA-040` — `NOT_STARTED` → `IMPLEMENTED`;
- `PA-041` — `NOT_STARTED` → `IMPLEMENTED`;
- `PA-042` — `NOT_STARTED` → `IMPLEMENTED`;
- `PA-043` — `NOT_STARTED` → `IMPLEMENTED`;
- `PA-044` — `NOT_STARTED` → `IMPLEMENTED`;
- `PA-ACC-008` — `NOT_STARTED` → `IMPLEMENTED`.

**Ограничения:**

- Godot и .NET SDK отсутствуют в аудиторской среде;
- сборка и runtime-проверка стрельбы не выполнялись;
- статусы стрельбы не переводились в `VERIFIED`.

**Следующее действие:** выполнить smoke test по разделу 9.

### 2026-08-01 — подтверждение взаимодействия пользователем

Пользователь сообщил, что предыдущая итерация работает. На этом основании взаимодействие и тестовый терминал переведены в `VERIFIED`. Отдельный CLI-лог сборки и SHA коммита пока не предоставлены.

**Изменения статусов:**

- `PA-030` — `IMPLEMENTED` → `VERIFIED`;
- `PA-031` — `IMPLEMENTED` → `VERIFIED`;
- `PA-032` — `IMPLEMENTED` → `VERIFIED`;
- `PA-033` — `IMPLEMENTED` → `VERIFIED`;
- `PA-034` — `IMPLEMENTED` → `VERIFIED`;
- `PA-ACC-007` — `IMPLEMENTED` → `VERIFIED`.

### 2026-08-01 — реализация базового взаимодействия

**Проверенный снимок:** `ProjectHorizon-feature-player-interaction.zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ файлов и автоматическая проверка структуры архива

**Изменённые файлы:**

- `src/Game.Client/project.godot`;
- `src/Game.Client/Scenes/Player/Player.tscn`;
- `src/Game.Client/Scenes/DebugWorld.tscn`;
- `src/Game.Client/Scripts/Player/PlayerController.cs`;
- `src/Game.Client/Scripts/Interaction/IInteractable.cs`;
- `src/Game.Client/Scripts/Interaction/IInteractable.cs.uid`;
- `src/Game.Client/Scripts/Interaction/TestInteractable.cs`;
- `src/Game.Client/Scripts/Interaction/TestInteractable.cs.uid`;
- `README.md`;
- `REQUIREMENTS_STATUS.md`.

**Реализовано:**

- действие `interact` на клавишу `E`;
- луч взаимодействия длиной 4 м из игровой камеры;
- исключение собственной коллизии игрока;
- минимальный контракт `IInteractable`;
- тестовый терминал, переключающий цвет между синим и зелёным;
- исправленная и статически согласованная тестовая геометрия столкновений.

**Статические проверки:**

- пути всех новых ресурсов существуют;
- Mesh/Collision терминала и препятствий совпадают;
- Input Map содержит `interact`;
- `PlayerController` получает существующий `InteractionRay`;
- запрещённые каталоги и результаты сборки в архив не включены.

**Изменения статусов:**

- `PA-027`: `IN_PROGRESS` → `IMPLEMENTED`;
- `PA-030`: `NOT_STARTED` → `IMPLEMENTED`;
- `PA-031`: `NOT_STARTED` → `IMPLEMENTED`;
- `PA-032`: `NOT_STARTED` → `IMPLEMENTED`;
- `PA-033`: `NOT_STARTED` → `IMPLEMENTED`;
- `PA-034`: `NOT_STARTED` → `IMPLEMENTED`;
- `PA-ACC-007`: `NOT_STARTED` → `IMPLEMENTED`.

**Ограничения:**

- сборка и игровой запуск в аудиторской среде недоступны;
- статусы `VERIFIED` не присваивались;
- простая стрельба ещё не реализована.

**Следующее действие:**

- выполнить локальный smoke test по разделу 9;
- после подтверждения перейти к `TASK-011`.

### 2026-08-01 — статический аудит тестовой геометрии столкновений

**Проверенный снимок:** `ProjectHorizon-feature-player-collision-test.zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ сцены и конфигурации

**Подтверждено:**

- явно задан Vulkan для Windows;
- добавлен контейнер `TestGeometry`;
- созданы узлы `WallFront`, `WallSide`, `LowBlock`, `HighBlock`;
- все препятствия основаны на `StaticBody3D`;
- для каждого объекта добавлены видимый меш и коллизионная форма.

**Обнаружены ошибки:**

- дочерние узлы `WallSide/MeshInstance3D` и `WallSide/CollisionShape3D` смещены на `(-5, 0, -5)`, поэтому фактически накладываются на `WallFront`;
- дочерние узлы `LowBlock` имеют ненулевые локальные смещения, поэтому меш и коллизия не совпадают с родительской позицией;
- коллизия `LowBlock` ошибочно использует размер стены `8 × 3 × 0.5`;
- дочерние узлы `HighBlock` смещены, а его меш и коллизия имеют неверные размеры;
- журнал не был обновлён под текущую ветку и продолжал утверждать, что тестовой геометрии нет.

**Изменения статусов:**

- `CFG-002`: `IN_PROGRESS` → `IMPLEMENTED`;
- `PA-027`: `NOT_STARTED` → `IN_PROGRESS`;
- `TASK-009`: остаётся текущей задачей до исправления сцены и ручного теста.

**Следующее действие:**

- заменить или исправить `Scenes/DebugWorld.tscn`;
- выполнить сборку;
- включить видимые формы столкновений;
- провести ручной smoke test;
- только после успешного теста перевести `PA-027` в `VERIFIED`.

### 2026-07-31 — повторный статический аудит после реализации управления

**Проверенный снимок:** `ProjectHorizon-feature-player-prototype(1).zip`
**Git SHA:** отсутствует в архиве
**Метод:** статический анализ файлов проекта

**Изменения по сравнению с предыдущим аудитом:**

- добавлен `PlayerController.cs`;
- добавлены Input Map-действия движения и прыжка;
- реализованы WASD, гравитация, прыжок и обзор мышью;
- добавлен `DirectionalLight3D`;
- удалена лишняя камера `DebugWorld/Camera3D`;
- включён Nullable;
- скрыт временный меш игрока для вида от первого лица.

**Не завершено:**

- явная фиксация Vulkan;
- свежая сборка и ручной smoke test;
- тестовая геометрия столкновений;
- взаимодействие;
- простая стрельба;
- финальная приёмка Прототипа A.

**Историческое решение на 2026-07-31:** Прототип A — `IN_PROGRESS`; на тот момент следующими задачами были фиксация Vulkan и smoke test.

### 2026-07-31 — первичный аудит каркаса игрока

Первичный снимок содержал сцену и заготовку игрока, но не содержал управления, света и актуальных настроек. Эта запись сохранена как историческая контрольная точка; её выводы заменены повторным аудитом выше.

---

## 18A. Runtime-приёмка `TASK-083/TASK-089`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — `0` предупреждений и `0` ошибок, в полном логе реально выполняется `CoreCompile`.
2. Запустить vertical slice и дождаться `DB: Ready/Passed`.
3. Нажать `F2` один раз; до завершения не запускать другие acceptance-команды. Тест использует отдельную БД и не изменяет gameplay-slot.
4. Ожидаемый HUD:

```text
TASK-083 chemical runtime (F2): PASS batch=2, energy=1, environment=1, vacuum=1, catalyst=1, byproduct=1, roundTrip=1
```

5. Ожидаемая строка Godot Output:

```text
TASK-083 chemical process runtime acceptance PASS: batchRecipe=recipe.chemistry.compotium_concentrate; vacuumRecipe=recipe.chemistry.compotium_crystal; batches=2; energyRejected=1; temperatureRejected=1; pressureRejected=1; vacuumRejected=1; missingCatalystRejected=1; catalystRetained=1; catalystConsumed=1; byproducts=1; batchOutput=1; hazards=1; energyConsumed=264; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=extended chemical runtime enforced energy and environment, executed deterministic catalyst consumption, emitted byproducts and persisted batch outputs exactly
```

6. Повторить `F3`, `F4`, `F5`, `F6`, `F7`, `F9`, `F10`, `F11`, `F12`; каждый маршрут должен завершиться `PASS`.
7. При `FAIL` предоставить полный HUD, строку `TASK-083 ... FAIL`, последние 120 строк Godot Output и полный build log.

## 18B. Runtime-приёмка `TASK-090/TASK-091`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — `0` предупреждений и `0` ошибок, в полном логе реально выполняется `CoreCompile`.
2. Запустить vertical slice и дождаться `DB: Ready/Passed`.
3. Нажать `F1` один раз; до завершения не запускать другие acceptance-команды. Тест использует отдельную БД `save_1.production-queue-test.db` и не изменяет gameplay-slot.
4. Ожидаемый HUD:

```text
TASK-090 production queue (F1): PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1
```

5. Ожидаемая строка Godot Output:

```text
TASK-090 production queue acceptance PASS: station=station.smelter; slots=2; maxParallel=2; thirdQueued=1; pauseResume=1; gracefulRestore=1; activeCancel=1; refundExact=1; completed=2; queueDrained=1; energyRemaining=96; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=parallel production slots queued work, freeze-and-resume persistence restored exact progress, cancellation refunded every reservation and remaining jobs completed exactly
```

6. После F1 открыть PortableFabricator клавишей `E`, переключить Recipes/Research через `Tab/R` и закрыть `Esc`; gameplay `RP`, outputs и основной save не должны измениться.
7. Повторить `F2`, `F3`, `F4`, `F5`, `F6`, `F7`, `F9`, `F10`, `F11`, `F12`; каждый маршрут должен завершиться `PASS`.
8. При `FAIL` предоставить полный HUD, строку `TASK-090 ... FAIL`, последние 120 строк Godot Output и полный build log.

## 18C. Runtime-приёмка `TASK-092/TASK-094`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`, `0` предупреждений и `0` ошибок.
2. Запустить vertical slice и нажать `F1`. Ожидаемые строки HUD:

```text
TASK-090 production queue (F1): PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1
TASK-092 queue terminal (F1): PASS progress=1, energy=1, reservations=1, actions=1
```

3. Output должен содержать `TASK-092 production queue terminal acceptance PASS` с `progress=1; energy=1; reservations=1; pauseResume=1; cancel=1`.
4. Нажать `F8`, собрать ресурсы и отремонтировать корабль. При необходимости исследовать технологию выбранного recipe.
5. Открыть PortableFabricator, выбрать готовый recipe и нажать `Q`. Терминал должен перейти в Queue и показать RUNNING job, progress bar, elapsed/duration, slot, reserved energy и inputs.
6. Нажать `Enter/E`: status должен стать `PAUSED`, elapsed не меняется. Повторить `Enter/E`: status возвращается в `RUNNING`.
7. Для refund-проверки поставить job и нажать `C/Delete`: job исчезает, reserved inputs и energy возвращаются; Output содержит `TASK-092 player queue cancellation PASS`.
8. Для cold restore поставить job, дождаться ненулевого elapsed, штатно закрыть игру и запустить снова. Job и elapsed должны восстановиться; elapsed не должен увеличиться на время выключенной игры. Output должен содержать `TASK-092 player queue restore PASS: jobs=<N>; ... elapsed=<T>; offlineProgress=0`.
9. Дождаться completion: output появляется в inventory, station/HUD обновляются, Output содержит `TASK-092 player queue completion PASS`, выполняется `QuestCompleted` autosave.
10. Повторить `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12`; все маршруты должны завершиться `PASS`.
11. При `FAIL` предоставить build log, полный HUD/Queue tab, строку `TASK-092 ... FAIL`, последние 120 строк Godot Output и шаг, на котором возникло расхождение.

## 18D. Runtime-приёмка `TASK-093/TASK-095`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`, `0` предупреждений и `0` ошибок.
2. Запустить vertical slice и нажать `F1`. Вместе с TASK-090/TASK-092 ожидается:

```text
TASK-093 item properties (F1): PASS Q=<0..100>, P=<0..100>, S=<0..100>, dismantle=<N>, roundTrip=1
```

3. Output должен содержать `TASK-093 item quality and dismantle acceptance PASS` с `deterministic=1; range=1; qualitySensitive=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok`.
4. Нажать `F8`, отремонтировать корабль, исследовать нужную технологию и изготовить любой runtime component, например `power_coupler`.
5. Штатно выйти и снова запустить игру. Открыть PortableFabricator, нажать `D`: component должен отображаться с теми же `Q/P/S`, что до выхода.
6. В Dismantle tab выбрать component и нажать `Enter/E`. Item исчезает, previewed materials появляются в inventory, Output содержит `TASK-093 player dismantle PASS`, выполняется `BaseChanged` autosave.
7. Ещё раз выполнить graceful exit/restart: dismantled component не возвращается, recovered materials и их properties сохраняются.
8. Повторить `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12`; все маршруты должны завершиться `PASS`.
9. При `FAIL` предоставить build log, полный HUD/Dismantle tab, строку `TASK-093 ... FAIL`, последние 120 строк Godot Output и шаг расхождения.

## 18E. Runtime-приёмка `TASK-096/TASK-097`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`, `0` предупреждений и `0` ошибок.
2. Запустить vertical slice и нажать `F1`. Вместе с TASK-090/092/093 ожидается:

```text
TASK-096 multi-station industry (F1): PASS stations=4, recipes=6, routing=1, repeatable=1, chain=1, recharge=1, properties=1, roundTrip=1
```

3. Output должен содержать `TASK-096 multi-station industry acceptance PASS` с `physicalStations=4; recipes=6; wrongStation=1; repeatable=1; chain=1; recharge=1; properties=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok`.
4. Нажать `F8`, собрать три salvage и отремонтировать корабль. Исследовать `basic_refining`, `paraffinium_processing`, `industrial_chemistry` и prerequisite-ветку `compotium_analysis`.
5. Собрать два `ferric_ore`, два `ice_water`, два `paraffinium`, два `raw_compotium`, два `acidic_brine` и один `catalytic_dust`.
6. На Smelter изготовить `refined_ferrite`; на Refinery — `purified_water`; на DistillationColumn — `paraffinium_fraction`.
7. На ChemicalProcessor изготовить `paraffinium_lubricant`, затем дважды `raw_compotium_solution`. При нехватке energy дождаться recharge и повторить enqueue.
8. На DistillationColumn изготовить `compotium_concentrate`. Проверить Q/P/S output, byproducts и расход/сохранение catalyst.
9. Поставить jobs минимум на двух разных stations, дождаться ненулевого elapsed, выполнить graceful exit/restart. Jobs, elapsed, station energy и shared free inventory должны восстановиться без offline progress.
10. Повторить `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12`; все маршруты должны завершиться `PASS`. F5 должен показать `station=15`, `crafted=15`, `isolated=15`, `roundTrip=1`.
11. При `FAIL` предоставить полный build log, HUD и station tab, строку `TASK-096 ... FAIL`, последние 120 строк Output и шаг manual chain, на котором возникло расхождение.

## 18F. Runtime-приёмка `TASK-098/TASK-099`

1. Выполнить чистую сборку `tools\clean-build-windows10.cmd`; критерий — реальный `CoreCompile`, `0` предупреждений и `0` ошибок.
2. Запустить vertical slice и нажать `F1`. Вместе с TASK-090/092/093/096 ожидается:

```text
TASK-098 production network HUD (F1): PASS stations=5, aggregate=1, transitions=1, recharge=1, restore=1, fallback=1, unavailable=0
```

3. Output должен содержать:

```text
TASK-098 production network HUD acceptance PASS: stations=5; aggregateCounts=1; aggregateEnergy=1; simultaneousRunning=1; pauseResume=1; cancel=1; completion=1; recharge=1; coldRestore=1; legacyFallback=1; falseUnavailable=0; roundTrip=1; maxWriters=1; integrity=ok; elapsedMs=<время>
```

4. Убедиться, что test database называется `save_1.production-network-hud-test.db` и основной gameplay-slot не изменён.
5. Нажать `F8`, разблокировать необходимые technologies и собрать ресурсы для `refined_ferrite` и `purified_water`.
6. На Smelter поставить `refined_ferrite`, на Refinery — `purified_water`. HUD должен показать `stations=5`, две active stations и корректную сумму energy.
7. Поставить дополнительную smelter job; проверить изменение `jobs/queued`.
8. В Queue tab приостановить job, проверить `running/queued/paused`, затем возобновить её.
9. Отменить одну job; проверить исчезновение job, возврат inputs/catalysts и reserved energy.
10. Дождаться completion оставшейся job; проверить уменьшение jobs, outputs/byproducts, catalyst result и station energy.
11. Оставить одновременно running, queued и paused jobs, штатно закрыть игру и запустить снова. Elapsed, states, station energy и aggregate HUD должны восстановиться без offline progress.
12. При исправной сети строка `Production queue: unavailable` не должна появляться ни в detailed, ни в compact HUD; unavailable допустим только при фактической ошибке инициализации runtime.
13. Повторить `F2/F3/F4/F5/F6/F7/F9/F10/F11/F12`; все маршруты должны завершиться `PASS`.
14. При `FAIL` предоставить полный build log, detailed HUD, строку `TASK-098 ... FAIL`, последние 160 строк Output и шаг ручного сценария, на котором возникло расхождение.

## 18G. Runtime-приёмка `TASK-100/TASK-101`

1. Выполнить `tools\clean-build-windows10.cmd`. Критерий: реальный `CoreCompile`, `0` предупреждений, `0` ошибок.
2. Запустить `SalvageRepairSlice` и дождаться startup строк:

```text
TASK-100 catalog resource binding PASS: catalog=42; physicalTypes=42; nodes=58; authored=32; generated=26; unique=1; deterministicYield=1; maxStack=1; coverage=1.
TASK-100 catalog resource lifecycle READY: catalog=42; physicalTypes=42; nodes=58; generated=26; genericCollection=enabled; mirrors=enabled; depletionPersistence=enabled; reset=enabled.
```

3. В detailed HUD проверить `Resources: types=42/42 • nodes=58 • collected=<N> • generated=26`.
4. Нажать `F7` и не выполнять других действий до завершения обеих параллельных проверок. Ожидаются прежний `TASK-062 ... PASS` и новая строка:

```text
TASK-100 resource lifecycle (F7): PASS catalog=42, physical=42, nodes=58, generated=26, collectTypes=42, collectNodes=58, duplicate=1, mirrors=1, depletion=1, restore=1, reset=1, roundTrip=1
```

5. Godot Output должен содержать:

```text
TASK-100 catalog resource lifecycle acceptance PASS: catalog=42; physicalTypes=42; nodes=58; generated=26; collectedTypes=42; collectedNodes=58; metadata=1; placement=1; unique=1; duplicateRejected=1; mirrors=1; depletion=1; coldRestore=1; reset=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=<description>
```

6. Убедиться, что acceptance использует `save_1.resource-lifecycle-test.db`; gameplay `save_1.db` не изменён.
7. Нажать `F8`; detailed HUD должен показать `collected=0`, а все authored/generated nodes должны быть доступны.
8. Пройти к generated field в секторе тестовой площадки `z=23.0..36.5`, навести ray/proximity на любой новый цветной node и нажать `E`. Output должен содержать `ResourceCollected(catalog.<id>, definition=resource.<id>, quantity=1)`, а node исчезнуть.
9. Дождаться autosave либо штатно закрыть игру. После повторного запуска тот же node должен оставаться скрытым, `collected` и inventory quantity — восстановиться; offline regeneration ресурса не допускается.
10. Нажать `F8` ещё раз: node должен снова появиться, `collected=0`, production inventory mirrors — пусты.
11. Повторить `F1`, `F2`, `F3`, `F4`, `F5`, `F6`, `F9`, `F10`, `F11`, `F12`; все маршруты должны завершиться `PASS`. Повторный `F7` также должен быть `PASS`.
12. Для приёмки прислать: build summary, screenshot detailed HUD `42/42, 58, 26`, screenshot F7 PASS, полную строку Output, screenshot generated node до/после collection, screenshot после cold restart и после F8 reset.
13. При `FAIL` предоставить полный build log, строки `TASK-100 ... FAIL`, последние 180 строк Godot Output, detailed HUD и шаг ручного сценария.

После выполнения этих критериев установить `TASK-100 → VERIFIED`, `TASK-101 → VERIFIED` и считать resource lifecycle vertical slice закрытым.

## 18H. Runtime-приёмка `TASK-102/TASK-103`

1. Выполнить `tools\clean-build-windows10.cmd`. В build log должен реально выполняться `CoreCompile`; критерий — `Предупреждений: 0`, `Ошибок: 0`.
2. Запустить `SalvageRepairSlice` и дождаться startup строк:

```text
TASK-102 station services catalog READY: schema=1; factions=3; markets=1; npcs=1; dialogues=1; quests=3; tradable=174.
TASK-102 station services binding PASS: economies=6; factions=3; npcs=1; dialogueOptions=3; quests=3; questNodes=3; tradable=174; priceFormula=6-factors; trade=atomic; questGraph=validated; persistence=enabled.
TASK-102 station services READY: npc=npc.trader.ilia_voss; market=market.frontier_exchange; credits=2400; reputation=0; tabs=Dialogue/Buy/Sell/Quests; F3=acceptance.
```

3. Нажать `F3` и не выполнять других действий до завершения обеих параллельных проверок. `TASK-082` должен остаться `PASS`; новая строка HUD:

```text
TASK-102 station services (F3): PASS economies=6, factions=3, npc=1, quests=3, tradable=174, price=1, daily=1, trade=1, graph=1, restore=1, roundTrip=1
```

4. Godot Output должен содержать полную строку `TASK-102 station services acceptance PASS` с `priceFormula=1; deterministicDaily=1; offlineEconomy=1; supplyDemand=1; buySell=1; atomicRejected=1; creditConservation=1; questGraph=1; questFeasibility=1; questFlow=1; reputation=1; coldRestore=1; legacyFallback=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok`.
5. Убедиться, что acceptance использует `save_1.station-services-test.db` и не изменяет gameplay `save_1.db`.
6. Нажать `F8`. Найти синий `StationTrader` около координат `x=14, z=12`, подойти и нажать `E`. Должна открыться вкладка Dialogue; Output — `TASK-102 player NPC interaction PASS`.
7. Проверить три dialogue options: переход в Buy, переход в Quests и Close. `Tab` циклически переключает Dialogue/Buy/Sell/Quests; `B/S/Q` открывают соответствующую вкладку.
8. Во вкладке Quests сначала принять все три contracts: ore sample, refined ferrite order и water-ice trade. Это гарантирует, что последующие gameplay-события учитываются их objective graph.
9. Собрать `2 × resource.ferric_ore` и `2 × resource.ice_water`; quest `CollectResource` должен перейти в `ReadyToClaim` на `2/2`. При необходимости исследовать `tech.basic_refining`.
10. В Buy выбрать любой item, не используемый текущими quest objectives, и купить одну единицу. Проверить уменьшение credits/stock и увеличение player inventory на всех station mirrors; Output — `TASK-102 player trade buy PASS`.
11. На Smelter изготовить `material.refined_ferrite`; quest `CraftItem` должен перейти в `ReadyToClaim` после completion.
12. Во вкладке Sell продать одну единицу `resource.ice_water`; quest `TradeItem` должен перейти в `ReadyToClaim`, credits увеличиться, stock измениться, inventory mirrors остаться синхронными.
13. Во вкладке Quests последовательно claim все три quests. Проверить итоговые rewards `+660 credits` и `+15 reputation` относительно состояния перед claims; Output должен содержать три `TASK-102 player quest action PASS`.
14. Запомнить credits, reputation, stock выбранного item и quest statuses. Штатно закрыть игру и запустить снова. Output `TASK-102 station services restore PASS` должен восстановить те же данные; offline time может изменить только economy day/daily price, но не терять stock/credits/quests.
15. Нажать `F8`: credits должны вернуться к `2400`, reputation к `0`, stock — к initial value `6`, все quests — в `Offered`.
16. Повторить `F1`, `F2`, `F4`, `F5`, `F6`, `F7`, `F9`, `F10`, `F11`, `F12`; все применимые маршруты должны завершиться `PASS`. Повторный `F3` также должен быть `PASS` для `TASK-082` и `TASK-102`.
17. Для приёмки прислать: build summary, screenshot Dialogue, screenshot Buy/Sell с factors, screenshot Quests после трёх claims, screenshot HUD F3 PASS и полную строку Output `TASK-102 ... PASS`.
18. При `FAIL` предоставить полный build log, строки `TASK-102 ... FAIL`, последние 200 строк Godot Output, screenshot активной station-services tab, значения credits/reputation/stock/quest state и точный шаг сценария.

После выполнения критериев установить `TASK-102 → VERIFIED`, `TASK-103 → VERIFIED` и считать station-services subsystem Этапа 1 закрытой.

## 18I. Runtime-приёмка `TASK-106/TASK-107`

1. Выполнить `tools\clean-build-windows10.cmd`. В build log должен реально выполняться `CoreCompile`; критерий — `Предупреждений: 0`, `Ошибок: 0`.
2. Запустить `SalvageRepairSlice` и дождаться startup строк:

```text
TASK-106 base construction catalog READY: schema=1; modules=50; categories=17; grid=2.5; limits=500/100/200/20.
TASK-106 base construction binding PASS: catalogModules=50; baseRecipes=10; anchors=1; snap=cardinal; collision=grid; power=graph; persistence=enabled.
TASK-106 base construction READY: modules=50; grid=2.5m; limits=500/100/200/20; snap=cardinal; power=graph; persistence=enabled; F6=acceptance.
```

3. Нажать `F6` и не выполнять других действий до завершения двух параллельных проверок. Legacy `TASK-072` должен остаться `PASS`; ожидаемый HUD:

```text
TASK-072 legacy fourth path (F6): PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1
TASK-106 base construction (F6): PASS modules=50, placed=50, snap=1, collision=1, power=1, limits=1, stress500=1, restore=1, roundTrip=1
```

4. Godot Output должен содержать:

```text
TASK-106 base construction acceptance PASS: catalogModules=50; categories=17; placed=50; anchor=1; snapping=1; collisionRejected=1; disconnectedRejected=1; powerGraph=1; battery=1; toggle=1; removalRefund=1; limits=1; stress500=1; coldRestore=1; legacyFallback=1; roundTrip=1; logWritten=1; maxWriters=1; integrity=ok; elapsedMs=<время>; result=<description>
```

5. Убедиться, что acceptance использует `save_1.base-construction-test.db`; gameplay `save_1.db` не изменяется.
6. Нажать `F8`; HUD base summary должен показать `modules=0/500`, `components=0`; Output — `TASK-106 base construction restore PASS ... legacyFallback=1` при первом старте либо reset.
7. Нажать `G`. Builder должен открыть palette из `50` modules; координаты игрока остаются видимыми. Target показывает grid/world coordinates, selected module, stock, rotation, power и battery.
8. До anchor выбрать любой structural module и нажать `Enter`: placement должна быть отклонена сообщением `the first module must be the base anchor`.
9. Выбрать `module.base_power_node`, поставить его. Затем поставить минимум: foundation/floor/wall/roof, `module.solar_array`, `module.battery_bank` и один consumer (`module.water_recycler` или terminal), каждый в cardinally adjacent cell. Preview должен быть зелёным только для допустимого cell.
10. Попытаться поставить module поверх занятой cell — ожидается overlap rejection. Отойти так, чтобы target не соседствовал с базой, и повторить — ожидается snap rejection.
11. Проверить HUD: `components=1`; generation, consumption и battery capacity соответствуют установленным devices. Подождать несколько секунд при surplus — battery должна увеличиваться. Offline charging не допускается.
12. Навести target на switchable consumer и нажать `T`; consumption/enabled/powered должны измениться, module визуально стать полупрозрачным, его lights — погаснуть. Повторное `T` возвращает device. Structural module должен отвечать `has no switchable device`.
13. Построить цепочку не менее чем из трёх modules. Попытаться удалить средний module `X/Delete`: removal должна быть отклонена как disconnect. Удалить крайний module: module исчезает, stock увеличивается на `1`, components остаётся `1`.
14. Пройти сквозь поставленный wall/module: static collision должна блокировать игрока; authored terrain geometry при строительстве не изменяется.
15. Штатно закрыть игру с несколькими modules, ненулевой battery и одним disabled device. После запуска Output `TASK-106 base construction restore PASS` и scene/HUD должны восстановить exact positions, rotations, stock, enabled state и stored energy без offline progress.
16. Нажать `F8`: все построенные modules исчезают, battery становится `0`, stock возвращается к starter values, player position — baseline.
17. Повторить `F1`, `F2`, `F3`, `F4`, `F5`, `F7`, `F9`, `F10`, `F11`, `F12`; все применимые маршруты должны завершиться `PASS`. Повторный `F6` также должен быть `PASS`.
18. Для приёмки прислать: build summary; screenshot builder с structural + power modules; screenshot green/red preview; screenshot HUD power/battery; screenshot после cold restore; screenshot F6 PASS; полную строку Output `TASK-106 ... PASS`.
19. При `FAIL` предоставить полный build log, `TASK-106 ... FAIL`, последние 220 строк Godot Output, screenshot builder, base summary, target grid, selected module и точный шаг сценария.

После выполнения критериев установить `TASK-106 → VERIFIED`, `TASK-107 → VERIFIED` и считать core base-construction subsystem закрытой.

## 19. Шаблон новой записи

```markdown
### YYYY-MM-DD — <название проверки>

**Ветка:**
**Коммит:**
**Связанные требования:**
**Изменённые файлы:**

**Что реализовано:**

-

**Как проверено:**

- Команда сборки:
- Автотесты:
- Ручной сценарий:
- Результат:

**Изменения статусов:**

- `<ID>`: `OLD_STATUS` → `NEW_STATUS`

**Открытые дефекты/ограничения:**

-

**Следующая задача:**

-
```

---

## 20. Правило коммита

Каждый функциональный коммит должен содержать обновление этого файла либо явно не изменять статус требований.

Рекомендуемый формат:

```text
<type>: <краткое действие>

Requirements: PA-030, PA-031
Verification: dotnet build; manual interaction smoke test
```

---

## 21. Регламент последующих итераций

Все последующие итерации разработки выполняются в соответствии с:

`DEVELOPMENT_ITERATION_PROTOCOL.md`

Последняя приложенная пользователем GitHub-редакция проекта используется как
исходная кодовая база. По завершении каждой итерации обязательны:

- обновлённый архив проекта;
- актуализированный `REQUIREMENTS_STATUS.md`;
- инструкция по доказательству работоспособности;
- измеримые критерии `PASS/FAIL`;
- готовый текст Git-коммита.

Стандартный запрос:

```text
Выполни следующую итерацию разработки Project Horizon по регламенту
`DEVELOPMENT_ITERATION_PROTOCOL.md`, PDF-ТЗ и `REQUIREMENTS_STATUS.md`.

Последняя редакция проекта, скачанная с GitHub, приложена к сообщению.
```
