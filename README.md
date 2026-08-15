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

Текущий Stage 1 vertical slice и подсистемы through TASK-119 закрыты принятой приёмкой. Текущая mega-итерация TASK-120 добавляет core персонажа: survival pools, exosuit, unified multitool, hazards, sprint/crouch/jetpack/swim и persistent equipment без изменения нормативного Industry Content baseline.

### Подсистемы through TASK-119 — `VERIFIED`; player survival / exosuit / multitool — `IMPLEMENTED`

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
src/Game.Client/Content/station_services.json
src/Game.Client/Content/base_construction.json
src/Game.Client/Content/planetary_pois.json
src/Game.Client/Content/procedural_quests.json
src/Game.Client/Content/player_survival.json
src/Game.Client/Content/ships.json
src/Game.Client/Content/localization.ru.json
src/Game.Client/Content/localization.en.json
src/Game.Client/Content/catalog_manifest.json
```

Редакция содержит шестнадцать runtime-enabled recipes: стартовый ремонт, девять корабельных компонентов PortableFabricator и связную шестирецептурную линию Refining/Chemistry. В сцене работают пять физических типов станций: PortableFabricator, Smelter, Refinery, DistillationColumn и ChemicalProcessor. Каждая станция получает свой список рецептов из JSON, собственную очередь, слоты и энергетический бюджет, но все станции синхронизированы с единым player inventory. Требования `RequiredTechnology` исполняются доменной моделью, исследовательские очки, разблокировки и сеть незавершённых production jobs сохраняются в SQLite. Queue-вкладка показывает progress bar, elapsed/duration, slot status, energy и точные reservations; поддерживает pause/resume и cancellation с полным возвратом inputs, catalysts и energy. Refining/Chemistry recipes являются повторяемыми, их продукты можно использовать как inputs следующих станций. Энергия каждой station автоматически восстанавливается от нуля до capacity за 60 секунд игрового времени. Основной HUD строится непосредственно из `ProductionNetworkRuntime`: агрегирует jobs, состояния и энергию всех пяти станций, показывает постанционную строку `[R/Q/P]` и не считает исправно инициализированную idle network недоступной. Resource layer vertical slice теперь физически покрывает все 42 world-resource definitions: 32 ранее созданных узла сохранены, а для 26 отсутствовавших типов создаётся детерминированное data-driven поле. Всего в сцене доступно 58 узлов; сбор, duplicate protection, расход, зеркала inventory производственной сети, depletion, autosave/cold restore и `F8` reset используют единый generic lifecycle.

Станционные услуги Этапа 1 реализованы отдельным data-driven слоем `station_services.json`. В vertical slice размещён один trader NPC `npc.trader.ilia_voss` с template dialogue и вкладками Dialogue/Buy/Sell/Quests. Каталог задаёт ровно шесть economy types, три factions с relations и три persistent quest graphs. Все 174 items доступны рынку; цена вычисляется из base price, economy, supply/demand, faction, reputation и deterministic daily factor. Credits, reputation, market stock/day и quest state сохраняются в optional SQLite setting `station_services` без повышения schema 2; старые saves используют legacy fallback. Trade синхронизирует основной inventory и все пять production mirrors.

Строительство баз реализовано как отдельная data-driven подсистема `base_construction.json`: 50 модулей покрывают все 16 категорий раздела 20.1 ТЗ — foundations, floors, walls, roofs, corridors, doors, windows, stairs, rooms, generators, batteries, processors, storage, landing pad, terminals и decoration. Дополнительная техническая категория `Structure` содержит несущие балки, арки и колонны, поэтому всего catalog содержит 17 категорий. Модули ставятся на сетку `2,5 м` с cardinal snap, collision rejection, обязательным anchor и проверкой связности при демонтаже. Исполняются ограничения `500/100/200/20`, электрическая сеть представлена графом, учитывает generators, consumers, batteries и enable/disable. Состояние modules, stock, rotation, device state и battery energy сохраняется в optional SQLite setting `base_construction` без повышения schema 2; legacy saves получают пустую базу и полный starter palette. Режим открывается клавишей `G`, а `F6` запускает изолированную `TASK-106` acceptance совместно с legacy coolant regression. Координаты `Player.GlobalPosition` постоянно отображаются в углу HUD во всех режимах `H`.

Корабельные системы vertical slice вынесены в строгий каталог `ships.json`. Он содержит все шесть классов из ТЗ v2.0 §14.2, все одиннадцать class parameters, семь отдельно повреждаемых систем из §14.3 и ровно 18 module definitions, совпадающих с outputs категории `ShipModule` Industry Content v2. Исполняемый starter ship использует универсальный класс; `U` на поверхности открывает loadout manager с вкладками Overview/Modules/Systems. `ShipSystemsRuntime.Commissioned` жёстко синхронизирован с сюжетным `StarterRepairSession.ShipRepaired`: до завершения стартового ремонта семь систем offline, flight/hyperspace readiness равны false, а install/uninstall/damage/repair/refuel запрещены самим domain runtime. Успешный starter repair выполняет единственный commissioning transition, переводит семь систем в исправное состояние и только после этого разрешает эксплуатацию корабля. Установка и снятие модулей расходуют и возвращают предметы через существующий shared inventory API, соблюдают Weapon/Technology slots и изменяют derived stats. Повреждение системы отключает зависящие от неё модули и влияет на flight/hyperspace readiness; ремонт требует catalog-defined ship component, а refuel — `chemical.high_energy_fuel`. Class, commissioned flag, fuel, installations и system health сохраняются в optional SQLite setting `ship_systems` без повышения schema 2; значение fuel одновременно синхронизируется с legacy `ships` row.

`TASK-112` интегрирует эту доменную модель с реальным `ArcadeShipController` и закрывает сквозной критерий Этапа 1: ремонт корабля → посадка в кабину → взлёт → перелёт к физической орбитальной станции → стыковка и открытие уже существующих station services → отстыковка → возврат → посадка → высадка. Ускорение, максимальная скорость и манёвренность контроллера вычисляются из `ShipSystemsRuntime.GetEffectiveStats()`, а взлёт, стыковка, посадка и расход топлива блокируются состоянием commissioning, readiness и соответствующих систем. Voyage location, pilot state, точная поза/скорость, checkpoints, station visit и completed-loop counter сохраняются в optional SQLite setting `stage_one_voyage` без повышения schema 2. `F5` запускает `TASK-076`, `TASK-110` и изолированную `TASK-112` acceptance.

`TASK-114` добавляет следующий целостный subsystem block: procedural galaxy, обязательные system/galaxy maps, route planning и hyperspace. `GalaxyNavigationRuntime` генерирует systems только по запросу из immutable universe seed, `GalaxyId`, integer sector coordinates и double system positions; whole galaxy никогда не помещается в один `Vector3` и не создаётся целиком в памяти. Каждый system имеет deterministic star type, 1–8 planets, archetypes, moons, atmosphere/water flags, economy, danger и planet seeds. `M` открывает Galaxy/System terminal; route planning использует A* по соседним sectors и фактический `HyperdriveRange` установленного ship loadout. Jump разрешён только commissioned/flight-ready кораблю с исправным hyperdrive и активным hyperspace module, только из orbital station; топливо списывается по длине waypoint. Current system, destination, counters и visited systems сохраняются в optional SQLite setting `galaxy_navigation` без повышения schema 2 и согласуются с `visited_planets`. После jump существующие voyage и station-services API переиспользуются в destination system. `F5` запускает отдельную `TASK-114` acceptance, включая 1000 deterministic samples и 100 последовательных hyperjumps.

`TASK-118` закрывает процедурную mission/quest подсистему PDF v2.0 §19 и Stage 2 baseline на 20 заданий. `procedural_quests.json` задаёт баланс всех 15 objective types (`VisitLocation`, `ScanObject`, `ScanSpecies`, `CollectResource`, `CraftItem`, `DeliverItem`, `RepairObject`, `DefeatTarget`, `ProtectTarget`, `BuildModule`, `TradeItem`, `FindSignal`, `ExplorePlanet`, `ExploreSystem`, `ReturnToNpc`). `ProceduralQuestGenerator` строит deterministic 20-quest board из world seed и только из реально доступных capability pools; невозможные combat objectives не выдаются gameplay-board до появления реальных combat/protect targets, но движок и acceptance покрывают оба типа. Каждый generated `QuestDefinition` содержит линейный state graph из `QuestNode`/`QuestCondition`/`QuestAction` и `QuestReward`: objective → optional return-to-giver → claim. Feasibility проверяет существование target, NPC, equipment tier, landing/inventory capability и отсутствие циклов. `Q` на поверхности открывает отдельный mission journal; в Station Services `Q` по-прежнему переключает legacy Quests tab, а в полёте остаётся roll input. Progress подключён к существующим resource/craft/trade/repair/build/POI/ecology/voyage/galaxy events. Rewards зачисляются в реальную station-services economy; faction reputation остальных фракций вычисляется из completed mission state. Сохраняются только delta-state миссий в optional SQLite setting `procedural_quests`, schema остаётся `2`. `F5` включает изолированную `TASK-118` acceptance в `save_1.procedural-quests-test.db`.

`TASK-120` закрывает core персонажа PDF v2.0 §13: Health, Shield, Stamina, LifeSupport, HazardProtection, Temperature/Radiation/Toxic protection, Oxygen, JetpackEnergy и MultitoolEnergy. `player_survival.json` связывает три существующих suit-модуля, три существующих Tool outputs и шесть consumables с runtime, не меняя нормативный baseline 174/42/128/15/32. На поверхности работают sprint, crouch, jetpack и water swimming; environmental archetype текущей планеты расходует hazard/life-support/oxygen с учётом protection. `I` открывает Exosuit & Multitool, `Z` переключает функцию multitool. Scanner/mining/weapon/analyzer/repair используют единый энергетический budget, fauna может наносить реальный shield/health damage. Состояние персонажа и equipment сохраняется в optional `save_settings.player_survival`, schema остаётся 2. Одновременно исправлен repeat-save defect: `procedural_quests` теперь удаляется/перезаписывается вместе с прочими optional settings, а TASK-116/TASK-118 acceptance читает фактический `SaveAutosaveCoordinator.AutosaveLogPath`. `F5` включает изолированную TASK-120 acceptance в `save_1.player-survival-test.db`.

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
Shift          бег (расход Stamina)
Ctrl           присесть; в воде — погружение
Space hold     в воздухе — jetpack; в воде — всплытие
I              Exosuit & Multitool
Z              переключить функцию мультитула
E              собрать ресурс / ремонтировать / открыть station или trader / подтвердить выбор
Q              на поверхности вне UI открыть/закрыть procedural mission journal
Up / Down      выбрать recipe, technology, queue job, market item или quest
Tab            station: Recipes/Research/Queue/Dismantle; services: Dialogue/Buy/Sell/Quests
R              station terminal: переключить Recipes / Research
D              station terminal: открыть Dismantle
B / S / Q      station services: Buy / Sell / Quests
Enter / E      выполнить выбранное station/service действие
Q              station Recipes: поставить recipe в очередь; из других station tabs открыть Queue
C / Delete     отменить выбранный queue job с полным возвратом reservations
Esc            закрыть station UI / освободить курсор
H              detailed / compact / hidden HUD; координаты игрока остаются видимыми
U              на поверхности открыть / закрыть управление системами и модулями корабля
Up / Down      в ship manager выбрать модуль или систему
Tab            в ship manager переключить Overview / Modules / Systems
Enter / E      установить модуль / отремонтировать систему / заправить корабль
X              снять выбранный установленный модуль с возвратом в inventory
D              нанести 25 единиц тестового повреждения выбранной системе
R              отремонтировать выбранную систему одним catalog-defined компонентом
E              у отремонтированного корабля: сесть; на pad/station: disembark/services
Enter          в полёте: выполнить docking или landing по текущей фазе
T              в кабине: взлететь с поверхности или отстыковаться от станции
K              включить / отключить navigation assist к текущей voyage-цели
W / S          тяга вперёд / назад в полёте
A / D          lateral strafe влево / вправо
C / Space      vertical thrust вниз / вверх
Стрелки        pitch вверх/вниз и yaw влево/вправо; мышь — pitch/yaw
Q / E          roll влево / вправо
B / X          boost / braking; G — stabilization
F2             переключить корабельную камеру во время пилотирования
M              открыть system/galaxy map; Tab переключает карты, Up/Down выбирает destination
Enter          в galaxy map: построить route и выполнить следующий hyperspace waypoint
G              открыть / закрыть режим строительства базы
Up / Down      в режиме строительства выбрать модуль
R              в режиме строительства повернуть модуль на 90°
Enter          поставить выбранный модуль в target grid cell
X / Delete     демонтировать targeted module с возвратом stock
T              включить / отключить targeted device
F1             TASK-090/092/093/096/098: queue, properties, multi-station industry и aggregate HUD
F2             TASK-083: chemical process runtime
F3             TASK-082 + TASK-102: research и station services mega-acceptance
F4             TASK-080 + TASK-108: Industry Content v2 и planetary exploration acceptance
F5             TASK-076 + TASK-110 + TASK-112 + TASK-114 + TASK-116 + TASK-118 + TASK-120 mega-acceptance
F6             TASK-106: base construction mega-acceptance + legacy coolant regression
F7             TASK-062 + TASK-100: salvage/repair и полный lifecycle всех 42 ресурсов
F8             очистить gameplay-slot, включая ship systems, voyage и galaxy state
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

Ожидаемый дополнительный `F7` HUD:

```text
TASK-100 resource lifecycle (F7): PASS catalog=42, physical=42, nodes=58, generated=26, collectTypes=42, collectNodes=58, duplicate=1, mirrors=1, depletion=1, restore=1, reset=1, roundTrip=1
```

`F7` одновременно сохраняет прежнюю регрессию `TASK-062` и запускает отдельную БД `save_1.resource-lifecycle-test.db`. `TASK-100` выбирает по одному физическому узлу каждого из 42 типов, проверяет metadata и MaxStack, собирает весь baseline, отклоняет повторный сбор, синхронно расходует часть ресурсов в session и во всех station inventory mirrors, выполняет exact SQLite round-trip, cold restore, database reset, `maxWriters=1` и `integrity=ok`. Gameplay-slot не изменяется.


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

### Stage 1 station services: economy, trader and quests

Синий `StationTrader` расположен на тестовой площадке примерно в точке `x=14, z=12`. Подойдите и нажмите `E`. Dialogue предлагает открыть market, contracts или завершить разговор. Внутри панели:

```text
Up/Down       выбор
Tab           Dialogue / Buy / Sell / Quests
B / S / Q     быстрый переход Buy / Sell / Quests
Enter / E     buy/sell/accept/claim/dialogue action
Esc           закрыть
```

Market покрывает все `174` item definitions. Для выбранного item показываются buy/sell, stock, player inventory и все шесть факторов цены. Buy уменьшает player credits и stock, Sell увеличивает их; обе операции синхронизируют inventory основной session и пяти production queues. Economy day и deterministic daily modifier обновляются после значимого time delta.

Три стартовых contracts:

```text
CollectResource: 2 x resource.ferric_ore        -> 180 credits, +4 reputation
CraftItem:      1 x material.refined_ferrite    -> 260 credits, +6 reputation
TradeItem:      1 x resource.ice_water          -> 220 credits, +5 reputation
```

Quest нужно принять до соответствующего действия. После достижения objective статус становится `ReadyToClaim`; claim выдаёт credits/reputation и сохраняется. Для smoke-test нажмите `F8`, примите все три quests, соберите ferric ore и ice water, изготовьте refined ferrite на Smelter, продайте ice water и claim contracts. После штатного restart credits, reputation, stock и quest states должны восстановиться; `F8` возвращает `2400` credits, `0` reputation, stock `6` и quests `Offered`.

### Base construction subsystem

Нажмите `G`, чтобы открыть builder. Target cell вычисляется по направлению взгляда игрока и округляется к сетке `2,5 м`. Первый модуль обязан быть `module.base_power_node`; каждый последующий модуль должен находиться в одной из четырёх соседних cells. Зелёный preview означает допустимую постановку, красный — collision, отсутствие snap, stock или нарушение limit.

```text
Up / Down    выбрать один из 50 модулей
R            rotation 0/90/180/270
Enter        place
X / Delete   remove с connectivity check и refund
T            enable/disable generator, battery или consumer
G / Esc      close
```

HUD builder показывает target grid/world coordinates, category, stock, power generation/consumption, battery, powered consumers и компактное окно palette. Module nodes имеют mesh, static collision и фактические dynamic lights согласно catalog metadata. Terrain geometry не изменяется.

Ожидаемый `F6` HUD:

```text
TASK-072 legacy fourth path (F6): PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1
TASK-106 base construction (F6): PASS modules=50, placed=50, snap=1, collision=1, power=1, limits=1, stress500=1, restore=1, roundTrip=1
```

`TASK-106` использует отдельную БД `save_1.base-construction-test.db` и проверяет 50 modules / 17 catalog categories (all 16 PDF categories plus Structure), обязательный anchor, grid collision, disconnected placement/removal rejection, connected power graph, battery charge, device toggle, dismantle refund, связный stress graph из 500 modules и отказ на 501-м, отдельный interactive-device limit, exact cold restore, legacy fallback, autosave log, `maxWriters=1` и `integrity=ok`. Gameplay-slot тестом не изменяется.

Ручной smoke-test: нажать `F8`, открыть `G`, поставить anchor, затем несколько соседних structural modules, solar array, battery и consumer; проверить рост generation/consumption и battery; отключить consumer клавишей `T`; попытаться поставить module поверх существующего и отдельно от базы; демонтировать крайний module и убедиться в refund; штатно перезапустить игру и проверить exact restore; `F8` должен вернуть пустую базу и исходный palette.

### Catalog-wide resource lifecycle

При старте `CatalogResourceFieldPlanner` сравнивает `Content/resources.json` с hand-authored узлами сцены. Для каждого отсутствующего типа создаётся один `SalvageResourceNode` со стабильным ID `catalog.<resource>`, детерминированной позицией и материалом из `ResourceVisualDefinition`. Текущая контрольная конфигурация:

```text
catalogResources=42
physicalResourceTypes=42
authoredNodes=32
generatedNodes=26
totalNodes=58
```

Сгенерированное поле расположено на расширенной тестовой площадке в секторе `z=23.0..36.5`. Все узлы используют существующее взаимодействие `E`, deterministic yield, MaxStack validation и одноразовый collection ID. Собранное состояние сохраняется как inventory delta; после cold restart узел остаётся скрытым, а его остаток и production-network mirrors восстанавливаются. `F8` удаляет snapshot и возвращает все 58 узлов. SQLite schema остаётся `2`.

Ручной smoke-test: нажмите `F8`, убедитесь в detailed HUD `types=42/42`, `nodes=58`, `generated=26`; соберите любой узел из generated field; выполните штатный выход и повторный запуск — выбранный узел не должен появиться снова; затем нажмите `F8` и убедитесь, что он снова доступен. Полное покрытие всех 42 типов проверяется автоматически клавишей `F7`.

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
TASK-102 station services (F3): PASS economies=6, factions=3, npc=1, quests=3, tradable=174, price=1, daily=1, trade=1, graph=1, restore=1, roundTrip=1
```

