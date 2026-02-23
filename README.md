# WormCrawlerPrototype

Прототип 2D-песочницы в духе **Worms**: уровень из PNG-карты, полигональная физика земли, крюк-кошка с верёвкой (DistanceJoint2D) и камерой с режимом free-look.

Проект сделан так, чтобы **визуал** и **физика** были разделены:

- Визуал: спрайт из PNG-текстуры террейна.
- Физика: **только** `EdgeCollider2D` у объекта `GroundPoly`.

---

## 0) Технические требования

### 0.1 Unity / Editor

- Unity: **6000.3.5f1** (см. `ProjectSettings/ProjectVersion.txt`).

### 0.2 Основные пакеты (Packages/manifest.json)

- `com.unity.inputsystem` — ввод (клавиатура/мышь/тач).
- `com.unity.render-pipelines.universal` — URP (рендер).
- `com.unity.2d.*` — 2D Tooling (tilemap/spriteshape/animation).

### 0.3 Платформы / режимы

- Desktop: управление клавиатура/мышь.
- Mobile: отрисовка touch UI и управление через экранные кнопки (см. `Bootstrap`).
  - Экранные кнопки (стрелки, огонь) масштабируются под разрешение.
  - Прицел при нажатии вверх/вниз фиксируется на заданном угле и не сбрасывается при отпускании кнопки.
  - Долгий тап на кнопку огня поддерживает burst-fire (очередь ClawGun) через прямое отслеживание касания.

---

## 0.4 Как ориентироваться в проекте (для форка)

- **Точка входа/меню/Touch UI**: `Assets/Scripts/Bootstrap.cs`
- **Смена ходов, ограничения оружия, команды**: `Assets/Scripts/TurnManager.cs`
- **Игрок/здоровье/идентификация команд**:
  - `Assets/Scripts/SimpleHero.cs`
  - `Assets/Scripts/SimpleHealth.cs`
  - `Assets/Scripts/PlayerIdentity.cs`
- **Оружие/механики**:
  - Rope: `Assets/Scripts/GrappleController.cs`
  - Grenade: `Assets/Scripts/HeroGrenadeThrower.cs`, `Assets/Scripts/GrenadeProjectile.cs`
  - ClawGun: `Assets/Scripts/HeroClawGun.cs`
  - Teleport: `Assets/Scripts/HeroTeleport.cs`
  - Переключение оружия + HUD: `Assets/Scripts/HeroAmmoCarousel.cs`
- **AI**: `Assets/Scripts/AI/SpiderBotController.cs`
- **Мир/разрушаемость/генерация**: `Assets/Scripts/SimpleWorldGenerator.cs`

---

## 0.5 Карта кода (классы и основные методы)

Ниже краткая «карта проекта» по скриптам из `Assets/Scripts`, чтобы по README можно было быстро понять, куда смотреть при добавлении механик.

### 0.5.1 Bootstrap / UI / Меню

- **`Bootstrap`** (`Assets/Scripts/Bootstrap.cs`)
  - **Роль**: создание/инициализация мира и меню, настройки режима (VsCpu, сложность, размер команд), отрисовка touch UI.
  - **Ключевые поля/свойства**:
    - `Bootstrap.IsMapMenuOpen` — стоп-флаг для игрового ввода, когда открыто меню.
    - `Bootstrap.SelectedTeamSize`, `Bootstrap.VsCpu`, `Bootstrap.CpuDifficulty`.
  - **Ключевые входы**:
    - генерация мира через `SimpleWorldGenerator`;
    - отрисовка и обработка кнопок меню/тач-управления.

### 0.5.2 Пошаговый режим / правила хода

- **`TurnManager`** (`Assets/Scripts/TurnManager.cs`)
  - **Роль**: выбор активного героя, строгая альтернация команд, ограничения оружия (locked/shotUsed/ropeOnly), таймер хода, damage-reaction.
  - **Ключевые типы**:
    - `TurnWeapon` (`None`, `Grenade`, `ClawGun`, `Teleport`).
  - **Ключевые свойства**:
    - `ActivePlayer` — текущий герой.
    - `LockedWeaponThisTurn`, `ShotUsedThisTurn`, `RopeOnlyThisTurn` — состояние ограничений хода.
  - **Ключевые методы (public API)**:
    - `CanSelectWeapon(TurnWeapon weapon)` — можно ли выбрать оружие сейчас.
    - `TryConsumeShot(TurnWeapon weapon)` — отмечает действие как совершённое (clamp таймера, lock оружия).
    - `NotifyWeaponSelected(TurnWeapon weapon)` — фиксация выбора оружия (lock).
    - `NotifyClawGunReleased()` — перевод хода в rope-only в конце удержания очереди.
    - `EndTurnAfterAttack()` — завершить ход после атаки.
  - **Инварианты/правила**:
    - После действия (выстрел/урон) оставшееся время может быть зажато до `postActionClampSeconds`.
    - Смена активного героя включает/выключает ввод и может переключать оружие на Rope.

