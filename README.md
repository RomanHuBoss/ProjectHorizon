# Project Horizon

**Project Horizon** — процедурный космический симулятор на Godot Engine с исследованием планет, космическими полётами, добычей ресурсов, крафтом, торговлей, заданиями и строительством баз.

Проект разрабатывается как одиночная игра с возможностью последующего расширения архитектуры для серверных функций и кооперативного режима.

## Технологический стек

- **Godot Engine 4.7.1 .NET**
- **C#**
- **.NET SDK**
- **JetBrains Rider**
- **Git**
- **Git LFS**
- **SQLite** через `Microsoft.Data.Sqlite` — для локальных сохранений
- **JSON** — для статических игровых данных
- **Godot Shader Language** — для шейдеров
- **ASP.NET Core** — для будущей серверной платформы

## Целевые платформы

- Windows 10 x64
- Windows 11 x64
- Linux x86_64

Основной рендерер — **Godot Mobile Renderer на Vulkan**.
Резервный профиль — **Compatibility Renderer на OpenGL 3.3**.

## Текущее состояние

Текущий этап — **Этап 1: вертикальный срез**. Все пять технических прототипов приняты; начата интеграция первого сквозного игрового цикла.

### Industry Content v2, multi-station production network и aggregate HUD — `IMPLEMENTED`

Редакция 2.0 технического задания расширяет промышленную подсистему Project Horizon до полноценного data-driven каталога:

```text
schemaVersion=2
items=174
worldResources=42
recipes=128
stations=15
technologies=32
runtimeEnabledRecipes=16
chemistryRecipes=30
compotiumRecipes=13
paraffiniumRecipes=5
dependencyCycles=0
unreachableRecipes=0
```

Нормативные документы находятся в:

```text
Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.docx
Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf
Technical_Specification/2.0/Project_Horizon_Recipe_Catalog_v2.0.csv
Technical_Specification/2.0/Project_Horizon_Industry_Content_Schema_v2.0.json
```

Каталог статических данных:

```text
src/Game.Client/Content/items.json
src/Game.Client/Content/resources.json
src/Game.Client/Content/recipes.json
src/Game.Client/Content/stations.json
src/Game.Client/Content/technologies.json
src/Game.Client/Content/localization.ru.json
src/Game.Client/Content/localization.en.json
src/Game.Client/Content/catalog_manifest.json
```

Редакция содержит шестнадцать runtime-enabled recipes: стартовый ремонт, девять корабельных компонентов PortableFabricator и связную шестирецептурную линию Refining/Chemistry. В сцене работают пять физических типов станций: PortableFabricator, Smelter, Refinery, DistillationColumn и ChemicalProcessor. Каждая станция получает свой список рецептов из JSON, собственную очередь, слоты и энергетический бюджет, но все станции синхронизированы с единым player inventory. Требования `RequiredTechnology` исполняются доменной моделью, исследовательские очки, разблокировки и сеть незавершённых production jobs сохраняются в SQLite. Queue-вкладка показывает progress bar, elapsed/duration, slot status, energy и точные reservations; поддерживает pause/resume и cancellation с полным возвратом inputs, catalysts и energy. Refining/Chemistry recipes являются повторяемыми, их продукты можно использовать как inputs следующих станций. Энергия каждой station автоматически восстанавливается от нуля до capacity за 60 секунд игрового времени. Основной HUD строится непосредственно из `ProductionNetworkRuntime`: агрегирует jobs, состояния и энергию всех пяти станций, показывает постанционную строку `[R/Q/P]` и не считает исправно инициализированную idle network недоступной.

В состав v2 входят:

- 18 refining recipes;
- 30 chemistry recipes;
- 22 industrial-component recipes;
- 18 ship-module recipes;
- 12 equipment/consumable recipes;
- 10 base recipes;
- 8 drone/vehicle/exotic recipes;
- 10 текущих repair/ship-component recipes.

