namespace RepoQuest.Builder.App.Localization;

// Russian texts: short, plain words for players, not developers. Menus of English-only programs (Unity Hub, the Quest's
// own prompts) are quoted in English, the way players see them.
internal static partial class Strings
{
    public static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["app.subtitle"] = "Собирает R.E.P.O. для Meta Quest 3 и Pico 4 из вашей копии игры",
        ["app.version"] = "версия {0}",
        ["lang.tooltip"] = "Язык интерфейса",
        ["unit.gb"] = "{0} ГБ",
        ["unit.mb"] = "{0} МБ",

        ["common.cancel"] = "Отмена",
        ["common.close"] = "Закрыть",
        ["common.ok"] = "ОК",
        ["common.tryAgain"] = "Попробовать снова",
        ["common.license"] = "Лицензия",
        ["common.details"] = "Подробности",
        ["common.unknown"] = "неизвестна",
        ["common.checking"] = "Проверяем…",

        ["startup.failed"] = "Сборщик не смог запуститься: {0}",

        ["banner.update.title"] = "Вышла версия {0}",
        ["banner.update.button"] = "Обновить",
        ["banner.update.downloading"] = "Скачиваем обновление… {0}",
        ["banner.gameChanged"] = "Игра обновилась. Пересоберите APK, чтобы и дальше играть с друзьями на ПК.",
        ["banner.builderChanged"] = "Это новая версия сборщика. Пересоберите APK, чтобы получить исправления.",
        ["banner.untested"] =
            "Эта версия игры ({0}) не проверялась с этим сборщиком. Перед сборкой проверьте, нет ли обновления сборщика.",

        ["game.title"] = "Папка игры",
        ["game.notFound"] = "Не найдена. Укажите папку, где лежит REPO.exe.",
        ["game.version"] = "Версия: {0}",
        ["game.tested"] = "проверена с этим сборщиком",
        ["game.untested"] = "с этим сборщиком не проверялась",
        ["game.choose"] = "Выбрать папку…",
        ["game.pickerTitle"] = "Выберите папку R.E.P.O. (где лежит REPO.exe)",

        ["ws.title"] = "Рабочая папка",
        ["ws.about"] = "Здесь хранятся загрузки, проект Unity и готовый APK.",
        ["ws.space"] = "Свободно: {0} · нужно: около {1}",
        ["ws.spaceUnknown"] = "Свободное место: неизвестно",
        ["ws.spaceLow"] = "Для сборки не хватает места.",
        ["ws.change"] = "Изменить…",
        ["ws.pickerTitle"] = "Выберите рабочую папку",

        ["problem.folder-missing"] = "Такой папки нет.",
        ["problem.folder-not-game"] =
            "Это не папка игры. Выберите папку, где лежит REPO.exe (в Steam: правой кнопкой по R.E.P.O. → " +
            "«Управление» → «Просмотреть локальные файлы»).",
        ["problem.workspace-invalid"] = "Эта папка не подходит.",
        ["problem.workspace-not-ascii"] =
            "В пути должны быть только латинские буквы и цифры: Unity ломается на других символах, например на " +
            "русских буквах в названии папки. Подойдёт что-то вроде D:\\RepoQuest.",
        ["problem.workspace-too-long"] =
            "Слишком длинный путь: инструменты сборки в Windows на таких ломаются. Выберите покороче, например " +
            "D:\\RepoQuest.",
        ["problem.workspace-in-game"] =
            "Рабочая папка не может быть внутри папки игры. Выберите другое место, например D:\\RepoQuest.",
        ["problem.workspace-program-files"] =
            "Папку Program Files защищает Windows. Выберите другое место, например D:\\RepoQuest.",