### 0.5.3 Идентификация игроков и здоровье

- **`PlayerIdentity`** (`Assets/Scripts/PlayerIdentity.cs`)
  - **Роль**: хранит `TeamIndex`, `PlayerIndex`, имя игрока — используется в очереди ходов и логах.

- **`SimpleHealth`** (`Assets/Scripts/SimpleHealth.cs`)
  - **Роль**: HP/урон/события.
  - **Ключевое**: событие `SimpleHealth.Damaged` используется `TurnManager` для damage reaction.

- **`SimpleHero`** (`Assets/Scripts/SimpleHero.cs`)
  - **Роль**: «сборка героя»/поведение героя в пошаговом режиме (движение, связка компонентов оружия/прицела).

### 0.5.4 Прицел и движение

- **`WormAimController`** (`Assets/Scripts/WormAimController.cs`)
  - **Роль**: управление направлением прицела (для Rope/Grenade/ClawGun и т.д.).

- **`HeroSurfaceWalker`** (`Assets/Scripts/HeroSurfaceWalker.cs`)
  - **Роль**: движение по поверхности, удержание на склонах, физическая стабилизация.

### 0.5.5 Оружие и предметы

- **`HeroAmmoCarousel`** (`Assets/Scripts/HeroAmmoCarousel.cs`)
  - **Роль**: выбор активного «слота» (Rope/Grenade/ClawGun/Teleport), HUD-иконки, обработка горячих клавиш `1..4`.
  - **Интеграция с TurnManager**: при выборе оружия проверяет `TurnManager.CanSelectWeapon()`.
  - **Примечание**: Rope остаётся базовым безопасным слотом.

- **`GrappleController`** (`Assets/Scripts/GrappleController.cs`)
  - **Роль**: крюк-кошка/верёвка — выстрел, attach/detach, свинг, подтягивание, wrap вокруг углов.
  - **Ключевой метод**: `FireRope(Vector2 dir)`.

- **`HeroGrenadeThrower`** (`Assets/Scripts/HeroGrenadeThrower.cs`)
  - **Роль**: бросок гранаты по траектории.

- **`GrenadeProjectile`** (`Assets/Scripts/GrenadeProjectile.cs`)
  - **Роль**: полёт гранаты, столкновения/взрыв.

- **`ExplosionController`** (`Assets/Scripts/ExplosionController.cs`)
  - **Роль**: применение урона/импульса/взаимодействия с миром при взрывах.

- **`GrenadeExplosionFx`** (`Assets/Scripts/GrenadeExplosionFx.cs`)
  - **Роль**: визуальный FX взрыва (spritesheet из `Resources`).

- **`HeroClawGun`** (`Assets/Scripts/HeroClawGun.cs`)
  - **Роль**: hitscan-оружие с очередью при удержании, raycast impact, урон/FX/кратер.
  - **Ключевые методы**:
    - `TryFireOnceNow()` — попытка сделать один выстрел (с учётом `TurnManager.CanSelectWeapon`).
    - `SetExternalHeld(bool held)` — удержание для UI/AI (используется для burst-fire на мобильных).
  - **Баланс/параметры**:
    - `shotsPerSecond` (обычно 2/s), `maxShots/shotsLeft` (обычно 25), дальность масштабируется от гранаты.
    - `bulletExplosionRadiusHeroHeights` = 0.575 (диаметр кратера = 1.15 высоты героя).
  - **Позиционирование**: управляется полями `weaponOffsetFractionOfHeroSize`, `weaponSpritePivotPixels`, `weaponAimAngleOffsetDeg` и т.д. непосредственно на компоненте (без глобальных настроек в Bootstrap).
  - **Линия прицела**: по умолчанию используется `AimLineVisual2D` — анимированная пунктирная линия (бегущие штрихи cyan/white) с пульсирующим снайперским прицелом на конце. Стандартное перекрестье `WormAimController` скрывается при выборе ClawGun.

