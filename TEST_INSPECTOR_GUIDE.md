# Тестовый файл: руководство по настройкам в Inspector

Назначение: быстрый справочник для тестов баланса и визуала.

> Важно: диапазоны ниже — **рабочие/безопасные** для тестирования. Если ставить экстремумы, можно получить нестабильную физику, невалидную анимацию или «ломаный» UI.

---

## 1) Герой

### 1.1 `HeroSurfaceWalker` (`Assets/Scripts/HeroSurfaceWalker.cs`)
Компонент движения героя по поверхности.

| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `moveSpeed` | Базовая горизонтальная скорость | `2.0 .. 8.0` |
| `acceleration` | Насколько быстро разгоняется/тормозит | `20 .. 120` |
| `airControl` | Управление в воздухе (доля от наземного) | `0 .. 1` |
| `jumpSpeed` | Сила прыжка | `4 .. 12` |
| `groundMask` | Какие слои считаются землей | Слой(и) terrain/ground |
| `groundCheckDistance` | Дистанция проверки земли | `0.05 .. 0.6` |
| `maxSlopeAngle` | Максимальный угол склона для «ходьбы» | `30 .. 85` |
| `stickToGroundForce` | Прижим к поверхности на склонах | `0 .. 100` |
| `idleDeceleration` | Замедление при отпускании движения | `20 .. 150` |
| `idleStopSpeed` | Порог «полной остановки» | `0.01 .. 0.3` |
| `cancelTangentGravityFactor` | Компенсация тангенциальной гравитации в idle | `0 .. 2` |
| `movingMaterial` / `idleMaterial` | Физматериал при движении/стое | Подбирается по проекту |

---

### 1.2 `WormAimController` (`Assets/Scripts/WormAimController.cs`)
Компонент прицеливания и ретикла.

| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `aimCamera` | Камера для прицела | Ссылка на активную камеру |
| `useKeyboardAim` | Разрешить клавиатурное прицеливание | `true/false` |
| `aimRadius` | Дистанция до ретикла | `3 .. 20` |
| `aimAngularSpeedDeg` | Скорость поворота прицела (град/с) | `120 .. 720` |
| `aimStepDeg` | Шаг дискретного поворота | `0 .. 30` (в коде clamp `>=0`) |
| `stopAtVerticalUnlessTurned` | Стоп на вертикали до смены стороны | `true/false` |
| `showReticle` | Показывать ретикл | `true/false` |
| `reticleColor` | Цвет ретикла | Любой RGBA |
| `reticleWorldSize` | Размер ретикла в мире | `0.2 .. 2.0` |

---

## 2) Оружие

### 2.1 `HeroClawGun` (`Assets/Scripts/HeroClawGun.cs`)
Клоуган: выстрел, анимация, позиция, урон, линия прицела.

#### Секция Sprite Sheet
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `spritesheetResourcesPath` | Путь в `Resources` к спрайт-листу | Напр. `Weapons/claw_gun` |
| `frames` | Количество кадров анимации | `1 .. 64` |
| `sheetColumns` / `sheetRows` | Разбиение листа | `>=1` |
| `pixelsPerUnit` | Плотность пикселей в мире | `16 .. 256` |

#### Секция Firing
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `shotsPerSecond` | Скорострельность | `0.2 .. 20` |
| `fireAnimationFps` | FPS анимации выстрела | `6 .. 60` |
| `impactOnFrameIndex` | На каком кадре применить попадание | `0 .. frames-1` |
| `bulletRange` | Дальность | `2 .. 100` |
| `bulletExplosionRadiusHeroHeights` | Радиус кратера/взрыва в высотах героя (дефолт 0.575; диаметр = 1.15 высоты героя) | `0 .. 3` |
| `scaleRangeFromGrenade` | Масштабировать дальность от гранаты | `true/false` |
| `bulletRangeAsGrenadeRangeMultiplier` | Множитель к дальности гранаты | `0.1 .. 3` |

#### Секция Aim Line (старая простая линия)
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `showAimLine` | Показывать старую простую линию (откл. по умолчанию) | `true/false` |
| `aimLineWidth` | Толщина линии | `0.005 .. 0.2` |
| `aimLineColor` | Цвет линии | Любой RGBA |
| `fireDirectionDownOffsetDeg` | Доп. смещение направления вниз | `-30 .. 30` |

#### Секция Fancy Aim Line (энергетическая линия + прицел)
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `useFancyAimLine` | Использовать анимированную линию с прицелом (вкл. по умолчанию) | `true/false` |