        ["headset.title"] = "Шлем",
        ["headset.quest"] = "Meta Quest 3 / 3S",
        ["headset.pico"] = "Pico 4 / 4 Ultra",
        ["headset.picoAbout"] =
            "Версия для Quest, переделанная для Pico программой ovrport (бесплатная, лицензия GPL). Проверено на Pico 4. " +
            "При первом запуске игра около минуты показывает «Preparing graphics», а предметы появляются по одному: " +
            "иначе Pico 4 зависает.",
        ["build.title"] = "Сборка",
        ["build.about"] =
            "Первая сборка займёт 1–1,5 часа на быстром компьютере и 3–5 часов на медленном, плюс время на " +
            "загрузки: Unity один раз компилирует шейдеры и данные игры. Следующие сборки используют готовое и идут " +
            "от 20 минут до часа. Компьютером в это время можно пользоваться.",
        ["build.lastApk"] = "Последний APK: {0}",
        ["build.running"] = "Идёт сборка… {0}",
        ["build.stopping"] = "Останавливаем…",

        ["step.unity-setup.title"] = "Unity",
        ["step.unity-setup.about"] = "Движок, который собирает версию для Quest. Ставится один раз, около 15 ГБ.",
        ["step.setup.title"] = "Инструменты",
        ["step.setup.about"] = "Python и вспомогательные скрипты — внутри рабочей папки.",
        ["step.export.title"] = "Файлы игры",
        ["step.export.about"] =
            "Переносит ресурсы из вашей копии игры в проект Unity. Повторяется только после обновлений игры.",
        ["step.bake.title"] = "VR-мод",
        ["step.bake.about"] = "Встраивает VR-мод RepoXR в код игры.",
        ["step.project.title"] = "Проект для Quest",
        ["step.project.about"] = "Настраивает проект под шлем: графика, управление, VR.",
        ["step.unity.title"] = "APK",
        ["step.unity.about"] = "Unity импортирует всё и собирает игру для Quest. Самый долгий шаг.",

        ["step.pico.title"] = "Версия для Pico",
        ["step.pico.about"] = "ovrport переделывает APK для Pico: его VR-среда и контроллеры. Несколько минут.",
        ["stage.export.download"] = "Скачиваем AssetRipper",
        ["stage.export.load"] = "Читаем файлы игры",
        ["stage.export.export"] = "Переносим ресурсы",
        ["stage.bake.download"] = "Скачиваем RepoXR и библиотеки",
        ["stage.bake.bake"] = "Встраиваем мод в игру",
        ["stage.project.download"] = "Скачиваем нужные файлы",
        ["stage.project.project"] = "Готовим проект",
        ["stage.project.done"] = "Завершаем",
        ["stage.unity.import"] = "Импортируем ресурсы",
        ["stage.unity.scripts"] = "Компилируем скрипты",
        ["stage.unity.scenes"] = "Собираем уровни",
        ["stage.unity.shaders"] = "Компилируем шейдеры",
        ["stage.unity.il2cpp"] = "Компилируем код игры",
        ["stage.unity.gradle"] = "Упаковываем APK",
        ["stage.pico.download"] = "Скачивание Java и ovrport",
        ["stage.pico.patch"] = "Переделка APK для Pico",
        ["stage.unity-setup.hub-download"] = "Скачиваем Unity Hub",
        ["stage.unity-setup.hub-install"] = "Устанавливаем Unity Hub",
        ["stage.unity-setup.editor"] = "Скачиваем Unity",
        ["stage.unity-setup.editor-install"] = "Устанавливаем Unity",

        ["status.Pending"] = "Ожидает",
        ["status.Running"] = "Идёт…",
        ["status.Done"] = "Готово",
        ["status.UpToDate"] = "Уже готово",
        ["status.Failed"] = "Ошибка",
        ["status.Cancelled"] = "Отменено",

        ["button.build"] = "Собрать APK",
        ["button.cancelBuild"] = "Отмена",
        ["button.install"] = "Установить на шлем",
        ["button.openApkFolder"] = "Открыть папку с APK",
        ["button.report"] = "Собрать отчёт",
        ["button.checkUpdates"] = "Проверить обновления",
        ["button.uninstall"] = "Удалить…",

        ["success.title"] = "APK готов!",
        ["success.noShare"] = "Не делитесь этим APK: в нём файлы игры. Каждый игрок собирает свой.",

        ["log.title"] = "Журнал",
        ["log.debug"] = "Показывать всё (для поиска проблем)",
        ["log.copy"] = "Копировать всё",