Химическая линия является канонической частью мира. Она включает Парафиний, добываемый сырой Компотий, растворы и концентраты Компотия, очистку, стабилизацию, катализаторы, электролит, энергетические элементы, реакторный гель и конечные экзотические модули. Название «Компотий» предложено сыном автора проекта и закреплено в ТЗ без изменения.

Recipe schema v2 поддерживает несколько inputs/outputs, catalysts, byproducts, dismantle returns, station/technology tiers, craft time, energy cost, batch size, температуру, давление, вакуум, качество и hazards. `GameContentCatalog` проверяет stable IDs, все ссылки, совместимость station/category/tier, technology graph, циклы и достижимость каждого recipe от мирового сырья.

Текущая стартовая сцена:

```text
src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn
```

Управление:

```text
WASD / Space   движение и прыжок
E              собрать ресурс / ремонтировать / открыть станцию / подтвердить выбор
Up / Down      выбрать рецепт, технологию или queue job
Tab            циклически переключить Recipes / Research / Queue / Dismantle
R              переключить Recipes / Research
D              открыть Dismantle из любой вкладки
Enter / E      craft/unlock; Queue — pause/resume; Dismantle — разобрать item
Q              во вкладке Recipes поставить рецепт в очередь; открыть Queue из других вкладок
C / Delete     отменить выбранный queue job с полным возвратом reservations
Esc            закрыть station UI / освободить курсор
H              detailed / compact / hidden HUD
F1             TASK-090/092/093/096/098: queue, properties, multi-station industry и aggregate HUD
F2             TASK-083: chemical process runtime
F3             TASK-082: universal selector + research + persistence
F4             TASK-080: весь Industry Content v2 (128 recipes)
F5             TASK-076: playable runtime matrix (16 recipes / 15 station recipes)
F6             регрессия coolant path
F7             регрессия salvage → repair
F8             очистить gameplay-slot
F9             регрессия strict JSON catalog
F10            регрессия launch-capacitor persistence
F11            регрессия craft-time state machine
F12            регрессия navigation path
```



Ожидаемый `F1` HUD:

```text
TASK-090 production queue (F1): PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1
TASK-092 queue terminal (F1): PASS progress=1, energy=1, reservations=1, actions=1
TASK-093 item properties (F1): PASS Q=72, P=80, S=80, dismantle=1, roundTrip=1
TASK-096 multi-station industry (F1): PASS stations=4, recipes=6, routing=1, repeatable=1, chain=1, recharge=1, properties=1, roundTrip=1
TASK-098 production network HUD (F1): PASS stations=5, aggregate=1, transitions=1, recharge=1, restore=1, fallback=1, unavailable=0
```

`F1` запускает изолированную проверку smelter queue на два parallel slots. Три jobs резервируют inputs и energy без overcommit; третья job ожидает слот. Проверка выполняет pause/resume, сохраняет незавершённые jobs через `GracefulExit`, восстанавливает точный elapsed progress без offline progress, отменяет активную job с полным возвратом inputs/catalysts/energy, завершает оставшиеся jobs и проверяет финальный `QuestCompleted` SQLite round-trip. Дополнительно строится тот же terminal projection, который используется игровым UI: проверяются progress bar, elapsed time, energy, reservations и допустимые pause/resume/cancel actions. Параллельный изолированный `TASK-093` проверяет детерминированные `Q/P/S`, зависимость dismantle returns от свойств предмета и exact SQLite round-trip. `TASK-096` прогоняет четыре специализированные station types и шесть связанных recipes: refined ferrite, purified water, Paraffinium fraction/lubricant и raw Compotium solution/concentrate. `TASK-098` строит aggregate HUD по всем пяти физическим станциям и проверяет aggregate counts/energy, одновременную работу Smelter и Refinery, pause/resume, cancel/refund, completion, recharge, exact cold restore без offline progress, legacy single-queue fallback и отсутствие ложного `unavailable`. Используются отдельные БД `save_1.production-queue-test.db`, `save_1.item-properties-dismantle-test.db`, `save_1.multi-station-industry-test.db` и `save_1.production-network-hud-test.db`; gameplay-slot не изменяется.


