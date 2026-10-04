# REPO Quest Builder: сборщик для игроков

_Начат 2026-10-03._ Программа для Windows, которая собирает Quest-версию R.E.P.O. из копии игры самого игрока. Игрок
запускает `RepoQuestBuilder.exe`, программа находит игру, сама скачивает нужное, собирает APK и ставит его на шлем.
Новые версии ставятся через неё же. Инструкция для игроков лежит в `builder/release/README.txt` (входит в архив).

Версию для Mac пока не выпускаем (решение пользователя от 03.10). Mac остаётся машиной разработки: наши сборки идут
тем же конвейером из терминала (`scripts/build.sh`), окно запускается из исходников для проверки.

## Как устроено

```
builder/
  QuestBuilder.Core/   вся логика: конвейер, загрузки, Unity, Python, обновления, установка на Quest
  QuestBuilder.App/    окно (Avalonia), exe для игроков: RepoQuestBuilder.exe
  QuestBuilder.Cli/    тот же конвейер из терминала (наши сборки, тесты, инструменты выпуска)
  QuestBuilder.Tests/  модульные тесты (dotnet test builder/QuestBuilder.Tests)
  kit.json             версии и SHA-256 всего, что скачивается; список проверенных версий игры
  python/              requirements.in + requirements.lock (хеши под Windows и macOS)
  release/             README.txt и THIRD-PARTY-NOTICES.txt для архива игрока
  licenses/, SOURCES.md  тексты лицензий и происхождение содержимого kit
```

**Конвейер** (`QuestBuilder.Core/Pipeline`). Это перенос bash-скриптов `make-quest-project.sh`, `make-quest-vr.sh`,
`apply-quest-runtime.sh` и `build-quest.sh` (удалены, остались в истории git) на C#:

| Шаг | Что делает | Пересобирается, когда меняются |
|---|---|---|
| export | AssetRipper 2.0.0 без окна (`--headless`, локальный HTTP API: `/Settings/Update`, `/LoadFolder`, `/Export/UnityProject`) | файлы игры (размеры и даты), версия AssetRipper, настройки |
| bake | HarmonyBaker: патчи RepoXR запекаются в сборки игры | DLL игры, RepoXR.dll и RepoXR.Standalone.dll из kit, зависимости, сам baker |
| project | копия экспорта + Python-скрипты + плагины, `link.xml`, ассеты RepoXR; Library сохраняется | экспорт, результат bake (содержимое DLL), содержимое kit |
| unity | два запуска редактора: `QuestBuild.Prepare`, `QuestBuild.BuildApk` (`-versionName/-versionCode`) | проект, версия, флаги сборки |

Каждый шаг записывает «штамп» входов в `state.json` рабочей папки. Шаг с прежним штампом пропускается, а прерванная
сборка продолжается с упавшего шага. Значит, обновление сборщика пересобирает начиная с bake (экспорт остаётся),
обновление игры — с экспорта.

**AssetRipper раздаёт ассетам случайные GUID при каждом экспорте** (проверено 03.10: два экспорта подряд расходятся в
23 тыс. файлов, в основном .meta). Внутри одного экспорта ссылки согласованы, так что на результат это не влияет. Но
после нового экспорта Unity импортирует всё заново (~12 минут на M-серии). Обновления сборщика экспорт не трогают.
Если повторный импорт после обновлений игры станет проблемой, можно сделать скрипт, который заменяет случайные GUID на
производные от пути ассета.

**Python-скрипты** остаются листовыми шагами. Пути они берут из `scripts/lib/paths.py`: переменные `REPOQUEST_WORK`,
`REPOQUEST_GAME`, `REPOQUEST_UNITY_EDITOR`, `REPOQUEST_TOOL`; без них работают со старыми путями на Mac. Текст пишется
с `newline="\n"`, а Python запускается в режиме UTF-8 (`PYTHONUTF8=1`), поэтому Windows даёт те же байты, что Mac.
`make-kit.sh` проверяет, что ни одна запись текста не забыла `newline=`. `stereo_halos.py` делает ID компонентов из
хеша (раньше они были случайными, и Unity при каждой сборке заново импортировал 136 префабов).

**Сверка переноса (03.10):** старый bash-конвейер и новый на одном и том же экспорте дали одинаковые хеши всех 32 502
файлов проекта (без Library). Запечённые DLL, `addressables-entries.json` и `shader-states.json` тоже совпали, а
зависимости, скачанные с сайтов издателей, совпали байт в байт с тем, что лежало в `work/`.