        ["update.title"] = "Обновления",
        ["update.latest"] = "У вас последняя версия.",
        ["update.notConfigured"] = "Эта копия сборщика не проверяет обновления.",

        ["consent.title"] = "Перед первой сборкой",
        ["consent.intro"] =
            "Чтобы собрать игру, сборщик скачает и установит эти программы. Все они бесплатные, у каждой — своя лицензия.",
        ["consent.download"] = "Загрузка: около {0}",
        ["consent.unity-hub.title"] = "Unity Hub",
        ["consent.unity-hub.about"] = "Программа Unity, которая устанавливает и запускает редактор.",
        ["consent.unity-editor.title"] = "Unity {0} с поддержкой Android (SDK, NDK, OpenJDK)",
        ["consent.unity-editor.about"] = "Движок, который собирает игру для Quest. Займёт около 15 ГБ на диске.",
        ["consent.android.title"] = "Android SDK и NDK",
        ["consent.android.about"] = "Инструменты Google для создания Android-приложений. Их установит Unity Hub.",
        ["consent.assetripper.title"] = "AssetRipper {0}",
        ["consent.assetripper.about"] = "Превращает файлы игры в проект Unity. Лицензия GPL.",
        ["consent.python.title"] = "Python {0} с пакетами",
        ["consent.python.about"] = "Запускает скрипты сборки. Ставится только внутрь рабочей папки.",
        ["consent.java.title"] = "Java {0} (Eclipse Temurin)",
        ["consent.java.about"] = "Запускает ovrport. Только внутри рабочей папки. Лицензия GPL с исключением Classpath.",
        ["consent.ovrport.title"] = "ovrport {0}",
        ["consent.ovrport.about"] = "Переделывает APK для Quest в APK для Pico. Лицензия GPL.",
        ["consent.long-paths.title"] = "Длинные пути к файлам в Windows",
        ["consent.long-paths.about"] =
            "Системная настройка, без которой сборка не работает. Windows попросит права администратора.",
        ["consent.signIn"] =
            "Одно действие — за вами: войти в Unity Hub с бесплатной учётной записью Unity. Сборщик подскажет, " +
            "когда до этого дойдёт.",
        ["consent.agree"] = "Я принимаю условия этих лицензий",
        ["consent.accept"] = "Принять",

        ["remove.title"] = "Удаление",
        ["remove.intro"] = "Выберите, что удалить. Прежде чем что-то удалять, сборщик скопирует ваши сохранения на компьютер.",
        ["remove.headset.title"] = "Игра на шлеме",
        ["remove.headset.about"] =
            "Шлем должен быть подключён кабелем. Сохранения, настройки и вход в Steam сначала копируются на рабочий " +
            "стол (папка «REPO Quest saves»).",
        ["remove.workspace.title"] = "Файлы сборщика в {0}",
        ["remove.workspace.about"] =
            "Проект игры, APK, скачанные инструменты (AssetRipper, Python) и логи. Копии сохранений со шлема в папке " +
            "backups останутся.",
        ["remove.defender.title"] = "Исключение в Защитнике Windows",
        ["remove.defender.about"] = "Папка {0} снова будет проверяться. Windows попросит права администратора.",
        ["remove.unity"] =
            "Unity Hub и Unity {0} поставили установщики самой Unity. Если Unity вам больше ни для чего не нужна, " +
            "удалите их в «Параметры Windows» → «Приложения» → «Установленные приложения».",
        ["remove.unity.open"] = "Открыть «Установленные приложения»",
        ["remove.exe"] = "Сам себя сборщик удалить не может: после этого удалите RepoQuestBuilder.exe и его папку.",
        ["remove.button"] = "Удалить",
        ["remove.uninstall"] = "Удаляем игру со шлема…",
        ["remove.removing"] = "Удаляем файлы сборщика…",
        ["remove.done.title"] = "Удалено",
        ["remove.done.headset"] = "Игра удалена со шлема. Сохранений на нём не было.",
        ["remove.done.headsetSaves"] = "Игра удалена со шлема. Сохранения лежат в {0}.",
        ["remove.done.workspace"] = "Файлы сборщика удалены.",
        ["remove.done.defender"] = "Исключение в Защитнике убрано.",
        ["remove.done.kept"] = "Оставлено: {0} (копии сохранений со шлема). Если они не нужны, удалите папку сами.",
        ["remove.done.failed"] = "Не удалось удалить:\n{0}",
        ["remove.done.exe"] = "Сейчас сборщик закроется. Чтобы закончить, удалите RepoQuestBuilder.exe и его папку.",