##### Компонент `AimLineVisual2D` (создаётся автоматически)
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `lineWidth` | Толщина пунктирной линии | `0.02 .. 0.15` |
| `lineColorStart` | Цвет начала линии (у героя) | Любой RGBA |
| `lineColorEnd` | Цвет конца линии (у цели) | Любой RGBA |
| `uvScrollSpeed` | Скорость бегущих штрихов | `0.5 .. 8` |
| `uvTilingX` | Кол-во повторов текстуры вдоль линии | `3 .. 20` |
| `scopeWorldSize` | Размер прицела-скопа | `0.2 .. 1.5` |
| `scopePulseAmplitude` | Амплитуда пульсации масштаба | `0 .. 0.2` |
| `scopePulseSpeed` | Скорость пульсации | `1 .. 10` |
| `lineSortingOrder` | Sorting order линии | `80 .. 100` |
| `scopeSortingOrder` | Sorting order прицела | `81 .. 101` |

#### Секция Ammo
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `maxShots` | Максимум патронов | `0 .. 999` |
| `shotsLeft` | Текущий остаток | `0 .. maxShots` |

#### Секция Weapon Placement
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `weaponHeightFractionOfHeroHeight` | Высота спрайта клоугана от высоты героя | `0.05 .. 1.0` |
| `weaponOffsetFractionOfHeroSize` | Базовый радиус/смещение от центра героя | `x,y: -2 .. 2` |
| `weaponSpriteLocalOffsetFractionOfHeroSize` | Тонкая коррекция спрайта | `x,y: -2 .. 2` |
| `weaponSpritePivotPixels` | Пиксельный pivot для нарезки спрайта | Внутри размеров кадра |
| `weaponAimAngleOffsetDeg` | Угловой оффсет оружия | `-180 .. 180` |
| `weaponOffsetAltFractionOfHeroSize` | Альт-смещение (другая рука/сторона) | `x,y: -2 .. 2` |
| `weaponSpriteLocalOffsetAltFractionOfHeroSize` | Альт-тонкая коррекция | `x,y: -2 .. 2` |
| `aimXDeadzone` | Мертвая зона X для выбора стороны | `0 .. 1` |
| `baseMirrorSpriteX` / `baseMirrorSpriteY` | Базовое зеркалирование | `true/false` |
| `flipYWhenFacingLeft` | Инверсия по Y при взгляде влево | `true/false` |
| `sortingOrderOffsetFromHero` | Порядок сортировки относительно героя | `-50 .. 200` |

#### Секция Damage
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `bulletDamage` | Урон попадания | `0 .. 200` |
| `bulletKnockbackImpulse` | Импульс отталкивания | `0 .. 50` |
| `hitMask` | Какие слои пробивает/попадает | Настроить по слоям проекта |

---

### 2.2 `HeroGrenadeThrower` (`Assets/Scripts/HeroGrenadeThrower.cs`)
Гранаты: иконка, траектория, боеприпас.

#### Icon
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `grenadeIconResourcesPath` / `grenadeIconSprite` | Источник иконки | Корректный ресурс/спрайт |
| `iconHeightFractionOfHeroHeight` | Размер иконки в руке | `0.02 .. 0.3` |
| `iconOffsetFractionOfHeroSize` | Смещение иконки | `x,y: -2 .. 2` |
| `iconOffsetAimUpExtraFractionOfHeroHeight` | Доп. смещение при прицеле вверх | `x,y: -1 .. 1` |

#### Throw
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `maxRangeFractionOfRope` | Макс дальность броска от длины веревки | `0.1 .. 2.0` |
| `minUpAimY` | Минимум Y прицела для разрешения броска | `-1 .. 1` |
| `flightSlowdown` | Коэффициент замедления полета | `0.2 .. 3.0` |
| `maxHeightFractionOfHeroHeight` | Максимум высоты траектории | `0.5 .. 8` |
| `minRangeFractionWhenHigh` | Мин. дальность в высоком броске | `0 .. 1` |
| `spawnForwardFractionOfHeroWidth` | Смещение точки спавна вперед | `-1 .. 2` |
| `spawnUpFractionOfHeroHeight` | Смещение точки спавна вверх | `-1 .. 2` |
| `grenadeLifetime` | Время жизни/фьюз | `0.1 .. 30` |

#### Trajectory / Projectile / Ammo
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `showTrajectory` | Показывать траекторию | `true/false` |
| `trajectorySteps` | Кол-во сегментов | `2 .. 200` |
| `trajectoryTimeStep` | Шаг симуляции траектории | `0.005 .. 0.2` |
| `trajectoryLineWidth` | Толщина линии | `0.005 .. 0.2` |
| `trajectoryColor` | Цвет траектории | Любой RGBA |
| `grenadeSpriteResourcesPath` / `grenadeSprite` | Спрайт гранаты | Корректный ресурс/спрайт |
| `grenadeSpinDegPerSecond` | Скорость вращения гранаты | `0 .. 5000` |
| `maxGrenades` / `grenadesLeft` | Боезапас | `0 .. 999` и `0..maxGrenades` |

---