Ожидаемый `F4` HUD:

```text
TASK-080 industry catalog (F4): PASS recipes=128, chemistry=30, compotium=13, stations=15, tech=32, cycles=0, unreachable=0
```

`F3` параллельно прогоняет две изолированные проверки. `TASK-082` сохраняет universal station selector и research graph. `TASK-102` проверяет точный baseline station services `6 economies / 3 factions / 1 NPC / 3 dialogue options / 3 quests / 174 tradable items`, шестимножительную price formula, daily/offline economy, atomic buy/sell, credit conservation, quest graph feasibility, rewards/reputation, cold restore, legacy fallback, one-writer и SQLite integrity. Используется отдельная БД `save_1.station-services-test.db`; gameplay-slot не изменяется.

Ожидаемый `F5` HUD:

```text
TASK-076 runtime matrix (F5): PASS station=15, blocked=15, timed=15, isolated=15, crafted=15, output=20, roundTrip=1
TASK-110 ship systems (F5): PASS classes=6, systems=7, modules=18, coverage=1, slots=1, damage=1, repair=1, commissioning=1, readiness=1, fuel=1, restore=1, roundTrip=1
TASK-112 Stage 1 voyage (F5): PASS derived=1, preRepair=1, takeoff=1, fuel=1, dock=1, station=1, undock=1, landing=1, loop=1, readiness=1, restore=1, roundTrip=1
TASK-114 galaxy navigation (F5): PASS deterministic=1, stars=1, route=1, jump=1, stress100=1, restore=1
TASK-116 ecology (F5): PASS biomes=16, flora=60, fauna=20, deterministic=1, populations=1, discovery=1, restore=1
TASK-118 procedural quests (F5): PASS objectiveTypes=15, generated=20, deterministic=1, feasibility=1, lifecycle=1, gameplayBoard=1, restore=1
TASK-120 player survival (F5): PASS suit=3, multitool=3, consumables=6, environments=8, hazards=1, oxygen=1, movement=1, damage=1, restore=1, repeatedSave=1
```