        ["defender.title"] = "Ускорить сборку",
        ["defender.text"] =
            "Защитник Windows проверяет каждый файл, который пишет сборка, а их десятки тысяч. Если исключить рабочую " +
            "папку {0} из проверки, сборка обычно идёт быстрее. В папке только проект игры и инструменты, которые " +
            "сборщик скачал и проверил. Windows попросит права администратора.",
        ["defender.undo"] =
            "Отменить можно в «Безопасность Windows» → «Защита от вирусов и угроз» → «Управление настройками» → " +
            "«Исключения». Сборщик спросит только один раз.",
        ["defender.yes"] = "Исключить папку",
        ["defender.no"] = "Не надо",

        ["notice.title"] = "Игра с друзьями",
        ["notice.offline"] =
            "Версия для Quest сразу работает без интернета. Чтобы играть с друзьями на ПК, войдите в Steam прямо в " +
            "шлеме: главное меню → кнопка Steam.",
        ["notice.risk"] =
            "Для входа используется неофициальный клиент Steam (SteamKit2). Правила Steam формально запрещают " +
            "сторонние клиенты: риск для аккаунта небольшой, но он есть.",
        ["notice.privacy"] = "Пароль уходит только в Steam. Сборщик больше никуда ничего не отправляет.",
        ["notice.ok"] = "Понятно",

        ["license.title"] = "Unity нужна бесплатная лицензия",
        ["license.failed"] = "Unity отказалась запускаться без лицензии.",
        ["license.intro"] =
            "Unity бесплатна, но работает только после входа в учётную запись Unity. Это займёт пару минут:",
        ["license.step1"] = "Откройте Unity Hub (кнопка ниже).",
        ["license.step2"] = "Войдите или создайте бесплатную учётную запись Unity.",
        ["license.step3"] =
            "Если Hub попросит лицензию: Preferences → Licenses (Manage licenses) → Add → «Get a free personal license».",
        ["license.step4"] = "Не устанавливайте редактор, который предложит Hub (Unity 6): он не нужен.",
        ["license.step5"] = "Вернитесь сюда и нажмите «Проверить снова».",
        ["license.openHub"] = "Открыть Unity Hub",
        ["license.checkAgain"] = "Проверить снова",

        ["untested.title"] = "Непроверенная версия игры",
        ["untested.text"] =
            "Ваша версия игры ({0}) ещё не проверялась с этим сборщиком. Сборка может не получиться, а игра в шлеме — " +
            "работать со странностями. Обычно поддержка скоро появляется в обновлении сборщика: сначала проверьте " +
            "обновления.",
        ["untested.build"] = "Всё равно собрать",

        ["cancel.title"] = "Остановить сборку?",
        ["cancel.text"] = "Готовые шаги сохранятся: следующая сборка продолжит с того же места.",
        ["cancel.stop"] = "Остановить",
        ["cancel.continue"] = "Продолжить сборку",

        ["close.title"] = "Закрыть сборщик?",
        ["close.text"] = "Сборщик ещё работает. Если закрыть окно, работа прервётся; готовые шаги сборки сохранятся.",
        ["close.stop"] = "Прервать и закрыть",
        ["close.keep"] = "Не закрывать",

        ["error.title"] = "Что-то пошло не так",
        ["error.reportHint"] = "Если будете просить помощи, приложите отчёт: в нём только логи, без файлов игры.",
        ["error.report"] = "Собрать отчёт",
        ["error.installInHub"] = "Установить через Unity Hub",
        ["error.installInHubHint"] =
            "Unity Hub откроет окно установки нужной версии Unity. Отметьте «Android Build Support» вместе с " +
            "«OpenJDK» и «Android SDK & NDK Tools», установите, а потом снова нажмите «Собрать APK».",
        ["error.unexpected"] = "Непредвиденная ошибка: {0}",

