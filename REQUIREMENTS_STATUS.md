# Project Horizon — журнал реализации требований ТЗ

> **Назначение:** единая точка контроля соответствия проекта техническому заданию.
> **Последняя актуализация:** 2026-08-01
> **Подготовленный снимок:** `ProjectHorizon-main-prototype-e-sqlite-build-hotfix.zip`
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
| E. Сохранение | `IN_PROGRESS` | Реализован SQLite-фундамент: явная миграция, WAL/foreign keys/NORMAL/busy timeout, последовательная очередь записи и транзакционный round-trip игрока, корабля, инвентаря и посещённой планеты |

**Вывод:** `TASK-043`–`TASK-053` подтверждены; Прототип D полностью `VERIFIED`. Повторный soak завершился `V: PASS 100/100` при `gear=3`, `vTouch=2,67 м/с`, `memΔ=0,02 MiB`, `nodesΔ=0` и чистой сборке. Текущая итерация начинает Прототип E задачей `TASK-054`; runtime-приёмка SQLite round-trip назначена как `TASK-055`.

## 3. Результат текущей итерации от 2026-08-01

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

### 8.3. Прототип E — SQLite save foundation

| ID | Требование | Статус | Доказательство / следующее действие |
|---|---|---|---|
| `PE-001` | Отдельная тестовая сцена сохранений | `IMPLEMENTED` | `Scenes/Persistence/SavePrototype.tscn`; назначена стартовой сценой |
| `PE-010` | SQLite через `Microsoft.Data.Sqlite`, один slot — одна БД | `IMPLEMENTED` | PackageReference `8.0.29`; `user://profiles/profile_prototype/save_1.db` |
| `PE-011` | Явные migrations и обязательные PRAGMA | `IMPLEMENTED` | schema migration 1; WAL, foreign keys, synchronous NORMAL, busy timeout 5000 |
| `PE-012` | Последовательная очередь записи вне main thread | `IMPLEMENTED` | Единственный writer gate; SQL выполняется в `Task.Run`; Godot API в worker не используется |
| `PE-013` | Транзакционное сохранение минимального snapshot | `IMPLEMENTED` | player position, ship, inventory и visited planet сохраняются одной transaction |
| `PE-014` | Загрузка и точный round-trip snapshot | `IMPLEMENTED` | Параметризованные SELECT; exact comparison baseline/final snapshot |
| `PE-015` | Диагностика и автоматический save acceptance | `IMPLEMENTED` | `S/L/R`; `Z` проверяет migration, PRAGMA, 8 queued writes, integrity и exact load |
| `PE-ACC-001` | SQLite-редакция собирается 0/0 | `IN_PROGRESS` | Локальная `dotnet build` |
| `PE-ACC-002` | Сцена запускается и создаёт БД по ожидаемому пути | `IN_PROGRESS` | HUD state READY; database path существует |
| `PE-ACC-003` | Migration и PRAGMA подтверждены | `IN_PROGRESS` | schema=1, journal=wal, foreignKeys=1, synchronous=1, busyTimeout=5000 |
| `PE-ACC-004` | Игрок, корабль, inventory и planet проходят exact round-trip | `IN_PROGRESS` | `exactComparisons=2`, revision=2, inventoryRows=3, visitedRows=1 |
| `PE-ACC-005` | Параллельные submissions сериализуются | `IN_PROGRESS` | queuedWrites=8, maxConcurrentWriters=1 |
| `PE-ACC-006` | Integrity check и автоматический тест завершаются PASS | `IN_PROGRESS` | `integrity=ok`; `TASK-054 save (Z): PASS` |

### 8.4. Оставшаяся часть Прототипа E

| Подсистема | Статус |
|---|---|
| Backup и атомарная замена | `NOT_STARTED` |
| Проверка повреждённой основной БД | `NOT_STARTED` |
| Recovery из последней корректной backup | `NOT_STARTED` |
| Миграция старой версии и unknown content | `NOT_STARTED` |