`F5` прогоняет семь независимых проверок. `TASK-076` сохраняет полную runtime crafting matrix. `TASK-110` проверяет точные counts `6 classes / 7 systems / 18 modules`, module coverage, class stats, блокировку операций до starter repair, commissioning transition, slot limits, derived stats, damage/repair/readiness/fuel lifecycle, cold restore, legacy fallback и exact SQLite round-trip в `save_1.ship-systems-test.db`. `TASK-112` использует отдельную `save_1.stage-one-voyage-test.db`: подтверждает применение effective ship stats к flight profile, запрет посадки в неотремонтированный корабль, расход топлива, docking/station/return/landing lifecycle, disembark, active-flight restore и exact persistence. `TASK-114` использует `save_1.galaxy-navigation-test.db`: проверяет 1000 deterministic systems, GalaxyId/Sector/Double3 hierarchy, все шесть star types, planet bounds, range-aware A*, strict preconditions, fuel debit, visited discovery, cold restore, legacy fallback, exact round-trip и 100 последовательных hyperjumps. `TASK-116` проверяет deterministic ecology baseline и delta-only persistence. `TASK-118` использует `save_1.procedural-quests-test.db` и проверяет все 15 objective types, deterministic 20-offer board, feasibility rejection, active limit, state-graph lifecycle, rewards, current gameplay board, cold restore, legacy fallback, exact round-trip, autosave log, one-writer discipline и SQLite integrity. Gameplay-slot ни одна acceptance не изменяет.

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