**Kit** (`scripts/make-kit.sh` → `work/kit`): `scripts/`, `unity-tools/` и то, что мы собираем из исходников:
RepoXR.dll с патчами, RepoXR.Standalone.dll, SteamShim и SteamKit2, libopus, Android-ассеты RepoXR. Плюс kit.json,
lock-файл Python и лицензии. Файлов игры в нём нет: `make-kit.sh` это проверяет. В выпуске kit вшит в exe как ресурс
`kit.zip` и распаковывается в `<рабочая папка>\kit\<версия>`.

**Что скачивается у игрока** (SHA-256 закреплены в `kit.json`): AssetRipper (GitHub), uv (GitHub) → управляемый
Python 3.13 и пакеты по `requirements.lock` с хешами, всё внутри рабочей папки. Ещё релиз RepoXR 1.2.3 с Thunderstore
(RuntimeDeps), BepInEx 5.4.23.5 (GitHub), NuGet-пакеты BepInEx.BaseLib (nuget.bepinex.dev) и CustomDiscoverStateLib
(nuget.daxcess.io), исходники встроенных шейдеров Unity, пакеты Unity с packages.unity.com. .NET игроку не нужен.
HarmonyBaker, AssemblyRenamer и Il2CppFixes вызываются как `<exe> --tool <имя> ...`. Окно — однофайловый exe, а в
нём MonoMod (детуры HarmonyX, на которых держится HarmonyBaker) не находит JIT: в однофайловом приложении `clrjit`
вшит в сам exe (проверено под CrossOver 03.10: «Could not locate clrjit library»). Поэтому инструменты едут внутри
exe отдельно: `tools.zip` — QuestBuilder.Cli, опубликованный как обычная самодостаточная папка. При первой сборке
программа распаковывает его в `<рабочая папка>\tools\builder-tools-<версия>` (`ToolLauncher.For`). Наши сборки на
Mac запускают инструменты прямо из Cli.

**Unity** ставится через Hub CLI (`Setup/UnitySetup.cs`):
1. Установщик Hub берётся из `latest.yml` на CDN Unity и проверяется по sha512 и подписи Authenticode «Unity
   Technologies».
2. Hub ставится тихо (`/S`), Windows спрашивает UAC.
3. Редактор ставится командой
   `Unity Hub -- --headless install --version 2022.3.62f3 --changeset 96770f904ca7 --module android android-sdk-ndk-tools android-open-jdk`.

Лицензию Personal из командной строки получить нельзя: игрок один раз входит в Unity Hub. Программа проверяет файл
`UnityEntitlementLicense.xml`, а если Unity всё же отказался работать, распознаёт это в логе и снова показывает
инструкцию. Пока Unity качается, параллельно идут экспорт и запекание.

**Windows-особенности.**
- Рабочая папка по умолчанию — `<диск с наибольшим свободным местом>:\RepoQuest`. Путь должен быть из ASCII и не
  длиннее 40 символов: Unity, Gradle и NDK ломаются на кириллице в имени пользователя.
- В Library пути доходят до ~240 символов от корня проекта. Поэтому программа предлагает включить `LongPathsEnabled`
  (системная настройка, через UAC).
- Программа не даёт компьютеру уснуть во время сборки.
- Сборщик не подписан платным сертификатом, поэтому SmartScreen покажет предупреждение (объяснено в README).

**Обновления** (`Updates/`):
- `latest.json` содержит версию, заметки RU/EN, имя zip и его SHA-256 для каждой платформы. `latest.json.sig` —
  подпись ECDSA P-256. Публичный ключ — `builder/release-public-key.txt`, он вшит в exe. Приватный ключ лежит в
  `~/.config/repoquest/release-signing-key.pem`, вне репозитория. **Нужна резервная копия: без ключа выпустить
  обновление нельзя.**
- Источник обновлений вшивается при выпуске (`--update-source`). Его заменяют файл `update-source.txt` рядом с exe или
  ключ `--update-source` (URL или папка). Пока на GitHub ничего не выкладываем, источник пустой.
- Самообновление: работающий exe переименовывается в `.old`, новый встаёт на его место и запускается. После этого
  программа видит, что APK собран прежним kit, и предлагает пересобрать.