Основная разработка вертикального среза не начинается до приёмки всех пяти прототипов.

## 9. Очередь ближайших задач

Задачи выполняются итеративно; runtime-проверки фиксируются до присвоения `VERIFIED`.

**Зафиксировано как `VERIFIED` по прямому подтверждению пользователя:** `TASK-005`, `TASK-009`, `TASK-011`, `TASK-023`–`TASK-053`; Прототипы A, B, C и D.

| Приоритет | ID | Задача | Результат |
|---:|---|---|---|
| 1 | `TASK-055` | Выполнить runtime-приёмку SQLite foundation | Чистая сборка; `Z: PASS`; schema/PRAGMA/integrity; exact round-trip; max writer concurrency=1 |
| 2 | `TASK-056` | Реализовать backup и recovery | Корректная backup-копия, атомарная замена, corruption detection и восстановление без потери единственной исправной БД |
| 3 | `TASK-006` | Записать SHA контрольного коммита | Журнал содержит Git-доказательство принятой редакции |

**Подтверждено в этой итерации:** `TASK-051`, `TASK-052`, `TASK-053`, `PD-050`–`PD-053`, `PD-ACC-040`–`PD-ACC-045`; Прототип D полностью `VERIFIED`.  
**Реализовано:** `TASK-054`, `PE-001`, `PE-010`–`PE-015`.  
**Текущая приёмочная задача:** `TASK-055`.

## 10. Runtime-приёмка `TASK-054/TASK-055`

1. Выполнить локальную сборку `Game.Client.csproj`. Критерий: `0` ошибок и `0` предупреждений. При первом restore NuGet должен загрузить `Microsoft.Data.Sqlite 8.0.29`.
2. Запустить стартовую сцену. Compact HUD должен показать `DB: Ready`, `schema=1`, `WAL=wal`, `FK=ON` и `TASK-054 save (Z): READY`.
3. Нажать `S`, затем `L`. Snapshot должен загрузиться с revision `1`, inventory `3` и посещённой планетой. Повторный `S` увеличивает revision и изменяет тестовые данные без дублирования inventory rows.
4. Нажать `R`; slot очищается транзакцией. После `L` HUD сообщает, что slot пуст.
5. Нажать `Z` и не использовать управление. Тест обычно занимает менее 5 секунд.
6. Ожидаемый HUD:

```text
TASK-054 save (Z): PASS rev=2, items=3, writes=8,
maxWriters=1, integrity=ok
```

7. Ожидаемая итоговая строка Godot Output:

```text
TASK-054 SQLite save foundation acceptance PASS:
schema=1; journal=wal; foreignKeys=1; synchronous=1;
busyTimeout=5000; integrity=ok; revision=2;
inventoryRows=3; visitedRows=1; queuedWrites=8;
maxConcurrentWriters=1; exactComparisons=2; result=...
```

8. Критерии: чистая сборка; database file создан в `user://profiles/profile_prototype/save_1.db`; schema `1`; WAL; FK `1`; synchronous `1`; busy timeout `5000`; revision `2`; inventory rows `3`; visited rows `1`; восемь submissions; max concurrent writer `1`; exact comparisons `2`; integrity `ok`.
9. В качестве доказательства прислать результат сборки, screenshot `Z: PASS`, полную итоговую строку Output и краткое подтверждение ручных `S/L/R`.
10. При `FAIL` прислать финальный HUD и последние 30 строк Output; дополнительно указать, появился ли файл БД и прошёл ли NuGet restore.

## 11. Журнал проверок

Новые записи добавляются сверху.

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

## 12. Шаблон новой записи

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

## 13. Правило коммита

Каждый функциональный коммит должен содержать обновление этого файла либо явно не изменять статус требований.

Рекомендуемый формат:

```text
<type>: <краткое действие>

Requirements: PA-030, PA-031
Verification: dotnet build; manual interaction smoke test
```

---

## 14. Регламент последующих итераций

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