## Catalog-wide resource lifecycle closure

`TASK-100` closes the vertical-slice resource subsystem against the fixed v2 baseline. Every one of the 42 world-resource definitions is represented by a physical generic node. Missing scene types are generated deterministically, while existing authored nodes and their stable IDs remain unchanged for save compatibility. Collection, duplicate rejection, available inventory, station mirrors, depletion, cold restore and reset are covered by the isolated F7 acceptance database `save_1.resource-lifecycle-test.db`. No SQLite schema migration is introduced. After `TASK-101` runtime acceptance, further functional iterations may consume the established resource API but should not add separate resource-lifecycle mechanics unless a confirmed defect requires it.

## Stage 1 station-services closure

`TASK-102` adds the complete Stage 1 station-services vertical-slice block: six economy types, three data-driven factions, one physical trader, template dialogue, catalog-wide market pricing, credits and reputation, and three persistent quest graphs. Every one of the 174 catalog items is quotable through the six-factor price formula. Buy/sell operations synchronize the player session and all five production inventory mirrors. The optional `station_services` SQLite setting stores credits, reputation, economy day, stock and quest-node state without increasing schema version 2; legacy saves remain loadable. F3 runs the isolated `save_1.station-services-test.db` acceptance alongside the existing research test. Full galaxy NPC populations, procedural quest generation and inter-system economies remain later-stage features, not unfinished work in this Stage 1 subsystem.