        ["err.download"] =
            "Не удалось скачать файл. Проверьте интернет и попробуйте ещё раз — сборка продолжится с того же места.",
        ["err.download-hash"] =
            "Скачанный файл повреждён (не совпала контрольная сумма). Попробуйте ещё раз; если повторится — соберите отчёт.",
        ["err.assetripper-start"] =
            "Не запустился AssetRipper — программа, которая читает файлы игры. Возможно, его блокирует антивирус: " +
            "попробуйте ещё раз или добавьте рабочую папку в исключения антивируса.",
        ["err.assetripper"] =
            "AssetRipper остановился, не дочитав файлы игры. Попробуйте ещё раз; если повторится — соберите отчёт.",
        ["err.export-empty"] =
            "Не получилось извлечь файлы игры. Проверьте целостность игры в Steam («Свойства» → «Установленные " +
            "файлы» → «Проверить целостность файлов игры») и попробуйте снова.",
        ["err.bake-failed"] =
            "Эта версия игры пока не поддерживается: VR-мод к ней не подходит. Дождитесь обновления сборщика — он сам " +
            "проверяет их при запуске.",
        ["err.bake-tool"] =
            "Сборщик не смог встроить VR-мод на этом компьютере — дело не в версии игры. Соберите отчёт и отправьте " +
            "его нам.",
        ["err.unity-missing"] =
            "Unity не установлена. Установите в Unity Hub версию 2022.3.62f3 с модулем Android Build Support (SDK, NDK " +
            "и OpenJDK) и запустите сборку снова.",
        ["err.unity-license"] =
            "Unity отказалась работать без лицензии. Войдите в Unity Hub, добавьте бесплатную лицензию Personal и " +
            "запустите сборку снова.",
        ["err.disk-full"] =
            "Диск заполнен. Освободите место (первой сборке нужно около 40 ГБ) и запустите сборку снова — она " +
            "продолжится с того же места.",
        ["err.disk-space"] =
            "В рабочей папке мало места: первой сборке нужно около 40 ГБ, следующим — около 6 ГБ. Освободите место " +
            "или выберите рабочую папку на другом диске.",
        ["err.disk-space-unity"] =
            "Не хватает места для Unity (около 20 ГБ) ни на системном диске, ни на диске с рабочей папкой. " +
            "Освободите место и попробуйте снова.",
        ["err.unity-android"] =
            "У Unity не хватает модулей для Android. В Unity Hub откройте Installs → Unity 2022.3.62f3 → Add modules " +
            "и добавьте Android Build Support вместе с OpenJDK и Android SDK & NDK Tools.",
        ["err.unity-compile"] =
            "Код проекта не скомпилировался. Это ошибка сборщика, а не ваша: соберите отчёт и отправьте его нам.",
        ["err.unity-failed"] =
            "Unity не смогла собрать APK. Попробуйте ещё раз; если повторится — соберите отчёт и отправьте его нам.",
        ["err.unity-install-failed"] =
            "Unity Hub не смог установить Unity сам. Установите её через окно Unity Hub — кнопка ниже.",
        ["err.hub-feed"] = "Не удалось связаться с сервером загрузок Unity. Проверьте интернет и попробуйте снова.",
        ["err.hub-hash"] = "Установщик Unity Hub скачался с ошибками. Попробуйте ещё раз.",
        ["err.hub-signature"] =
            "Скачанный установщик Unity Hub не подписан Unity, поэтому мы его не запустили. Попробуйте позже или " +
            "установите Unity Hub с сайта unity.com сами.",
        ["err.hub-uac"] =
            "Для установки Unity Hub нужны права администратора, а запрос Windows отклонили. Запустите сборку снова " +
            "и нажмите «Да», когда Windows спросит.",
        ["err.hub-install"] = "Не удалось установить Unity Hub. Установите его с сайта unity.com и запустите сборку снова.",
        ["err.quest-unauthorized"] =
            "Шлем ещё не разрешил отладку по USB. Наденьте его, подтвердите запрос (отметьте «Always allow from this " +
            "computer») и попробуйте снова.",
        ["err.quest-offline"] = "Шлем не отвечает. Переподключите кабель, разбудите шлем и попробуйте снова.",
        ["err.quest-storage"] =
            "На шлеме не хватает места: игре нужно около 4 ГБ. Удалите что-нибудь со шлема и попробуйте снова.",
        ["err.quest-install"] =
            "Не удалось установить игру на шлем. Переподключите шлем и попробуйте снова; ответ шлема — в подробностях.",
        ["err.quest-backup"] =
            "Не получилось скопировать сохранения со шлема, поэтому на нём ничего не меняли. Переподключите шлем и " +
            "попробуйте снова.",
        ["err.quest-restore"] =
            "Игра установлена, но сохранения не удалось вернуть на шлем. Они в безопасности на этом компьютере, в " +
            "рабочей папке (backups); попробуйте установить ещё раз.",
        ["err.adb-missing"] =
            "Программа для установки на шлем (adb) ставится вместе с Unity, а её пока нет. Сначала соберите APK — " +
            "или установите его через SideQuest или Meta Quest Developer Hub.",
        ["err.pico-convert"] =
            "Не получилось переделать APK для Pico. Причина — в подробностях и в logs\\ovrport.log рабочей папки; " +
            "«Собрать отчёт» соберёт их.",
        ["err.update-signature"] = "Обновление не подписано авторами сборщика, поэтому мы его не установили.",
        ["err.update-offline"] = "Не удалось проверить обновления. Проверьте подключение к интернету.",
        ["err.game-missing"] = "Игра не найдена. Укажите папку игры — ту, где лежит REPO.exe.",