- **`AimLineVisual2D`** (`Assets/Scripts/AimLineVisual2D.cs`)
  - **Роль**: визуал линии прицела — анимированные бегущие штрихи (LineRenderer с тайловой текстурой + UV scroll) и снайперский scope-прицел (процедурный спрайт с пульсацией масштаба) на конце луча.
  - **Создаётся автоматически** компонентом `HeroClawGun` при `useFancyAimLine = true`.

- **`HeroTeleport`** (`Assets/Scripts/HeroTeleport.cs`)
  - **Роль**: телепорт героя в выбранную точку карты (предмет Teleport).

### 0.5.6 AI

- **`SpiderBotController`** (`Assets/Scripts/AI/SpiderBotController.cs`)
  - **Роль**: логика поведения ботов команды Spider: выбор оружия, прицеливание, удержание огня.
  - **Интеграция**:
    - запускается в начале хода активного бота через `TurnManager.ApplyActiveState()`.

### 0.5.7 Мир / генерация / разрушаемость

- **`SimpleWorldGenerator`** (`Assets/Scripts/SimpleWorldGenerator.cs`)
  - **Роль**: загрузка мира из PNG-текстуры, построение `EdgeCollider2D` по контурам, карвинг кратеров в рантайме.
  - **Ключевые методы**:
    - `Generate(int seed)` — загружает PNG, строит коллайдеры, спавнит героя.
    - `CarveCraterWorld(Vector2 center, float radius)` — вырезает кратер в террейне.
    - `ConfigurePngTerrain(string path)` — задаёт путь к PNG-ресурсу.

- **`WorldDecoration`** (`Assets/Scripts/WorldDecoration.cs`)
  - **Роль**: компонент-маркер на префабах декораций (размер, допустимый наклон, вертикальный offset). Используется при размещении entity из PNG entities-карты.

---

## 1) Быстрый старт

- Открой сцену и нажми Play.
- `GameManager` создаётся автоматически через `Bootstrap`.
- Уровень загружается из PNG-текстуры при старте.

---

## 2) Управление (Keyboard + Gamepad)

### 2.1 Персонаж

- **A / LeftArrow**: движение влево.
- **D / RightArrow**: движение вправо.

Важно:

- Когда персонаж **на верёвке**, обычное движение по земле отключается (движение задаётся верёвкой/свингом).

### 2.2 Прицеливание (курсор-указатель)

Прицел задаётся углом относительно направления взгляда.

- **W / UpArrow**: поднять угол прицела.
- **S / DownArrow**: опустить угол прицела.

Особенность:

- Пока персонаж **на верёвке**, угол прицела не меняется (чтобы управление свингом не конфликтовало с прицелом).

### 2.3 Крюк-кошка / Верёвка

- **Space** (Gamepad: **South / A**):
  - если верёвки нет и не идёт выстрел — **начать выстрел**;
  - если идёт выстрел — **отменить выстрел**;
  - если верёвка уже закреплена — **отцепиться**.

- **W / UpArrow**: подтягивание (уменьшение дистанции joint).
- **S / DownArrow**: отпускание (увеличение дистанции joint).

- **A/D** (или left stick по X на геймпаде): свинг/раскачка (силой вдоль касательной к радиусу верёвки).

### 2.4 Камера (Free Look)

- **Tab** (Gamepad: **Select**): включить/выключить free-look.

В режиме free-look:

- **WASD / стрелки**: панорамирование камеры.
- **Mouse wheel**: зум (orthographic size).

Камера ограничивается границами мира (`GameManager.WorldBounds`) с учётом текущего зума.

---

## 3) Основной игровой цикл

### 3.1 Генерация уровня

`GameManager.Generate()`:

- Инициализирует/переинициализирует `TerrainTilemapView` (`CreateOrGet()` вызывается каждый раз).
- Пытается сгенерировать валидный уровень до **20 попыток**, увеличивая seed (`seed + attempt`).
- Спавнит:
  - героя (`Hero`),
  - выход (`Exit`),
  - несколько `Sentry_*` (плейсхолдеры).

### 3.2 Рестарт

Рестарт происходит в случаях:

- Герой вышел за границы уровня (bounds + margin).
- Герой получил смертельный удар/падение.
- Герой вошёл в триггер выхода (`ExitTrigger`).

Рестарт — это повторная генерация уровня (`GameManager.Restart()` → `Generate()`).

---

## 4) Физическая модель уровня: тайлы + полигон

### 4.1 Почему так сделано

Изначально тайлы могли:

- мешать верёвке (цеплялась за невидимые/непредназначенные коллайдеры),
- давать «невидимые блоки»,
- ломать согласованность визуала и коллизии.

Решение:

- Тайлы остаются **только визуальными**.
- Физика — строго через `PolygonCollider2D`.

### 4.2 `TerrainTilemapView` — соглашения

`CreateOrGet()` делает следующее:

- Гарантирует наличие `World` и `Grid`.
- Создаёт/находит tilemap-объекты:
  - `Background` (видимый),
  - `Foreground` (видимый),
  - `Collision` (невидимый, используется как служебный слой/экстра-объекты).

Слои и рендер:

- `Background` / `Foreground`:
  - активны,
  - слой `Default` (0),
  - `TilemapRenderer.enabled = true`.

- `Collision`:
  - активен,
  - слой `Ignore Raycast` (2),
  - `TilemapRenderer.enabled = false`.

Коллайдеры tilemap:

- `TilemapCollider2D` и `CompositeCollider2D` у `Foreground` и `Collision` **отключены**.
- `Rigidbody2D.simulated = false` на тайлмапах (если они есть).

Физические коллайдеры:

- Земля: объект `GroundPoly` с `PolygonCollider2D`.
- Острова/полки: корневой объект `Islands` (набор отдельных коллайдеров на каждую фигуру).

---

## 5) Генерация мира из PNG

Мир загружается из PNG-текстуры (`Resources/Levels/terrain`).

### 5.1 Загрузка террейна

- PNG-текстура интерпретируется попиксельно: непрозрачные пиксели → solid, прозрачные → воздух.
- Порог прозрачности настраивается через `pngAlphaSolidThreshold`.
- Размер мира определяется размером PNG и `pngPixelsPerUnit`.

### 5.2 Построение коллайдеров

- Из bitmap solid-маски строятся контурные петли (`BuildBoundaryLoops`).
- Петли упрощаются (`SimplifyLoop`) и сглаживаются (`ApplyChaikinSmoothing`).
- Каждая петля становится `EdgeCollider2D` на объекте `GroundPoly`.

### 5.3 Entities из PNG

- Опциональная вторая PNG (`Resources/Levels/entities`) задаёт позиции объектов по цвету пикселей.
- Цвет `pngHeroColor` (по умолчанию magenta) задаёт точку спавна героя.
- Другие цвета могут быть привязаны к `WorldDecoration` префабам через `pngEntitySpawns`.

### 5.4 Карвинг кратеров (runtime)

- `CarveCraterWorld(center, radius)` вырезает круг из solid-маски.
- Обновляет текстуру и перестраивает `EdgeCollider2D` в реальном времени.

---

## 6) Герой: движение, удержание на склонах

### 6.1 Обычное движение

`WormHeroController`:

- В `Update()` считывает A/D.
- Если **не** на верёвке:
  - плавно ведёт `v.x` к `target = h * moveSpeed` через `Mathf.MoveTowards`.

### 6.2 Удержание на наклонных полигонах (slope hold)

Проблема: на полигональной поверхности герой мог «сползать» даже без ввода.

Решение: в `FixedUpdate()` при условиях:

- герой **не** на верёвке,
- ввода по X нет,

делается raycast вниз, берётся нормаль поверхности и:

- убирается скорость вдоль касательной (срез тангенциальной компоненты скорости),
- компенсируется тангенциальная составляющая гравитации (AddForce против «скатывания»).

Порог «пригодной поверхности» сейчас мягкий: `normal.y >= 0.05`.

---

## 7) Крюк-кошка / Верёвка (GrappleController)

Верёвка реализована через:

- `DistanceJoint2D` (без auto-config),
- `LineRenderer` (визуализация),
- RaycastAll для поиска точек цепляния и обмотки.

### 7.1 Выстрел верёвки (анимация)

При нажатии Space начинается выстрел:

- выбирается направление `AimDirection` (из `WormAimController`),
- делается raycast до `maxDistance`,
- линия рисуется от героя к точке выстрела с параметром времени `t`.

Если попадание было:

- после долёта (`t >= 1`) выполняется `FinishAttachFromShot()`.

Если промах:

- линия «возвращается» за `missRetractTime`.

### 7.2 Точка закрепления (anchor)

- Якорь хранится как `_anchorWorld`.
- Якорь сдвигается вдоль нормали поверхности на `anchorSurfaceOffset`, чтобы:
  - не было залипания в коллайдер,
  - визуально и физически точка была чуть снаружи поверхности.

