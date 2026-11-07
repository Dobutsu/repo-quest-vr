# b17: зеркало в магазине в single-pass (каждому глазу своя текстура)

Ветка `repo-0.5.0`, последний коммит 9ed5d07 (вышел builder 1.1.1 с исправлением шрифтов). Код зеркала написан
и компилируется, но **не закоммичен и ещё не проверялся в шлеме**.

## Что изменено (не закоммичено)

- `patches/repoxr/0037-shop-mirror-single-pass-per-eye.patch`:
  - Source/Config.cs: новая настройка `Rendering / ShopMirrorSinglePass`, в VR Settings она называется
    "Shop Mirror Single Pass". Варианты `Hidden`, `AlternateEyes` (по умолчанию), `BothEyes`.
  - Source/Patches/Rendering/MirrorPatches.cs: в single-pass отражение рисуется в `Camera.onPreCull` стерео-камеры,
    один раз за кадр (`Time.frameCount`):
    - `AlternateEyes`: левый глаз в чётные кадры, правый в нечётные;
    - `BothEyes`: оба глаза каждый кадр, `resolutionScale` ×0.5.

    Левый глаз рисуется в текстуру игры. Для правого на время рендера в `reflectionTexture` подставляется вторая RT
    (с тем же дескриптором), и она привязывается к `_ReflectionTexRight` через property block. В multi-pass
    `_ReflectionTexRight` равна `reflectionTexture`. `EyeFromDisplay` в single-pass берёт render parameter по
    индексу глаза.
- `unity-tools/ShaderOverrides/Semiwork_Mirror.shader`: во frag вызывается
  `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i)`, затем `unity_StereoEyeIndex == 0 ? _ReflectionTex : _ReflectionTexRight`.

## Сборка (~80 мин)

Шейдер изменился, поэтому сборка только скриптов (`-scriptsOnly`) не подходит, нужна полная.

```
scripts/prepare-repoxr.sh          # применяет патчи 0001–0037, собирает RepoXR.dll
tasks/fastbuild.sh                 # kit → bake → stage/rename → copy → generate_shaders → Unity; итог work/build/repo-quest-vr-b17.apk
```

Запускать в фоне, логи пишутся в `work/logs/*-b17.log`. Перед Unity проверить, что в `work/logs/shaders-b17.log`
сгенерирован Semiwork/Mirror.

## Проверка в шлеме (Quest 3)

Учесть [[headset-shared-with-peak]]: шлем общий с сессией Peak. Не убивать io.peakvr.quest, не крутить BACK в
цикле, не делать `logcat -c`.

1. `adb install -r work/build/repo-quest-vr-b17.apk`. Если статичный шлем не даёт запустить игру из-за Guardian:
   `setprop debug.oculus.guardian_pause 1`, после теста обязательно вернуть 0.
2. Single-pass включён по умолчанию. Зайти в магазин и посмотреть на зеркало у машины косметики. В логе должно быть
   `[Mirror] ... single-pass, AlternateEyes` и `rendering the Left/Right eye's reflection, single-pass`.
3. Проверить:
   - отражение есть и не белое;
   - у каждого глаза свой ракурс (стерео-глубина отражения);
   - нет мерцания от чередования глаз;
   - вдали зеркало гаснет (distance fade, туман).
4. FPS в магазине: без зеркала (Hidden), AlternateEyes, BothEyes. Ориентир: в 1.1.0 single-pass без зеркала даёт
   71–73 FPS, multi-pass с зеркалом около 50.
5. После правок cfg RepoXR в шлеме нужен `chmod 666`.

## Возможные проблемы

- Если у правого глаза серая или белая картинка, `_ReflectionTexRight` не привязалась: смотреть `BindRightEye` и не
  затирает ли игра property block.
- Если оба глаза видят одно и то же отражение, `unity_StereoEyeIndex` во фрагментном шейдере не работает (multiview).
  Тогда передавать индекс глаза из вершинного шейдера своим `nointerpolation` полем.
- Если отражение съехало или зеркальное не в ту сторону, проверить `EyeFromDisplay` в single-pass (одна render pass,
  параметры по глазам).

## После теста

Ничего не коммитить и не выкладывать без явной просьбы. Если всё работает, предложить: коммит 0037 и шейдера, затем
релиз 1.1.2 тем же способом, что и 1.1.1 (см. память [[repo-v050-unity6-port]]). Пост в канал пользователь пишет сам.

# b18: b17 + повторные Steam-тикеты (сборка только кода)

`tasks/codebuild.sh b18`: Unity `-scriptsOnly` поверх b17, около 15–20 минут, шейдеры и ассеты берутся из b17. Изменён
только SteamShim (`tools/SteamShim/SteamKit/AuthTicketHandler.cs`, `SteamKitBackend.cs`), в нём перенесён фикс из
SteamKit 3.5.0 (issue #1567). С 2025-08 Steam не возвращает JobID в ack на повторный `ClientAuthList`, поэтому второй и
следующие тикеты за логон падали по таймауту (`[Steam] auth ticket timed out`, Photon не пускал). Теперь ack
сопоставляется по `ticket_crc`. Если ack не пришёл за 5 с, тикет всё равно отдаётся, а в логе пишется
`not acknowledged by Steam`.

Проверка в шлеме: зайти в лобби друга, выйти, зайти снова (или в другое лобби). Оба раза Photon должен подключиться.
В логе не должно быть `auth ticket timed out`/`failed`.

На Маке: `SteamShimProbe ticket --count 3`. Но запускать только когда игра на шлеме закрыта: у неё копия
`work/steam-session.json` с тем же LoginID, и Steam выбивает одну из сессий (`LoggedInElsewhere`).