### Multi-station Paraffinium and Compotium starter line

После `F8` в мире доступны дополнительные добываемые узлы и четыре отдельные станции. Линия выполняется последовательно:

```text
2 ferric_ore -> Smelter -> refined_ferrite
2 ice_water -> Refinery -> purified_water
2 paraffinium -> DistillationColumn -> paraffinium_fraction
paraffinium_fraction + refined_ferrite -> ChemicalProcessor -> paraffinium_lubricant
raw_compotium + acidic_brine -> ChemicalProcessor -> raw_compotium_solution (repeatable, выполнить дважды)
2 raw_compotium_solution + purified_water + catalytic_dust -> DistillationColumn -> compotium_concentrate
```

Для постановки процесса подойдите к нужной station, откройте терминал `E`, исследуйте требуемую technology, выберите recipe и нажмите `Q`. Вкладки Queue относятся к конкретной станции; jobs разных станций выполняются параллельно и сохраняются одной `production_queue_network`.

### Aggregate production network HUD

В detailed и compact HUD отображается единая сводка, рассчитанная непосредственно из `ProductionNetworkRuntime`:

```text
Production network: stations=5 • jobs=2 • running=2 • queued=0 • paused=0 • energy=948/1060
Stations: PortableFabricator 80/80 [0R/0Q/0P] • Smelter 140/180 [1R/0Q/0P] • Refinery 248/320 [1R/0Q/0P] • DistillationColumn 300/300 [0R/0Q/0P] • ChemicalProcessor 180/180 [0R/0Q/0P]
```

Detailed mode показывает все станции. Compact mode показывает активные stations и `+N idle stations`. Значение `Production network: unavailable (...)` допустимо только при реальном отсутствии или исключении инициализации runtime; пустая сеть с `jobs=0` остаётся доступной.

Для ручной приёмки после `F8` запустите `refined_ferrite` на Smelter и `purified_water` на Refinery, добавьте queue job, выполните pause/resume и cancel, дождитесь completion, затем штатно перезапустите игру с незавершёнными running/queued/paused jobs. Сводка должна немедленно отражать каждое изменение и восстановить elapsed, states и station energy без offline progress.

### Ручная проверка Queue-вкладки

1. Соберите ресурсы для доступного рецепта и при необходимости исследуйте его технологию.
2. Откройте PortableFabricator, выберите рецепт и нажмите `Q`.
3. Терминал автоматически перейдёт во вкладку Queue. Должны отображаться status, progress bar, elapsed/duration, slot, reserved energy и reserved inputs.
4. Нажмите `Enter` или `E`: running job перейдёт в `PAUSED`, прогресс остановится. Повторное нажатие вернёт job в `RUNNING`.
5. Для проверки отмены нажмите `C` или `Delete`: job исчезает, inputs и energy возвращаются полностью.
6. Для проверки persistence поставьте job в очередь, закройте игру штатно и запустите снова. Job должен восстановиться с тем же elapsed progress; offline progress не начисляется. В Output появляется `TASK-092 player queue restore PASS` с числом jobs и сохранённым elapsed.

Ожидаемый `F2` HUD:

```text
TASK-083 chemical runtime (F2): PASS batch=2, energy=1, environment=1, vacuum=1, catalyst=1, byproduct=1, roundTrip=1
```

`F2` запускает изолированную chemical-runtime проверку на двух рецептах Компотия. Она подтверждает отказ при нехватке энергии и неверной среде, обязательный вакуум, batch output, deterministic catalyst retained/consumed paths, byproducts, hazards, QuestCompleted autosave и exact SQLite round-trip. Основной gameplay-slot не изменяется.

Ожидаемый `F3` HUD:

```text
TASK-082 selector/research (F3): PASS recipes=9, oneStation=1, initial=4/5, unlocked=9, crafted=1, rp=690, roundTrip=1
```

Ожидаемый `F4` HUD:

```text
TASK-080 industry catalog (F4): PASS recipes=128, chemistry=30, compotium=13, stations=15, tech=32, cycles=0, unreachable=0
```

`F3` прогоняет универсальный station selector и research graph: девять рецептов на одной физической станции, блокировку по технологиям, порядок prerequisites, расход RP, изготовление выбранного рецепта и точный SQLite round-trip прогресса исследований.

`F5` прогоняет все пятнадцать runtime station recipes в отдельной SQLite БД и проверяет timing, isolation, autosave, exact round-trip, one-writer и `integrity=ok`.

После замены файлов поверх собранной рабочей копии необходимо выполнить чистую сборку через `tools\clean-build-windows10.cmd` либо удалить `src\Game.Client\.godot\mono\temp`. В полном build log должен реально выполняться `CoreCompile`.


### Прототип A. Персонаж — `VERIFIED`

Реализованы плоская тестовая сцена, `CharacterBody3D`, камера от первого лица, WASD, гравитация, прыжок, столкновения, взаимодействие по `E` и простая hitscan-стрельба по ЛКМ. Сцена сохранена в:

```text
src/Game.Client/Scenes/DebugWorld.tscn
```

Предыдущие функциональные итерации персонажа, столкновений, взаимодействия и простой стрельбы приняты пользователем как `VERIFIED`. Для окончательной репозиторной фиксации остаётся записать SHA контрольного коммита или тега.

### Прототип B. Чанк рельефа — `VERIFIED`

Прототип B завершён и принят по фактическим runtime-проверкам. Реализованы детерминированный noise-рельеф, сетка `3 × 3`, LOD0/LOD1, согласование кромок, глобальные нормали, отдельная collision-сетка, гистерезис, отменяемая фоновая генерация, дозированное main-thread применение и выгрузка ресурсов.

Короткий stress-test `TASK-025` завершён с результатом:

```text
PASS: rev=13, cancel=0, stale=48, 9/9, queue=0, workers=0, errors=0
```

Длительный soak-test `TASK-026` завершён с результатом:

```text
PASS: 121 s, moves=82, managedDelta=0.0 MB, mesh=9, collision=9
```

После soak-test стриминг вернулся в стабильное состояние: `9/9`, `queue=0`, `workers=0/4`, ошибок фоновой генерации нет. Сцена сохранена для регрессии:

```text
src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn
```

### Прототип C. Сферическая планета — `VERIFIED`

Все обязательные критерии PDF-ТЗ подтверждены локальными runtime-проверками:

- cube sphere и совпадение швов граней;
- гравитация к центру и касательное управление;
- ходьба через независимые collision-грани;
- floating origin;
- quadtree LOD-швы;
- отменяемый async visual streaming `L1/L2/L3`;
- динамический topology-complete collision LOD.

Финальная collision-приёмка:

```text
build: 0 errors, 0 warnings
TASK-038 collision (K): PASS
plans=60, commits=60, created=257, unloaded=233, fallback=60
L3=28, gap=0.00 s, rMin=92.46 m, recoveries=0, errors=0
```

После теста сохранены `ground=да`, `floor=да`, `probe=да`, радиальная система
`PASS`, а циклические провалы и подбрасывания отсутствуют.

Регрессионная сцена планеты сохранена в:

```text
src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn
```

### Диагностический HUD Прототипа C — `VERIFIED`

Панель больше не должна перекрывать весь 3D-холст. По умолчанию используется
компактный HUD размером около `700 × 220 px`.

Клавиша `H` циклически переключает:

1. `COMPACT` — только ключевые visual/collision/player/topology/test показатели;
2. `DETAILED` — вся телеметрия в ограниченной прокручиваемой панели;
3. `HIDDEN` — основная панель скрыта, остаётся небольшой hint `HUD скрыт • H`.