### Base construction closure iteration

`TASK-106` adds a 50-module, 17-category data-driven base-construction runtime matching PDF section 20: cardinal snapping, overlap and disconnection rejection, per-base limits, a graph-based electric network with generators/batteries/consumers and switchable devices, static collisions, dynamic lights, dismantle refunds, autosave/cold restore, legacy fallback and F8 reset. F6 runs the isolated SQLite acceptance in parallel with the existing fourth-path regression. The coordinate overlay is preserved across detailed, compact and hidden HUD modes.

### Planetary exploration and discovery closure

`TASK-108` closes the Stage 1 planetary exploration loop with a strict
`planetary_pois.json` catalog containing exactly 20 POI types, including all
15 types required by PDF section 21. The deterministic planner evaluates
biome, slope, height, water distance, danger, rarity, quest tags, pairwise
spacing and vertical-slice infrastructure clearance. One physical
`StaticBody3D` is generated for every POI type without changing planetary
terrain geometry.

Press `P` to pulse the scanner. A POI must normally be scanned before `E`
can resolve its interaction; scan-only POIs complete during the pulse. Press
`J` to open the persistent discovery catalog, use `Up/Down` to browse and `N`
to assign a deterministic waypoint name to a discovered, nameable object.
Discovery points, discovered/resolved state and custom names are saved in the
optional `save_settings.planetary_exploration` value. SQLite schema remains
version 2 and old saves load with an empty discovery state.