**Установка на Quest** идёт через adb из Android SDK Unity. Если на шлеме сборка с другой подписью (отладочные ключи
Android свои на каждом компьютере), программа сохраняет `/sdcard/Android/data/io.repoxr.quest/files` (сохранения,
настройки RepoXR, вход в Steam, косметику), переустанавливает игру и возвращает данные. versionCode =
major·10000 + minor·100 + patch; у наших dev-сборок он равен 1.

**В шлеме:**
- на странице входа в Steam есть предупреждение о риске для аккаунта (неофициальный клиент);
- над кнопкой Steam в главном меню показана версия порта и автор: «REPO Quest <версия> by Dobutsu» (`Application.version`: версия сборщика или `dev-<commit>`).

**Замеры (Mac M-серии, 03.10).** Сценарий игрока с пустой рабочей папкой (`player-build`, выпуск 1.0.0): экспорт
50 с, запекание 4 с, проект около минуты, Unity 50 минут (импорт 8,5 минуты, сборка APK ~41 минута). Всего 52
минуты. Самообновление 1.0.0 → 1.0.1 (`update --then-build`): подпись проверена, exe заменён, экспорт переиспользован,
пересборка 38 минут. На Windows-ПК, скорее всего, дольше. **Что ускорить:** при каждой сборке Unity заново
компилирует ~75 шейдеров (в основном наши сгенерированные: `Semiwork/*`, `Hurtable/*`, `W/*`), хотя Library
сохранена. Проект каждый раз пересоздаётся из экспорта, и кэш шейдеров не используется повторно. Это 15–20 минут
каждой пересборки.

## Наша сборка на Mac

```sh
scripts/build.sh [<apk name>] [-developmentBuild] [-profiling] [--force] [--only export,bake,project,unity]
```

Скрипт обновляет kit из текущего кода (`make-kit.sh`), собирает Cli и запускает конвейер с рабочей папкой `work/`. Шаги
без изменений пропускаются. Результат: `work/build/<имя>.apk` (по умолчанию `repo-quest-vr`), логи в `work/logs/`
(`builder.log`, `unity-prepare.log`, `unity-build.log`, `assetripper.log`). Python берётся тот же, что у игрока
(`work/tools/venv` через uv), а не `work/.venv`. `bake.sh` и `make-crossover-test.sh` остались для плоского теста в
CrossOver.

## Выпуск версии

```sh
scripts/release.sh 1.0.1 --notes-ru notes-ru.txt --notes-en notes-en.txt [--update-source <url>] [--mac-test]
```

Результат лежит в `work/release/1.0.1/`: `RepoQuestBuilder-1.0.1-win-x64.zip` (exe с kit + README), `latest.json`,
`latest.json.sig`, с `--mac-test` ещё сборка для этого Mac. Ничего не публикуется.

Как проверить обновление без публикации:
1. Положить старый exe в папку, рядом `update-source.txt` с путём к `work/release/<новая версия>`, либо раздать эту
   папку через `python3 -m http.server`.
2. Запустить старый exe: он должен предложить обновление, заменить себя и пересобрать APK.

## Проверка

- `dotnet test builder/QuestBuilder.Tests`: VDF и поиск игры (в том числе в бутылках CrossOver), GUID плагинов как у
  bash-конвейера, подпись манифеста, версии, adb, kit.json, рабочая папка, state.json, источник-папка обновлений.
- `scripts/crossover-windows-check.sh`: Windows-сборка инструментов сборщика под CrossOver (Windows-версии uv,
  Python и .NET) делает bake и project из того же экспорта и kit, что и Mac, и проекты сравниваются по хешам.
  03.10 так нашлась ошибка путей в `restore_resource_names.py`; после исправления результат совпал: 32 502 файла.
- Сверка с эталоном при изменении конвейера: снимок хешей файлов проекта (без Library) до и после.
- Сценарий игрока без окна: `scripts/release.sh X.Y.Z --mac-test` даёт однофайловый `QuestBuilder.Cli` для Mac
  с kit и инструментами внутри. Дальше `QuestBuilder.Cli player-build --workspace <папка> --yes`, а для
  самообновления `QuestBuilder.Cli update --update-source work/release/<новая> --workspace <папка> --then-build --yes`.
- Окно без дисплея: `RepoQuestBuilder --kit work/kit --screenshot <папка>` рисует окно, состояние сборки и все
  диалоги на обоих языках в PNG.
- Windows проверяет друг пользователя. При сбое он нажимает «Собрать отчёт» и присылает zip: только логи и сводка, без
  файлов игры.