Detailed mode прокручивается колёсиком мыши. Размер обоих видимых режимов
ограничивается текущим viewport, поэтому панель не выходит за границы окна.
Каждое переключение дублируется в Output строкой `Prototype HUD mode: ...`.


### Прототип D. Базовый корабль — `VERIFIED`

Свободный полёт, атмосферный переход, поиск площадки, touchdown/takeoff и
нагрузочный тест 100 последовательных физических посадок приняты runtime.

Финальная soak-приёмка:

```text
TASK-051 soak (V): PASS 100/100
gear=3
vTouch=2,67 м/с
managedDelta=0,02 MiB
nodeDelta=0
build: 0 warnings, 0 errors
```

Регрессионная сцена корабля сохранена в:

```text
src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn
```

### Прототип E. SQLite save, backup, recovery и migration — `VERIFIED`

Регрессионная сцена persistence-прототипа:

```text
src/Game.Client/Scenes/Persistence/SavePrototype.tscn
```

Все обязательные элементы Прототипа E подтверждены локальной runtime-приёмкой:

- SQLite через `Microsoft.Data.Sqlite 8.0.29`, без Entity Framework;
- один slot — одна БД: `user://profiles/profile_prototype/save_1.db`;
- обязательные PRAGMA, последовательная очередь записи и транзакционный snapshot;
- exact round-trip игрока, корабля, inventory и посещённой планеты;
- валидированная предыдущая копия, атомарное recovery, quarantine и журналы;
- copy migration schema `1→2` с byte-identical сохранением исходной БД;
- безопасные alias/placeholder для неизвестного контента;
- регрессионные `C: PASS`, `X: PASS` и `Z: PASS` при сборке `0/0`.

После приёмки всех пяти прототипов начата производственная ступень persistence.
В `TASK-060` реализован autosave/graceful-exit foundation по разделу 22.8 и
критерию 14 PDF-ТЗ:

- периодический autosave каждые `60` секунд после появления игрового snapshot;
- типизированные причины `Landing`, `Takeoff`, `Hyperspace`,
  `QuestCompleted`, `ShipPurchased`, `BaseChanged` и `GracefulExit`;
- входом worker является только неизменяемый `SaveGameSnapshot`; Godot API не
  вызывается из фоновой операции;
- burst событий объединяется в один batch с сохранением самого нового snapshot;
- запись проходит через существующую единственную очередь `SaveDatabase`;
- событие и revision фиксируются в `logs/save_1.autosave.log`;
- запрос закрытия окна перехватывается, последний snapshot записывается и очередь
  полностью flush-ится до вызова `SceneTree.Quit()`;
- `F6` запускает изолированный тест всех восьми trigger types, coalescing,
  graceful-exit flush, exact round-trip и `integrity_check`.

Управление:

```text
S     сохранить snapshot; предыдущая копия защищается автоматически
L     загрузить snapshot
R     очистить slot, сохранив предыдущую копию
B     создать или обновить валидированный backup
Y     восстановить предыдущую копию с quarantine текущей БД
Z     TASK-054 SQLite foundation acceptance
X     TASK-056 backup/recovery acceptance в изолированной БД
C     TASK-058 schema migration / unknown-content acceptance в изолированной БД
F6    TASK-060 autosave / graceful-exit acceptance в изолированной БД
H     compact / detailed / hidden HUD
```

После каждой команды необходимо дождаться завершения текущей операции. Для
проверки реального штатного выхода сначала создайте snapshot клавишей `S`, затем
закройте игровое окно кнопкой закрытия в заголовке или сочетанием `Alt+F4`:
приложение должно завершиться только после строки `graceful-exit autosave PASS`.
Если slot намеренно пуст, выход ждёт активные persistence-операции, но не создаёт
новый snapshot.

## Состояние реализации ТЗ