### 7.3 Свинг (раскачка)

В `FixedUpdate()` при активной верёвке:

- считается `radial` (направление от anchor к телу) и `tangent`.
- Если есть ввод по горизонтали:
  - добавляется сила `tangentRight * (hInput * swingForce)`.
- Если ввода нет:
  - включается демпфирование по касательной (`noInputRopeDamping`), чтобы верёвка не «вечно качалась».

### 7.4 Подтягивание/отпускание (изменение длины)

Ввод по вертикали меняет `DistanceJoint2D.distance`:

- `W` уменьшает,
- `S` увеличивает,

с зажимом:

- `minDistance .. maxDistance`.

### 7.5 Обмотка верёвки вокруг углов (pivot / wrap)

Чтобы верёвка могла огибать выступы, введены pivot-точки (`_pivots`).

- `GetSwingAnchor()` возвращает:
  - последний pivot, если он есть,
  - иначе `_anchorWorld`.

При каждом `FixedUpdate()` выполняется `UpdateRopeWrap()`:

- Делается проверка лучом от героя к текущему swing anchor.
- Если есть препятствие — создаётся/обновляется pivot на точке удара + offset по нормали.

Размотка:

- Если сегмент к «внешней» точке (предыдущий pivot или anchor) становится чистым, pivot снимается.

### 7.6 Ключевой инвариант длины (фикс “бесконечной верёвки”)

Внутри механики есть две длины:

- `_totalRopeLength` — **общая длина** (якорь → … pivot-цепочка … → герой).
- `_joint.distance` — **последний сегмент** (от swing anchor до героя).

Важно:

- При создании pivot длина **не сбрасывается**.
- `_totalRopeLength` фиксируется при attach и изменяется только при подтягивании/отпускании.

Пересчёт:

- `_pathLengthToSwing = ComputePathLengthToSwing()` (anchor → pivots).
- `remaining = _totalRopeLength - _pathLengthToSwing`.
- `remaining` ограничивается снизу `minSegment = 0.05f` и сверху `maxDistance`.

Дополнительная защита:

- pivot не принимается, если путь `anchor → pivot` уже «съедает» почти всю длину (`candidatePath >= _totalRopeLength - minSegment`).

### 7.7 Фильтрация коллайдеров (важно для отсутствия «невидимых блоков»)

Все raycast’ы в `GrappleController` фильтруют хиты так, чтобы верёвка взаимодействовала **только** с нужной геометрией.

Игнорируются:

- любые `TilemapCollider2D` и вообще любые коллайдеры, принадлежащие `Tilemap` (`IsTilemapCollider`).
- любые коллайдеры, которые не находятся в иерархии объектов с именем `GroundPoly` или `Islands` (`IsTerrainCollider`).
- триггеры.
- коллайдеры самого героя.

Таким образом:

- тайлы остаются полностью «визуальными»,
- верёвка цепляется и обматывается только о физические полигоны.

---

## 8) Падение, смертельные условия, отскок (WormFailureController)

### 8.1 Границы уровня

Если позиция героя выходит за `WorldBounds` с `boundsMargin`:

- происходит `GameManager.Restart()`.

### 8.2 Падение и смертельный урон

Трекинг падения:

- Когда герой не grounded → `_falling = true`, запоминается `_fallStartY`.
- Пока падает:
  - если герой в момент падения был на верёвке, считается что он «спасён верёвкой» (`_savedByRope = true`).

Смерть:

- если герой не был спасён верёвкой и одновременно:
  - падение больше `killFallDistance`,
  - скорость удара больше `killImpactSpeed`,

то происходит рестарт.

### 8.3 Отскок (bounce) от поверхности

При столкновении:

- выбирается контакт с максимальным «входом в поверхность» (`bestInto = -dot(relativeVelocity, normal)`).
- рассчитывается сила отскока `t` как максимум из:
  - доли падения (`tFall`),
  - доли удара (`tImpact`).

Если `t` достаточно большой (сейчас порог `0.02`):

- вычисляется `bounceHeight` в диапазоне `bounceMinHeight..bounceMaxHeight`.
- пересчитывается скорость: удаляется компонент «внутрь нормали» и добавляется скорость по нормали вверх.

Особенность: отскок работает не только при «чистом падении», но и при ударах о стены/склоны.

### 8.4 Взаимодействие bounce с верёвкой (ускорение свинга)

Если герой в момент bounce на верёвке:

- вычисляется касательная к верёвке.
- Если игрок **не** держит A/D:
  - тангенциальная скорость убирается (bounce «гасится» относительно свинга).