### 2.3 `HeroTeleport` (`Assets/Scripts/HeroTeleport.cs`)
Телепорт (одноразовый за матч по умолчанию).

| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `Enabled` | Включить режим телепорта у активного героя | `true/false` |
| `usedThisMatch` | Уже использован в матче | `false` в начале матча |

> В этом компоненте немного инспекторных полей — логика в основном кодовая.

---

## 3) Веревка

### `GrappleController` (`Assets/Scripts/GrappleController.cs`)
Основной контроллер веревки: выстрел, прикрепление, свинг, reel, визуал.

#### Shot / Attach
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `maxDistance` | Дальность выстрела крюка | `2 .. 100` |
| `shotTravelTime` | Время долета крюка | `0.01 .. 1.0` |
| `missRetractTime` | Время втягивания при промахе | `0.01 .. 2.0` |
| `anchorSurfaceOffset` | Отступ точки зацепа от поверхности | `0 .. 0.5` |
| `minDistance` | Минимальная дистанция зацепа | `0 .. 10` |
| `maxRopeLength` | Макс длина веревки | `2 .. 200` |

#### Swing / Reel / Stiffness / Wrap
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `swingForce` | Сила раскачки | `0 .. 200` |
| `noInputRopeDamping` | Затухание без ввода | `0 .. 30` |
| `verticalFallBiasFraction` | Вертикальный приоритет падения | `0 .. 2` |
| `verticalFallBiasTangentDamping` | Демпфирование касательной скорости | `0 .. 50` |
| `verticalFallBiasControlAngleDeg` | Угол влияния вертикального bias | `0 .. 90` |
| `verticalFallBiasKickSpeed` | Стартовый «пинок» вертикали | `0 .. 10` |
| `ropeUpwardGravityFactor` | Множитель гравитации вверх | `0 .. 30` |
| `ropeGravityAssist` | Ассист гравитации вдоль веревки | `0 .. 5` |
| `ropeGravityAssistMinAngleDeg` | Мин угол для ассиста | `0 .. 90` |
| `ropeGravityAssistMinAngleDegShort` | Мин угол для короткой веревки | `0 .. 90` |
| `ropeGravityAssistMinAngleShortFraction` | Порог «короткой» веревки | `0 .. 1` |
| `ropeGravityAssistMaxTangentSpeed` | Ограничение по касательной скорости | `1 .. 5000` |
| `reelSpeed` | Скорость подтягивания | `0 .. 100` |
| `reelInGravityScaleFactor` | Гравитация во время reel-in | `0 .. 5` |
| `stiffLineDamping` | Демпфирование жесткой фазы | `0 .. 200` |
| `stiffLineSpring` | Жесткость пружины | `0 .. 2000` |
| `stiffLineMaxStep` | Макс шаг коррекции | `0.01 .. 2.0` |
| `minSegment` | Минимальный сегмент wrap | `0.005 .. 1.0` |

#### Rope visual / sparks / in-hand sprite
| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `ropeWidth` / `ropeWidthMultiplier` | Толщина веревки | `0.01 .. 0.5`, `0.1 .. 5` |
| `enableRopeVisuals` / `enableRopeSparks` / `enableAnchorDrop` | Вкл/выкл эффекты | `true/false` |
| `ropeEdgeColor` / `ropeCoreColor` / `sparkColor` | Цвета | Любой RGBA |
| `ropeCoreWidthFraction` | Толщина сердцевины | `0 .. 1` |
| `sparkSpacingHeroHeights` | Интервал искр | `0.1 .. 20` |
| `sparkSpeed` | Скорость искр | `0 .. 100` |
| `sparkSegmentLength` | Длина сегмента искры | `0.05 .. 5` |
| `sparkAmplitude` | Амплитуда искры | `0 .. 2` |
| `maxSparks` | Макс количество искр | `0 .. 100` |
| `anchorDropStrands` | Кол-во «нитей» якоря | `1 .. 20` |
| `anchorDropLength` | Длина drop-эффекта | `0 .. 5` |
| `anchorDropSpread` | Разброс drop-эффекта | `0 .. 2` |
| `anchorDropWidthMultiplier` | Толщина drop-эффекта | `0.1 .. 10` |
| `anchorHeadHalfFractionOfHeroHeight` | Размер «головы» якоря | `0.05 .. 1.0` |
| `anchorRenderInsetFractionOfHeroHeight` | Внутренний отступ рендера | `0 .. 1` |
| `anchorLightningAmplitudeFraction` | Амплитуда молнии на якоре | `0 .. 2` |
| `ropeHandSpriteResourcesPath` | Спрайт веревки в руке | Ресурс `Weapons/rope` и т.п. |
| `ropeHandHeightFraction` | Размер спрайта в руке | `0.02 .. 1.0` |
| `ropeHandOffsetFraction` | Смещение спрайта в руке | `x,y: -2 .. 2` |
| `ropeHandCenterOffsetPixels` | Пиксельная коррекция центра | обычно `-200 .. 200` |
| `ropeHandPixelsPerUnit` | PPU спрайта в руке | `8 .. 512` (в коде min `0.01`) |
| `ropeHandPivotNormalized` | Нормализованный pivot [0..1] | `x,y: 0 .. 1` |
| `ropeHandAimAngleOffsetDeg` | Угловой оффсет спрайта в руке | `-180 .. 180` |
| `ropeHandTridentDownAngleDeg` | Угол при «вниз» состоянии | `-360 .. 360` |
| `ropeHandUseSharedSettingsForAllHeroes` | Общие настройки веревки для всех героев | `true/false` |
| `ropeHandFollowAim` | Следовать направлению прицела | `true/false` |
| `ropeHandFlipYWhenFacingLeft` | Инверсия Y при взгляде влево | `true/false` |
| `ropeHandSortingOrderOffset` | Сортировка относительно героя | `-50 .. 200` |