Актуальный статус требований, доказательства реализации и очередь следующих задач
ведутся в документе:

[`REQUIREMENTS_STATUS.md`](REQUIREMENTS_STATUS.md)

Требование считается завершённым только после получения статуса `VERIFIED`.

## Структура репозитория

```text
ProjectHorizon/
├── src/
│   ├── Game.Client/
│   │   ├── Scenes/
│   │   ├── Scripts/
│   │   ├── Shaders/
│   │   ├── UI/
│   │   ├── Audio/
│   │   ├── project.godot
│   │   └── Game.Client.csproj
│   ├── Game.Domain/
│   ├── Game.Application/
│   ├── Game.WorldGen/
│   ├── Game.Persistence/
│   ├── Game.Networking/
│   ├── Game.Content/
│   └── Game.Tools/
├── server/
│   ├── Universe.Api/
│   └── Universe.Worker/
├── tests/
│   ├── Game.Domain.Tests/
│   ├── Game.WorldGen.Tests/
│   ├── Game.Persistence.Tests/
│   └── Game.IntegrationTests/
├── content/
│   ├── Items/
│   ├── Biomes/
│   ├── Planets/
│   ├── Ships/
│   ├── Species/
│   ├── Quests/
│   └── Localization/
├── art/
│   ├── Source/
│   ├── Models/
│   ├── Textures/
│   ├── Animations/
│   └── Audio/
├── build/
├── docs/
├── .gitattributes
├── .gitignore
└── README.md
```

Часть каталогов будет добавляться по мере перехода к соответствующим этапам разработки.

## Архитектурные принципы

Проект использует многослойную архитектуру:

1. **Presentation Layer** — сцены, камеры, UI и визуальные эффекты.
2. **Application Layer** — игровые сценарии и координация систем.
3. **Domain Layer** — правила мира и чистая игровая логика.
4. **Infrastructure Layer** — базы данных, файлы, сеть и логирование.
5. **Tools Layer** — внутренние редакторы, генераторы и диагностика.

Процедурная генерация, экономика, предметы, задания и состояние мира не должны напрямую зависеть от `Godot.Node`.

`Game.Domain` не должен содержать ссылок на Godot.

## Запуск проекта

### Требования

Перед запуском должны быть установлены:

- Godot Engine 4.7.1 .NET;
- .NET SDK x64;
- JetBrains Rider или другая IDE с поддержкой C#;
- Git;
- Git LFS.

Проверка .NET SDK:

```powershell
dotnet --info
```

### Запуск через Godot

1. Открыть Godot Project Manager и импортировать:

```text
src/Game.Client/project.godot
```

2. Дождаться импорта ресурсов и выполнить сборку C#.
3. Нажать `F5`: стартует `ShipFlightPrototype.tscn` с компактным HUD.
4. Проверить ручной free-flight:
   - W/S — тяга;
   - A/D и Space/C — боковые/вертикальные импульсные двигатели;
   - мышь или стрелки — тангаж/рыскание;
   - Q/E — крен;
   - B — форсаж;
   - X — торможение;
   - G — автоматическая стабилизация;
   - F2 — chase/cockpit camera;
   - R — reset.
5. Нажать `J` и убедиться, что ранее принятый free-flight test остаётся `PASS`.
6. Нажать `P`: корабль перемещается к верхней границе атмосферы. Временный
   radial guidance поддерживает снижение до `blend >= 0,20`, затем отключается;
   повторное `P` возвращает космический spawn.
7. Нажать `L` и убедиться, что принятый atmospheric test остаётся
   `TASK-045 atmosphere (L): PASS`.
8. Нажать `M`: коричневая наклонная площадка и серое препятствие должны быть
   отклонены, зелёная площадка зарезервирована и помечена cyan marker, корабль
   должен перейти в `Aligned` примерно в `12 м` над surface normal. Повторное
   `M` восстанавливает baseline.