`F4` preserves the complete Industry Content v2 structural acceptance and
also runs `TASK-108` against the isolated database
`save_1.planetary-exploration-test.db`. The command uses an event-silence
gate: every F4 key-down, key-up and repeat packet refreshes the gate. A new
run is permitted only after an actual release packet was the last F4 event,
the previous acceptance has completed and no further F4 event has been
observed for at least 750 ms. A subsequent press or repeat cancels the pending
release. This does not depend on platform-specific physical-key polling and
blocks synthetic release / non-echo repeat sequences while the key is held. One held press therefore
produces exactly one TASK-080/TASK-108 acceptance pair. The test verifies 20 deterministic
placements, environment constraints, symmetric spacing, infrastructure
clearance, quest bias, complete scan/resolve/naming flow, cold restore, legacy
fallback, exact round-trip, one-writer discipline and SQLite integrity.


### Ship systems, loadout and damage closure

`TASK-110` закрывает core-подсистему корабельных классов, модулей и повреждений, требуемую ТЗ v2.0 §14.2–14.3. `ships.json` содержит шесть class profiles с Hull, Shield, CargoCapacity, FuelCapacity, Acceleration, MaxSpeed, Maneuverability, WeaponSlots, TechnologySlots, HyperdriveRange и AtmosphericEfficiency; семь system definitions; восемнадцать module definitions, полностью совпадающих с category `ShipModule` outputs.