---

## 4) Мир

### 4.1 `SimpleWorldGenerator` (`Assets/Scripts/SimpleWorldGenerator.cs`)
Генерация/загрузка террейна по PNG.

| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `pngTerrain` | Явно заданная текстура террейна | Опционально |
| `pngTerrainResourcesPath` | Путь к террейну в `Resources` | `Levels/<имя>` |
| `pngEntities` | Текстура сущностей/спавнов | Опционально |
| `pngEntitiesResourcesPath` | Путь к entities-текстуре | `Levels/entities` |
| `pngPixelsPerUnit` | Масштаб пиксель→мир | `2 .. 64` |
| `pngAlphaSolidThreshold` | Порог «твердых» пикселей | `0 .. 1` (Range в коде) |
| `pngHeroColor` | Цвет пикселей, где спавнятся герои | Должен совпадать с картой |
| `pngEntitySpawns` | Соответствие цветов prefab’ам | Настроить по палитре карты |
| `terrainBottomY` | Низ мира (смещение по Y) | `-200 .. 50` |
| `terrainSmoothIterations` | Кол-во сглаживаний | `0 .. 20` |
| `terrainCellularSmoothIterations` | Cellular-сглаживание | `0 .. 20` |
| `terrainMinIslandCells` | Мин размер острова (для фильтра) | `0 .. 10000` |
| `terrainMinHoleCells` | Мин размер отверстия | `0 .. 10000` |
| `terrainMinLoopAreaCells` | Мин площадь петли/замкнутой области | `0 .. 20000` |

---

### 4.2 `TurnManager` (`Assets/Scripts/TurnManager.cs`)
Правила раунда/ходов.

| Поле | Для чего | Рекомендуемый диапазон |
|---|---|---|
| `turnSeconds` | Длительность хода | `5 .. 300` |
| `postActionClampSeconds` | «Окно escape» после действия | `0 .. turnSeconds` |
| `loseBelowY` | Y-граница проигрыша (падение) | зависит от карты, обычно `-500 .. 0` |
| `loseGraceSeconds` | Льготная задержка перед поражением | `0 .. 10` |
| `spidersTeamIndex` | Индекс команды пауков | `0 .. 7` |
| `damageReactionSeconds` | Пауза на реакцию после урона | `0 .. 10` |
| `logTurnOrder` | Лог порядка ходов | `true/false` |
| `turnLogHistorySize` | Размер истории логов | `1 .. 200` |
| `logWeaponBlocks` | Лог блокировок оружия | `true/false` |

---

## Быстрый тест-профиль (если нужно «просто проверить всё сразу»)

1. Герой:
   - `moveSpeed=4.5`, `acceleration=55`, `airControl=0.35`, `jumpSpeed=7.5`
2. Прицел:
   - `aimRadius=9`, `aimAngularSpeedDeg=420`, `aimStepDeg=5`
3. ClawGun:
   - `shotsPerSecond=2`, `bulletRange=25`, `weaponAimAngleOffsetDeg=0`
4. Веревка:
   - `maxDistance=36`, `maxRopeLength=36`, `swingForce=32`, `reelSpeed=9`
5. Мир:
   - `pngPixelsPerUnit=8`, `pngAlphaSolidThreshold=0.2`, `terrainSmoothIterations=3`
6. Ходы:
   - `turnSeconds=60`, `postActionClampSeconds=5`

---

## Примечания

- Поля типа `LayerMask`, `Material`, `Sprite`, `Texture2D` не имеют «числового диапазона» — проверяй валидность ссылки и слой/ресурс.
- Для визуальных оффсетов (`Vector2`) лучше менять шагом `0.02 .. 0.1`, чтобы не «перестреливать» нужную позицию.
- После правок физики (rope/swing/move) обязательно тестируй на 2–3 разных картах с разными углами поверхности.