9. Нажать `N` и дождаться `TASK-047 landing (N): PASS`.
10. Нажать `O` и убедиться, что `TASK-049 touchdown (O): PASS` виден в HUD.
11. Нажать `V` для soak-теста 100 последовательных посадок; на подтверждённой
    машине ожидаемая продолжительность — около 4–5 минут. Hard timeout рассчитывается
    автоматически; при стандартных параметрах он равен 480 секундам.
12. Клавиша `H` переключает compact, detailed и hidden HUD корабля.
13. Для регрессии Прототипа C открыть
   `Scenes/Planet/CubeSpherePrototype.tscn` через `F6`; compact mode теперь явно
   отключает scrollbar, detailed mode сохраняет прокрутку.
14. Для регрессии Прототипа B открыть
   `Scenes/Terrain/TerrainChunkPrototype.tscn` через `F6`; `F10` запускает
   stress-test, `P` — soak-test.
15. Для повторной проверки Прототипа A открыть `Scenes/DebugWorld.tscn` через `F6`.

### Сборка через командную строку

Из корня репозитория:

```powershell
dotnet build .\src\Game.Client\Game.Client.csproj -c Debug
```

## Первый этап разработки

Разработка начинается с независимых технических прототипов.

### Прототип A. Персонаж

- плоская тестовая сцена;
- управление;
- камера;
- прыжок;
- взаимодействие;
- простая стрельба.

### Прототип B. Чанк рельефа

- noise;
- mesh;
- collision;
- LOD;
- фоновая генерация;
- выгрузка.

### Прототип C. Сферическая планета

- cube sphere;
- гравитация к центру;
- ходьба;
- floating origin;
- устранение швов LOD.

### Прототип D. Корабль — `VERIFIED`

- свободный аркадный полёт — `VERIFIED`;
- тяга, импульсные двигатели, тангаж/рыскание/крен — `VERIFIED`;
- форсаж, торможение, стабилизация и камеры — `VERIFIED`;
- переход `SPACE ↔ ATMOSPHERE` — `VERIFIED`;
- simplified lift, minimum speed, drag и climb limit — `VERIFIED`;
- surface-safety — `VERIFIED`;
- поиск точки, slope/obstacle checks и alignment — `VERIFIED`;
- touchdown, трёхточечные опоры и landed-state — `VERIFIED`;
- контролируемый взлёт и складывание опор — `VERIFIED`;
- soak-test 100 последовательных посадок — `VERIFIED`.

### Прототип E. Сохранение — `VERIFIED`

- SQLite foundation, snapshot и exact round-trip — `VERIFIED`;
- последовательная очередь записи — `VERIFIED`;
- валидированная backup, атомарное recovery и quarantine — `VERIFIED`;
- copy migration schema `1→2` — `VERIFIED`;
- alias/placeholder compatibility для неизвестного контента — `VERIFIED`;
- runtime-приёмка migration/unknown-content и регрессии `C/X/Z` — `VERIFIED`.

Все пять технических прототипов приняты; переход к вертикальному срезу разрешён.
Autosave/graceful-exit foundation следующей производственной ступени имеет статус
`IMPLEMENTED` до локальной приёмки `TASK-061`.

## Правила разработки

- основной язык производственного кода — C#;
- `Nullable` должен быть включён;
- предупреждения компилятора должны устраняться;
- зависимости передаются явно;
- асинхронные операции принимают `CancellationToken`;
- SQL-запросы должны быть параметризованы;
- исключения не подавляются;
- запрещены циклические зависимости проектов;
- Godot Node не используется как доменная модель;
- игровая логика не размещается непосредственно в UI;
- SQL не размещается внутри сцен;
- генерация мира не выполняется непосредственно в `_Process`;
- Godot Signals используются для локального взаимодействия сцены;
- доменные события используются для бизнес-логики.

## Ветки Git

Используемая модель ветвления:

```text
main
develop
feature/*
fix/*
release/*
```

