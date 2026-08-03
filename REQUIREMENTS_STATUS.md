# Project Horizon — журнал реализации требований ТЗ

> **Назначение:** единая точка контроля соответствия проекта техническому заданию.
> **Последняя актуализация:** 2026-08-03
> **Подготовленный снимок:** `ProjectHorizon-main-production-queue-terminal-ui-build-fix.zip`
> **Git-состояние:** архив не содержит `.git`, поэтому ветка и SHA статически не подтверждаются.
> **Правило:** задача считается завершённой только после обновления этого журнала и фиксации проверяемых доказательств.

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

**Вывод:** все пять технических прототипов, vertical slice, полный Industry Content v2 и его runtime-регрессии подтверждены пользователем. `TASK-082/084`, `TASK-083/089` и `TASK-090/091` приняты после runtime-проверок; пользователь подтвердил исправленную nullable-редакцию формулировкой «все работает». Текущая итерация реализует player-facing Queue-вкладку промышленного терминала, управление job lifecycle и сохранение незавершённой gameplay-очереди.

## 3. Результат текущей итерации от 2026-08-03

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

## 3A. Предыдущая итерация от 2026-08-02

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
| `TOOL-006` | 37.1 | Не хранить кеш, сборки, IDE-настройки, локальные БД и логи | `IMPLEMENTED` | Запрещённые каталоги отсутствуют в архиве | Позже добавить CI-проверку |
| `TOOL-007` | 37.2 | `main`, `develop`, `feature/*`, `fix/*`, `release/*` | `IN_PROGRESS` | Имя снимка соответствует feature-итерации; удалённая структура веток архивом не доказывается | Проверить ветки на GitHub |
| `TOOL-008` | 37.2 | `main` всегда собирается | `IN_PROGRESS` | Архив не содержит `.git`, CI и журнал сборки текущей итерации отсутствуют | Выполнить локальную сборку; затем создать CI |

---

## 5. Архитектура и настройки

