REPO Quest Builder
==================

[EN] Builds a standalone VR version of R.E.P.O. for Meta Quest 3 from your own copy of the game.
[RU] Сборщик автономной VR-версии R.E.P.O. для Meta Quest 3 из вашей копии игры. (Русский текст ниже.)


ENGLISH
-------

The builder puts together R.E.P.O. with the RepoXR VR mod (by DaXcess, https://github.com/DaXcess/RepoXR) for Meta
Quest 3 from your copy of the game on Steam. It all happens on your computer; the game's files are not sent anywhere.
Source code, new versions and issues: https://github.com/Dobutsu/repo-quest-vr

You need
- Windows 10 or 11 (64-bit), 16 GB of RAM (12 at least), about 60 GB of free disk space.
- R.E.P.O. from Steam, updated to the latest version (the one your friends play).
- A Meta Quest 3 in developer mode and a USB cable to install the game.
- A free Unity account (once).

How to build
1. Unzip anywhere and run RepoQuestBuilder.exe.
   If Windows says "Windows protected your PC", click "More info" → "Run anyway".
2. Check that the builder found the game, or choose the game's folder yourself.
3. Click "Build APK" and accept the licenses. The builder downloads Unity and everything else.
4. When asked, sign in to Unity Hub with a free Unity account, then come back to the builder.
5. The first build takes 1–1.5 hours plus the downloads.
6. Connect the headset with the cable and click "Install on Quest". On the headset, the game is in the Library under
   "Unknown Sources".

Updates
- When a new version comes out, the builder offers to update itself and rebuilds the APK (about 40 minutes).
- After the game itself updates on Steam, rebuild too: otherwise you cannot play with friends on PC.

Please note
- Do not share the APK: it contains the game's files. Everyone builds their own.
- Playing with friends: log in to Steam in the headset (main menu → Steam button). It is an unofficial Steam client:
  Steam's rules do not allow third-party clients; the risk for your account is small, but it is there.
- If something goes wrong, click "Create report" and send the zip from your desktop.

Developer mode on the Quest: Meta Horizon app on your phone → Devices → your headset → Headset settings → Developer
mode. It needs a free developer organization on developers.meta.com.
If the headset does not show up over USB, install the Oculus ADB Drivers:
https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/


РУССКИЙ
-------

Программа собирает R.E.P.O. с VR-модом RepoXR (автор DaXcess, https://github.com/DaXcess/RepoXR) для Meta Quest 3
из вашей копии игры в Steam. Сборка идёт на вашем компьютере, файлы игры никуда не отправляются.
Исходный код, новые версии и вопросы: https://github.com/Dobutsu/repo-quest-vr

Что нужно
- Windows 10 или 11 (64-бит), 16 ГБ памяти (минимум 12), около 60 ГБ свободного места на диске.
- R.E.P.O. из Steam, обновлённая до последней версии (как у друзей).
- Meta Quest 3 в режиме разработчика и USB-кабель для установки.
- Бесплатный аккаунт Unity (понадобится один раз).

Как собрать
1. Распакуйте архив в любую папку и запустите RepoQuestBuilder.exe.
   Если Windows покажет «Система Windows защитила ваш компьютер», нажмите «Подробнее» → «Выполнить в любом случае».
2. Проверьте, что программа нашла игру, или укажите папку игры вручную.
3. Нажмите «Собрать APK» и примите лицензии. Программа сама скачает Unity и всё остальное.
4. Когда программа попросит, войдите в Unity Hub с бесплатным аккаунтом Unity и вернитесь в программу.
5. Первая сборка занимает 1–1,5 часа плюс время на загрузки.
6. Подключите шлем кабелем и нажмите «Установить на Quest». В шлеме игра появится в Библиотеке, раздел
   «Неизвестные источники».

Обновления
- Когда выйдет новая версия, программа предложит обновиться и пересоберёт APK (около 40 минут).
- После обновления самой игры в Steam тоже нужно пересобрать, иначе не получится играть с друзьями на ПК.

Важно
- Не передавайте APK другим: в нём файлы игры. Каждый собирает свою копию сам.
- Игра с друзьями: в шлеме войдите в Steam (главное меню → кнопка Steam). Это неофициальный клиент Steam: правила
  Steam формально запрещают сторонние клиенты, риск для аккаунта небольшой, но он есть.
- Если что-то пошло не так, нажмите «Собрать отчёт» и пришлите архив с рабочего стола.

Режим разработчика на Quest: приложение Meta Horizon на телефоне → Устройства → ваш шлем → Настройки гарнитуры →
Режим разработчика. Для этого нужна бесплатная организация разработчика на developers.meta.com.
Если шлем не виден по кабелю, установите драйвер Oculus ADB Drivers:
https://developers.meta.com/horizon/downloads/package/oculus-adb-drivers/
