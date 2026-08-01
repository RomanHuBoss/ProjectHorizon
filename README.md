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

### Data-driven salvage/repair/crafting vertical slice — `IMPLEMENTED`

Текущая стартовая сцена:

```text
src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn
```

Принятый цикл `salvage → repair` расширен вторым ресурсом, отдельным рецептом
и самостоятельной crafting station. Статические определения находятся в:

```text
src/Game.Client/Content/items.json
src/Game.Client/Content/resources.json
src/Game.Client/Content/recipes.json
```

Каталог строго проверяет schema version, стабильные dotted-ID, неизвестные поля,
дубликаты, диапазоны и межфайловые ссылки. Текущий набор содержит четыре item,
два resource и два recipe definitions:

```text
3 × resource.salvage_alloy
→ 1 × component.starter_hull_patch
→ station.field_repair
→ RepairShip

2 × resource.conductive_crystal
→ 1 × component.ship.launch_capacitor
→ station.portable_fabricator
→ StoreOutputs
```

Игровая последовательность:

1. собрать три голубых salvage-узла;
2. отремонтировать красный стартовый корабль;
3. собрать два фиолетовых conductive-crystal узла;
4. использовать фиолетовый `PortableFabricator`;
5. получить `component.ship.launch_capacitor` и production-autosave;
6. после холодного запуска восстановить собранные узлы, repaired ship,
   crafted component, позицию и revision.

Крафт launch capacitor до ремонта корабля блокируется. Неверная station ID и
недостаточное количество inputs также отклоняются доменной моделью. Оба рецепта
используют один Godot-независимый `StarterRepairSession`; inputs расходуются,
outputs сохраняются как inventory definitions и проходят SQLite round-trip.

Управление стартовой сценой:

```text
WASD / Space   движение и прыжок
E              собрать ресурс / ремонтировать / крафтить
H              detailed / compact / hidden HUD
F7             регрессия TASK-062: salvage → repair
F8             очистить gameplay-slot
F9             регрессия TASK-064: strict JSON/data-driven catalog
F10            TASK-066: второй ресурс, station crafting и persistence
Esc            освободить курсор
```

HUD показывает текущую цель взаимодействия (`aimed at ...` или `near ...`).
Resource nodes обязаны иметь уникальные `ResourceNodeId`; scene startup проверяет,
что оба рецепта обеспечены физическими узлами, а `StationId`/`RecipeId` сцены
совпадают с JSON. Успешный запуск печатает:

```text
TASK-064 content catalog READY: schema=1; items=4; resources=2; recipes=2.
TASK-066 crafting binding PASS: recipe=recipe.ship.launch_capacitor; resource=resource.conductive_crystal; required=2; available=2; station=station.portable_fabricator; items=4; resources=2; recipes=2.
```

Ожидаемые acceptance-результаты:

```text
TASK-062 vertical slice integration acceptance PASS: resources=3; repairBlocked=1; shipRepaired=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok
```

```text
TASK-064 data-driven content acceptance PASS: schema=1; items=4; resources=2; recipes=2; recipe=recipe.ship.starter_repair; required=3; variantRequired=4; blockedBelowVariant=1; repairedAtVariant=1; outputs=1; duplicateRejected=1; missingReferenceRejected=1; stableIds=1
```

```text
TASK-066 crafting expansion acceptance PASS: resources=2; repairPrerequisite=1; wrongStationRejected=1; blockedBeforeResources=1; crafted=1; output=1; questAutosave=1; roundTrip=1; logWritten=1; revision=1; maxWriters=1; integrity=ok
```

`F7`, `F9` и `F10` используют изолированные test-БД и не изменяют gameplay-slot.
`F8` необходим только для чистого ручного прогона.


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