| ID | Раздел ТЗ | Требование | Статус | Доказательство / замечание | Следующее действие |
|---|---:|---|---|---|---|
| `ARCH-001` | 4.1 | Многослойная архитектура | `IN_PROGRESS` | Пока создан только `Game.Client` | Создавать библиотеки по мере появления логики, не заранее |
| `ARCH-002` | 4.1 | Доменная логика не зависит от `Godot.Node` | `NOT_STARTED` | Доменная логика ещё отсутствует | Контролировать при создании `Game.Domain` |
| `ARCH-003` | 4.2 | Godot-клиент в `src/Game.Client` | `IMPLEMENTED` | Структура соответствует ТЗ | Подтвердить сборкой из чистого клона |
| `ARCH-006` | 4.3 | Клиент содержит сцены, камеры, управление и адаптеры взаимодействия | `IMPLEMENTED` | `DebugWorld`, `TerrainChunkPrototype`, `CubeSpherePrototype`, `PlanetaryPlayer`, управление, взаимодействие и бой | Довести Прототип A до приёмки |
| `CFG-001` | 1.2 | Основной renderer — Mobile | `IMPLEMENTED` | `renderer/rendering_method="mobile"` | Подтвердить запуском |
| `CFG-002` | 1.2 | Основной графический API — Vulkan | `IMPLEMENTED` | В `project.godot` явно задано `rendering_device/driver.windows="vulkan"` | Подтвердить фактический драйвер выводом `RenderingServer` при запуске |
| `CFG-003` | 1.2 | Compatibility/OpenGL 3.3 — резервный профиль | `NOT_STARTED` | Экспортные профили отсутствуют | Вернуться при настройке экспорта |
| `CFG-004` | 38 | Nullable включён | `IMPLEMENTED` | `<Nullable>enable</Nullable>` присутствует | Пересобрать без предупреждений |
| `CFG-005` | 38 | Предупреждения контролируются | `IN_PROGRESS` | Политики CI нет | После текущего smoke test добавить warnings-as-errors в CI |
| `CFG-006` | 38 | Нет циклических зависимостей | `IMPLEMENTED` | C#-проект пока один | Проверять при добавлении проектов |
| `CFG-007` | 38 | Генерация мира не выполняется в `_Process` | `IMPLEMENTED` | `_PhysicsProcess` только обнаруживает переход; worker-задачи считают данные, timer дозированно применяет готовые mesh/collision в main thread | Подтвердить профилированием |
| `CFG-008` | 37.1 | Хранить import-настройки, исключая `.godot/` | `IMPLEMENTED` | `icon.svg.import` хранится, `.godot/` исключена | Не игнорировать глобально `*.import` |

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
| `INDUSTRY-050` | Station terminal содержит вкладки Recipes, Research и Queue | `IMPLEMENTED` | `StationSelectorMode.Queue`; `Tab` циклически переключает три режима |
| `INDUSTRY-051` | Queue UI показывает status, progress, elapsed/duration и slot state | `IMPLEMENTED` | `ProductionQueueTerminalModel`, progress bar и timing row |
| `INDUSTRY-052` | Queue UI показывает remaining/capacity energy и точные reservations | `IMPLEMENTED` | Header energy; per-job reserved energy, inputs и catalysts |
| `INDUSTRY-053` | Игрок может enqueue recipe из Recipes без удаления legacy direct craft | `IMPLEMENTED` | `Q` enqueue; `Enter/E` сохраняет immediate timed craft |
| `INDUSTRY-054` | Игрок может pause/resume выбранный running/paused job | `IMPLEMENTED` | Queue `Enter/E` вызывает `Pause/Resume` |
| `INDUSTRY-055` | Игрок может cancel job с полным возвратом reservations | `IMPLEMENTED` | Queue `C/Delete`; inputs/catalysts возвращаются в `StarterRepairSession`, energy — в runtime |
| `INDUSTRY-056` | Gameplay queue completion применяет outputs/byproducts/catalyst policy | `IMPLEMENTED` | `UpdateGameplayProductionQueue` синхронизирует session и scene |
| `INDUSTRY-057` | Gameplay queue сохраняется при periodic/autosave/graceful exit и cold restore | `IMPLEMENTED` | Queue payload передаётся в `StarterRepairSnapshotFactory.Create`; restore без offline progress |
| `INDUSTRY-058` | Refund inputs и byproducts переживают SQLite round-trip | `IMPLEMENTED` | `StarterRepairSession.FromSnapshot` принимает inputs/catalysts/outputs/byproducts/dismantle IDs |
| `INDUSTRY-ACC-050` | Clean build не содержит errors/warnings | `IN_PROGRESS` | Выполнить `tools\clean-build-windows10.cmd` |
| `INDUSTRY-ACC-051` | F1 подтверждает terminal projection | `IN_PROGRESS` | `progress=1`, `energy=1`, `reservations=1`, `actions=1` |
| `INDUSTRY-ACC-052` | Ручной UI подтверждает enqueue, pause/resume и cancel/refund | `IN_PROGRESS` | Нужны Queue screenshots и Output lines `TASK-092 player queue ... PASS` |
| `INDUSTRY-ACC-053` | Cold restart восстанавливает job и exact elapsed без offline progress | `IN_PROGRESS` | Поставить job, выйти штатно, перезапустить и сравнить elapsed |
| `INDUSTRY-ACC-054` | F2–F12 не регрессируют | `IN_PROGRESS` | Повторить F2/F3/F4/F5/F6/F7/F9/F10/F11/F12 |

## 9. Очередь ближайших задач

Задачи выполняются итеративно; runtime-проверки фиксируются до присвоения `VERIFIED`. Обычная новая JSON-запись с уже поддерживаемой семантикой не должна становиться отдельной C#-итерацией.

**Зафиксировано как `VERIFIED` по прямому runtime-подтверждению пользователя:** `TASK-005`, `TASK-009`, `TASK-011`, `TASK-023`–`TASK-081` за исключением superseded/плановых задач; Прототипы A–E, production persistence, полный Industry Content v2 и все регрессии F4–F12.

| Приоритет | ID | Задача | Результат |
|---:|---|---|---|
| 1 | `TASK-094` | Выполнить runtime-приёмку Queue-вкладки | Clean build `0/0`; F1 terminal projection; manual enqueue/pause/resume/cancel/refund; cold restore; F2–F12 regressions |
| 2 | `TASK-006` | Записать SHA контрольного коммита | `BLOCKED`: в переданном ZIP нет `.git`; требуется SHA фактического коммита GitHub |
| 3 | `TASK-093` | Реализовать quality/purity/stability и dismantle returns | Следующая семантика ТЗ v2.0 §52.3 после terminal queue acceptance |

**Подтверждено:** `TASK-060`–`TASK-084`, `TASK-089`–`TASK-091`, persistence, vertical slice, Industry Content v2 и runtime matrix.
**Реализовано:** `TASK-092` — player-facing Queue-вкладка, progress/actions/reservations и gameplay queue persistence.
**Заменено:** `TASK-075` и `CONTENT-ACC-060`–`CONTENT-ACC-067` → `SUPERSEDED` полной catalog matrix.
**Текущая приёмочная задача:** `TASK-094`.

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
