REPO Quest Builder
==================

[EN] Builds a standalone VR version of R.E.P.O. for Meta Quest 2, Quest 3 or Pico 4 from your own copy of the game.
[RU] Сборщик автономной VR-версии R.E.P.O. для Meta Quest 2, Quest 3 или Pico 4 из вашей копии игры. (Русский текст ниже.)


ENGLISH
-------

The builder puts together R.E.P.O. with the RepoXR VR mod (by DaXcess, https://github.com/DaXcess/RepoXR) for Meta
Quest 2, Quest 3 or Pico 4 from your copy of the game on Steam. It all happens on your computer; the game's files are not sent anywhere.
Source code, new versions and issues: https://github.com/Dobutsu/repo-quest-vr

You need
- Windows 10 or 11 (64-bit), 16 GB of RAM (12 at least), about 60 GB of free disk space.
- R.E.P.O. from Steam, updated to the latest version (the one your friends play).
- A Meta Quest 2, Quest 3 or Pico 4 in developer mode and a USB cable to install the game.
- A free Unity account (once).

How to build
1. Unzip anywhere and run RepoQuestBuilder.exe.
   If Windows says "Windows protected your PC", click "More info" → "Run anyway".
2. Check that the builder found the game, or choose the game's folder yourself. Pick your headset: Meta Quest
   (the default, for Quest 2, 3 and 3S) or Pico 4.
3. Click "Build APK" and accept the licenses. The builder downloads Unity and everything else. It also offers to
   exclude its working folder from Windows Defender scanning, which makes builds faster; you can say no.
4. When asked, sign in to Unity Hub with a free Unity account, then come back to the builder.
5. The first build takes 1–1.5 hours on a fast computer and 3–5 hours on a slower one, plus the downloads: Unity
   compiles the game's shaders and data once. Leave it running; if you stop it, the next build continues from there.
6. Connect the headset with the cable and click "Install on the headset". On the headset, the game is in the Library under
   "Unknown Sources".

Updates
- When a new version comes out, the builder offers to update itself and rebuilds the APK (20 minutes to an hour).
- After the game itself updates on Steam, rebuild too: otherwise you cannot play with friends on PC.

Uninstall
- Click "Uninstall…" in the builder. It removes the game from the headset (your saves are copied to the desktop
  first), its working folder and the Defender exclusion. Unity Hub and Unity are removed in Windows Settings → Apps.
  Then delete the builder's folder.

Please note
- Do not share the APK: it contains the game's files. Everyone builds their own.
- Playing with friends: log in to Steam in the headset (main menu → Steam button). It is an unofficial Steam client:
  Steam's rules do not allow third-party clients; the risk for your account is small, but it is there.
- If something goes wrong, click "Create report" and send the zip from your desktop.

Developer mode on the Quest: Meta Horizon app on your phone → Devices → your headset → Headset settings → Developer
mode. It needs a free developer organization on developers.meta.com.
If the headset does not show up over USB, install the Oculus ADB Drivers:
https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/

Pico 4 / 4 Ultra: the builder makes the Quest version and converts it with ovrport (https://github.com/ovrport/app,
GPL-3.0), downloading ovrport and Java 17 for that. Tested on a Pico 4 and a Pico 4 Ultra. On the first start on a
Pico 4 the game shows "Preparing graphics" for about a minute while things appear one by one (Pico 4 hangs otherwise),
and it asks for the microphone in the main menu. Developer mode on the Pico: Settings → General → About, tap "Software version" 7 times, then
Settings → Developer → USB debugging.


РУССКИЙ
-------

Программа собирает R.E.P.O. с VR-модом RepoXR (автор DaXcess, https://github.com/DaXcess/RepoXR) для Meta Quest 2,
Quest 3 или Pico 4 из вашей копии игры в Steam. Сборка идёт на вашем компьютере, файлы игры никуда не отправляются.
Исходный код, новые версии и вопросы: https://github.com/Dobutsu/repo-quest-vr

Что нужно
- Windows 10 или 11 (64-бит), 16 ГБ памяти (минимум 12), около 60 ГБ свободного места на диске.
- R.E.P.O. из Steam, обновлённая до последней версии (как у друзей).
- Meta Quest 2, Quest 3 или Pico 4 в режиме разработчика и USB-кабель для установки.
- Бесплатный аккаунт Unity (понадобится один раз).

Как собрать
1. Распакуйте архив в любую папку и запустите RepoQuestBuilder.exe.
   Если Windows покажет «Система Windows защитила ваш компьютер», нажмите «Подробнее» → «Выполнить в любом случае».
2. Проверьте, что программа нашла игру, или укажите папку игры вручную. Выберите шлем: Meta Quest (по умолчанию,
   для Quest 2, 3 и 3S) или Pico 4.
3. Нажмите «Собрать APK» и примите лицензии. Программа сама скачает Unity и всё остальное. Ещё она предложит
   исключить свою рабочую папку из проверки Защитником Windows, чтобы сборка шла быстрее; можно отказаться.
4. Когда программа попросит, войдите в Unity Hub с бесплатным аккаунтом Unity и вернитесь в программу.
5. Первая сборка занимает 1–1,5 часа на быстром компьютере и 3–5 часов на медленном, плюс время на загрузки:
   Unity один раз компилирует шейдеры и данные игры. Не прерывайте её; если всё же остановите, следующая сборка
   продолжит с того же места.
6. Подключите шлем кабелем и нажмите «Установить на шлем». В шлеме игра появится в Библиотеке, раздел
   «Неизвестные источники».

Обновления
- Когда выйдет новая версия, программа предложит обновиться и пересоберёт APK (от 20 минут до часа).
- После обновления самой игры в Steam тоже нужно пересобрать, иначе не получится играть с друзьями на ПК.

Удаление
- Нажмите «Удалить…» в программе. Она удалит игру со шлема (сохранения сначала скопируются на рабочий стол), свою
  рабочую папку и исключение в Защитнике. Unity Hub и Unity удаляются в «Параметры Windows» → «Приложения». Потом
  удалите папку с программой.

Важно
- Не передавайте APK другим: в нём файлы игры. Каждый собирает свою копию сам.
- Игра с друзьями: в шлеме войдите в Steam (главное меню → кнопка Steam). Это неофициальный клиент Steam: правила
  Steam формально запрещают сторонние клиенты, риск для аккаунта небольшой, но он есть.
- Если что-то пошло не так, нажмите «Собрать отчёт» и пришлите архив с рабочего стола.

Режим разработчика на Quest: приложение Meta Horizon на телефоне → Устройства → ваш шлем → Настройки гарнитуры →
Режим разработчика. Для этого нужна бесплатная организация разработчика на developers.meta.com.
Если шлем не виден по кабелю, установите драйвер Oculus ADB Drivers:
https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/

Pico 4 / 4 Ultra: программа собирает версию для Quest и переделывает её программой ovrport
(https://github.com/ovrport/app, GPL-3.0), для этого скачивает ovrport и Java 17. Проверено на Pico 4 и Pico 4 Ultra.
При первом запуске на Pico 4 игра около минуты показывает «Preparing graphics», а предметы появляются по одному
(иначе Pico 4 зависает);
доступ к микрофону она спросит в главном меню. Режим разработчика на Pico: Настройки → Общие → О устройстве,
7 раз нажмите «Версия ПО», затем Настройки → Разработчик → Отладка по USB.