- `main` — стабильное собираемое состояние;
- `develop` — интеграционная ветка разработки;
- `feature/*` — разработка отдельных функций;
- `fix/*` — исправления;
- `release/*` — подготовка выпусков.

Пример создания рабочей ветки:

```powershell
git switch develop
git switch -c feature/player-prototype
```


## Регламент итеративной разработки

Порядок выбора следующей задачи, внесения изменений, проверки, обновления журнала,
подготовки доказательств работоспособности и упаковки архива определён в:

```text
DEVELOPMENT_ITERATION_PROTOCOL.md
```

Краткий запрос для следующей итерации:

```text
Выполни следующую итерацию разработки Project Horizon по регламенту
`DEVELOPMENT_ITERATION_PROTOCOL.md`, PDF-ТЗ и `REQUIREMENTS_STATUS.md`.

Последняя редакция проекта, скачанная с GitHub, приложена к сообщению.
```

Фактические статусы требований и результаты приёмки ведутся только в
`REQUIREMENTS_STATUS.md`.

## Git LFS

Git LFS применяется для крупных бинарных файлов, включая:

- исходные 3D-модели;
- крупные текстуры;
- звуковые файлы;
- видео;
- другие тяжёлые бинарные ресурсы.

Правила LFS хранятся в `.gitattributes`.

## Файлы, не включаемые в Git

В репозиторий не должны попадать:

- `.godot/`;
- `bin/`;
- `obj/`;
- `.idea/`;
- локальные настройки IDE;
- временные сборки;
- локальные базы данных;
- журналы;
- секреты и экспортные учётные данные.

## Лицензия

Лицензия проекта пока не определена.

До выбора лицензии исходный код и материалы проекта считаются закрытыми и не предназначенными для свободного распространения.

## Quality, purity, stability and dismantling

`TASK-093` adds persistent industrial properties to crafted inventory:

- `Quality` is constrained by the recipe quality range;
- `Purity` reflects process conditions, technology tier and hazards;
- `Stability` derives from quality, purity, environment fit and hazards;
- the same recipe and process sequence produce the same values;
- old saves without property metadata load as `100/100/100`.

The PortableFabricator terminal has a fourth `Dismantle` tab. Press `D` from
any terminal tab, or reach it by cycling with `Tab`. The tab lists crafted
items that define `DismantleReturns`, shows `Q/P/S`, recovery efficiency and a
preview of recovered materials. `Enter/E` consumes one item, returns the
quality-scaled materials and requests a `BaseChanged` autosave.

The nine runtime ship-component recipes now define dismantle returns. F1 runs
an additional isolated `TASK-093` acceptance using
`save_1.item-properties-dismantle-test.db`; it checks deterministic property
generation, quality-sensitive partial recovery and exact SQLite round-trip.

## Multi-station refining and Compotium starter line

`TASK-096` expands the playable catalog to sixteen runtime-enabled recipes and
five physical station types. The PortableFabricator keeps the nine one-time
ship-component recipes, while Smelter, Refinery, DistillationColumn and
ChemicalProcessor execute six repeatable refining/chemistry recipes.

All station queues have independent slots and energy, but mirror one shared
player inventory. Intermediate products can move through the chain from
refined ferrite and purified water to Paraffinium lubricant and Compotium
concentrate. The complete queue network is stored in
`save_settings.production_queue_network`; legacy single-queue saves remain
loadable. Gameplay energy recharges to station capacity over sixty active
seconds and does not advance while the game is closed.

`TASK-098` replaces the legacy single-queue HUD diagnostic with a read-only
projection of the complete `ProductionNetworkRuntime`. The HUD aggregates five
physical stations, job states and energy, shows per-station `[R/Q/P]` counters,
and treats an initialized network with zero jobs as available. F1 validates the
projection, transitions, recharge, cold restore, legacy fallback and SQLite
integrity in `save_1.production-network-hud-test.db`.