После ремонта starter ship нажмите `U` на поверхности. В Modules установка по `Enter/E` атомарно потребляет один предмет из shared inventory; `X` снимает модуль и возвращает его. В Systems клавиша `D` наносит контролируемое тестовое повреждение, `R` расходует заданный системой repair component. Overview позволяет заправить корабль высокоэнергетическим топливом. Повреждённые engine/impulse/landing/hull блокируют flight readiness, повреждённый hyperdrive или его module — hyperspace readiness, а affected modules перестают давать stat bonuses до ремонта.

Snapshot хранит class ID, commissioned flag, fuel, slot installations и exact system health в `save_settings.ship_systems`; `ships.fuel` синхронизируется для совместимости. Старые saves без блока получают состояние, согласованное с сюжетным starter repair. Покупка и смена класса корабля остаются отдельной будущей функцией.

### Stage 1 repair-to-station voyage closure

`TASK-112` соединяет ранее изолированные vertical-slice системы в обязательный сквозной цикл Этапа 1. После `StarterRepairQuestCompleted` повторное `E` у корабля передаёт управление встроенному экземпляру `ArcadeShip.tscn`. Контроллер получает acceleration, max speed и angular response из текущих effective ship stats; модульные бонусы и повреждения поэтому влияют не только на интерфейс, но и на фактический полёт.

Основной маршрут:

```text
repair ship → E board → T takeoff → fly/navigation assist to orbital dock
→ Enter dock → E open station services → T undock
→ return to planet approach → Enter land → E disembark
```

Физическая орбитальная станция, docking marker и planet approach marker находятся в той же vertical-slice сцене. `K` включает deterministic navigation assist к текущей цели, но не телепортирует корабль и использует тот же controller input path. Docking требует допустимой дистанции и скорости; landing дополнительно требует исправной Landing system. Каждая фазовая операция расходует fuel и отклоняется при недостаточном запасе. На станции повторно используется уже принятая панель `STATION SERVICES` с торговлей и заданиями.

`save_settings.stage_one_voyage` хранит location, piloted flag, station visit, counters, exact ship pose/velocity и checkpoint. `ships` row получает ту же позицию для cross-table validation. Cold restore возвращает игрока в кабину и в точную фазу полёта без offline progress; legacy saves получают surface/not-piloted state. `F8` очищает voyage вместе с остальными gameplay-данными. SQLite schema остаётся `2`.

После runtime-приёмки `TASK-112` изменять этот контур следует только при интеграции полноценной планетарно-космической смены сцен, межсистемных перелётов или новых типов станций; повторно реализовывать boarding, readiness/fuel gates, docking/landing lifecycle и persistence не требуется.

### Procedural galaxy, maps and hyperspace (`TASK-114`)

После полного Stage 1 loop приобрести и установить `module.ship.hyperspace_core` либо `module.ship.compotium_drive_core`, состыковаться с orbital station и нажать `M`. Galaxy tab показывает nearby systems, sector coordinates, star type, прямую distance, количество waypoint jumps и `VISITED/NEW`; System tab показывает все planets текущей системы. `Up/Down` меняет selection, `Enter` строит route и выполняет следующий waypoint. Jump отклоняется на поверхности, в полёте, без commissioning, при `flightReady=0`, с повреждённым hyperdrive, без активного hyperspace module, при недостатке fuel или отсутствии range-aware route. После успешного jump ship остаётся piloted и docked у station checkpoint новой системы; station services доступны без нового economy runtime. Штатное завершение и cold restore обязаны сохранять exact system/sector/destination/jump/distance/visited state. `F8` возвращает `galaxy.g1/system.vertical_slice`, `visited=1`, `jumps=0`.