- Если игрок держит A или D:
  - добавляется дополнительная скорость по касательной, усиливая раскачку.
  - есть счётчик `_ropeBounceCount`, который усиливает эффект при повторных bounce.

---

## 9) AP (Action Points)

Есть компонент `ActionPoints`, который хранит `max/current` и рисует простую GUI-панель.

На текущий момент:

- `GrappleController` поддерживает стоимость зацепа (`apCostAttach`),
- но по умолчанию `apCostAttach = 0`, то есть механика AP включена инфраструктурно, но фактически не ограничивает верёвку.

---

## 10) Иерархия объектов в рантайме (важные имена)

- `GameManager` (создаётся через `Bootstrap`, `DontDestroyOnLoad`).
- `World`
  - `Grid`
    - `Background` (Tilemap)
    - `Foreground` (Tilemap)
    - `Collision` (Tilemap, невидимый)
  - `GroundPoly` (mesh + `PolygonCollider2D`) — **основная земля**
  - `Islands` (коллайдеры островов/полок) — **зацепы и полки**
  - `Extras` (служебное)
- `Hero` (игрок)
- `Exit` (триггер выхода)
- `Sentry_*` (плейсхолдеры противников)

Именно имена `GroundPoly` и `Islands` используются в whitelist-фильтрации для верёвки.

---

## 11) Параметры, которые чаще всего хочется подкрутить

### 11.1 Верёвка (`GrappleController`)

- `maxDistance` — максимальная длина.
- `minDistance` — минимальная длина (чтобы не схлопывалось в 0).
- `climbSpeed` — скорость подтягивания.
- `swingForce` — сила раскачки.
- `noInputRopeDamping` — демпфирование, если нет ввода.
- `anchorSurfaceOffset` — отступ якоря и pivot от поверхности.
- `shotSpeed`, `missRetractTime` — анимация выстрела/промаха.

### 11.2 Камера (`CameraFollow2D`)

- `smoothTime`, `offset` — слежение.
- `panSpeed`, `zoomSpeed`, `minOrthoSize`, `maxOrthoSize` — free-look.

### 11.3 Падение/отскок (`WormFailureController`)

- `killFallDistance`, `killImpactSpeed` — условия смерти.
- `bounceMinHeight`, `bounceMaxHeight`, `bounceReferenceFall` — настройка bounce.
- `ropeBounceTangentFactor` — насколько bounce усиливает свинг.

### 11.4 Генерация (`SimpleWorldGenerator`)

- `pngTerrainResourcesPath` — путь к PNG террейна в Resources.
- `pngPixelsPerUnit` — масштаб (пикселей на юнит).
- `pngAlphaSolidThreshold` — порог прозрачности для solid.
- `terrainBottomY` — нижняя граница мира.
- `terrainSmoothIterations` — число итераций сглаживания контуров.

---

## 12) Известные ограничения/договорённости

- Верёвка намеренно **не взаимодействует с тайлами**.
- Физика уровня должна жить в `GroundPoly`/`Islands`.
- Обмотка верёвки рассчитана на pivot-цепочку; текущая логика ориентирована на устойчивость (не на бесконечные многооборотные намотки вокруг сложных форм).

---

## 13) Где смотреть код

- Меню/Touch UI/старт матча: `Assets/Scripts/Bootstrap.cs`
- Очередность ходов/правила оружия: `Assets/Scripts/TurnManager.cs`
- Прицел: `Assets/Scripts/WormAimController.cs`
- Движение по поверхности: `Assets/Scripts/HeroSurfaceWalker.cs`
- Верёвка: `Assets/Scripts/GrappleController.cs`
- Выбор оружия + HUD: `Assets/Scripts/HeroAmmoCarousel.cs`
- Оружие:
  - Grenade: `Assets/Scripts/HeroGrenadeThrower.cs`, `Assets/Scripts/GrenadeProjectile.cs`
  - ClawGun: `Assets/Scripts/HeroClawGun.cs`
  - Teleport: `Assets/Scripts/HeroTeleport.cs`
- AI: `Assets/Scripts/AI/SpiderBotController.cs`
- Взрывы/FX: `Assets/Scripts/ExplosionController.cs`, `Assets/Scripts/GrenadeExplosionFx.cs`
- Здоровье: `Assets/Scripts/SimpleHealth.cs`
- Генерация/земля/кратеры: `Assets/Scripts/SimpleWorldGenerator.cs`