        ["install.searching"] = "Ищем шлем…",
        ["install.stop"] = "Закрываем игру на шлеме…",
        ["install.install"] = "Устанавливаем на шлем (минуту-другую)…",
        ["install.backup"] = "Копируем сохранения со шлема…",
        ["install.uninstall"] = "Удаляем старую версию…",
        ["install.restore"] = "Возвращаем сохранения на шлем…",
        ["install.done.title"] = "Игра установлена на шлем",
        ["install.done.text"] =
            "В шлеме она в библиотеке: Library → фильтр справа вверху → Unknown Sources («Неизвестные источники»).",
        ["install.done.reinstalled"] =
            "Старая копия была подписана на другом компьютере, поэтому обновить её поверх не получилось: игру " +
            "переустановили, а сохранения и настройки перенесли обратно.",
        ["install.none.title"] = "Шлем не найден",
        ["install.none.intro"] = "Подключите шлем к компьютеру:",
        ["install.none.step1"] =
            "Включите режим разработчика для шлема в приложении Meta Horizon на телефоне (настройки шлема → " +
            "Developer Mode).",
        ["install.none.step1Pico"] =
            "Включите режим разработчика на Pico: Настройки → Общие → О устройстве, 7 раз нажмите «Версия ПО»; " +
            "затем Настройки → Разработчик → Отладка по USB.",
        ["install.none.step2"] = "Подключите шлем USB-C-кабелем, который передаёт данные (некоторые кабели только заряжают).",
        ["install.none.step3"] = "Наденьте шлем и разрешите отладку по USB (Allow USB debugging).",
        ["install.none.drivers"] = "В Windows также установите драйвер Meta «Oculus ADB Drivers»:",
        ["install.none.alternative"] = "Или установите APK сами — через SideQuest или Meta Quest Developer Hub.",
        ["install.none.alternativePico"] = "APK можно поставить и самому — через adb или SideQuest.",
        ["install.unauthorized.title"] = "Разрешите отладку по USB",
        ["install.unauthorized.text"] =
            "Шлем подключён, но ещё не доверяет этому компьютеру. Наденьте его и подтвердите запрос «Allow USB " +
            "debugging?» (отметьте «Always allow from this computer»).",
        ["install.pick.title"] = "Выберите шлем",
        ["install.pick.text"] = "Подключено несколько устройств. На какое установить игру?",
        ["install.pick.button"] = "Установить",
        ["install.deviceUnknown"] = "Android-устройство",
    };
}