## Procedural planetary ecology closure

`TASK-116` adds the Stage 2-ready planetary ecology core required by PDF v2.0
sections 11–12 and the Stage 2 content baseline. `Content/ecology.json` defines
16 biomes, 60 flora modules and 20 fauna archetypes split into 12 terrestrial,
4 flying and 4 aquatic species. All six required fauna body plans are covered.
The runtime regenerates populations deterministically from `WorldSeed` and
`RegionKey` instead of serializing every plant or animal.

Repeated vegetation is rendered by `MultiMeshInstance3D` groups. Only nearby
flora specimens are promoted to interactive `StaticBody3D` nodes for scan,
harvest and damage interaction. Fauna is capped at 20 fully active local
`CharacterBody3D` agents plus 80 statistical/simplified population entries.
Nearby AI evaluates at 10 Hz, medium-range AI at 4 Hz and distant fauna remains
statistical. The utility/steering runtime covers Idle, Wander, Graze, Drink,
Sleep, Investigate, Flee, Threaten, Attack, ReturnToTerritory and FollowGroup.

On foot:

```text
V          scan the nearest flora/fauna signal within 16 m
O          open/close the ecology catalogue
Tab        switch Flora/Fauna inside the catalogue
Up/Down    browse discovered species
E          harvest an interactable promoted flora specimen
```

Harvesting yields `resource.flora_pulp`. Discovery species IDs and removed flora
instance IDs are persisted in the optional `save_settings.ecology` value. No
procedural fauna instance pose/state is stored. SQLite schema remains version 2;
legacy saves regenerate ecology from the catalog seed with empty discovery and
harvest deltas.

`F5` now runs `TASK-116` in the isolated
`save_1.ecology-test.db` alongside the existing runtime/ship/voyage/galaxy
acceptance. The ecology test checks the 16/60/20 baseline, 12/4/4 movement
coverage, six body plans, all eleven behavior states, deterministic placement,
MultiMesh-oriented flora population, 20/80 population limits, update tiers,
utility behavior, discovery/harvest lifecycle, delta-only persistence, all 16
biomes, cold restore, legacy fallback, exact SQLite round-trip, one-writer
discipline and integrity.

The integrated `VoyageShip` now has a concrete `Gameplay/AtmospherePlanet` target for its default `../AtmospherePlanet` reference. The product owner explicitly waived the remaining ecology/runtime acceptance, so disappearance of the prior `Arcade ship has no atmosphere reference` warning is not claimed as independently verified in this prepared snapshot.


## Procedural mission system closure

`TASK-118` implements the repeatable mission system required by PDF v2.0 §19 without replacing hand-authored story content. The generated board contains exactly 20 deterministic offers and supports all 15 objective types in the domain model. Gameplay generation uses capability-gated feasibility: objectives are built only from resources, runtime-enabled craft outputs, attainable resource/craft items, base modules, real POIs/species, reachable first planets of nearby systems and existing NPCs. Combat/protection objectives remain engine-supported but are withheld from the current gameplay board until physical hostile/protected targets exist.

On foot outside other UI surfaces:

```text
Q          open/close procedural mission journal
Up/Down    select mission
Enter      accept / deliver / return / claim
Esc        close
```

The mission graph is `Objective -> Return (when required) -> Claim`. Accepting, progressing, returning and claiming are persistent. Rewards grant credits through the existing Station Services economy; completed-state faction reputation remains deterministic and restorable. Mission progress is hooked to resource collection, runtime crafting/production, trade, starter/system repair, base construction, POI scan/resolve, ecology scan/harvest, planetary landing and hyperspace system exploration. `DeliverItem` consumes the exact shared inventory quantity at the giver. `ReturnToNpc` and return-required nodes advance only at the real trader/orbital service checkpoint.

`save_settings.procedural_quests` stores only non-default board deltas (status/progress) plus seed/revision; the 20 definitions are regenerated from content and seed on load. SQLite schema remains 2 and legacy saves receive a fresh zero-progress board. `F8` resets the board. `F5` uses `save_1.procedural-quests-test.db` and validates exact 15-type support, deterministic generation, feasibility rejection, active limit, full state-graph lifecycle, reward integrity, a playable combat-free current board, cold restore, legacy fallback, round-trip, autosave log, one-writer discipline and SQLite integrity.
