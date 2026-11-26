REPO Quest Builder for Mac
==========================

[EN] Builds a standalone VR version of R.E.P.O. for Meta Quest 2, Quest 3 or Pico 4 from your own copy of the game.
[RU] Сборщик автономной VR-версии R.E.P.O. для Meta Quest 2, Quest 3 или Pico 4 из вашей копии игры. (Русский текст ниже.)


ENGLISH
-------

The builder puts together R.E.P.O. with the RepoXR VR mod (by DaXcess, https://github.com/DaXcess/RepoXR) for Meta
Quest 2, Quest 3 or Pico 4 from your copy of the game on Steam. It all happens on your Mac; the game's files are not
sent anywhere. Source code, new versions and issues: https://github.com/Dobutsu/repo-quest-vr

You need
- A Mac with Apple Silicon (M1 or newer), macOS 12 or later, 16 GB of RAM, about 60 GB of free disk space.
- R.E.P.O. on your Steam account. It has no Mac version, so the builder downloads the Windows one with SteamCMD,
  Valve's official command-line Steam client (or finds it in CrossOver/Whisky, or you choose its folder).
- A Meta Quest 2, Quest 3 or Pico 4 in developer mode and a USB-C cable to install the game.
- A free Unity account (once).

Opening the builder the first time
The builder is not notarized by Apple, so macOS refuses to open it at first:
1. Unzip, move RepoQuestBuilder.app to Applications (or anywhere) and double-click it. macOS says it cannot verify it.
2. Open System Settings → Privacy & Security, scroll down to "RepoQuestBuilder was blocked" and click "Open Anyway",
   then confirm. From then on it opens normally.
(Or in Terminal: xattr -dr com.apple.quarantine /Applications/RepoQuestBuilder.app)

How to build
1. If the builder did not find the game, click "Download with SteamCMD…" and enter your Steam account name. When
   SteamCMD asks, enter the password and the Steam Guard code (or approve the login in the Steam Mobile app). They go
   to Steam only; SteamCMD remembers the login, so game updates later ask for nothing. About 1.5 GB is downloaded.
   SteamCMD is an Intel program: if macOS asks to install Rosetta, agree.
2. There is nothing to pick for the headset: one APK runs on Quest 2, 3, 3S and Pico 4 / 4 Ultra.
3. Click "Build APK" and accept the licenses. The builder downloads Unity Hub, Unity and everything else.
4. When asked, sign in to Unity Hub with a free Unity account, then come back to the builder.
5. The first build takes about an hour on an M1 Pro or newer, plus the downloads. Leave it running; if you stop it, the
   next build continues from there.
6. Connect the headset with the cable and click "Install on the headset". On the headset, the game is in the Library
   under "Unknown Sources".

Updates
- When a new version comes out, the builder offers to update itself and rebuilds the APK.
- After the game updates on Steam, click "Download with SteamCMD…" again (it fetches only what changed) and rebuild:
  otherwise you cannot play with friends on PC.

Please note
- Do not share the APK: it contains the game's files. Everyone builds their own.
- Playing with friends: log in to Steam in the headset (main menu → Steam button). It is an unofficial Steam client:
  Steam's rules do not allow third-party clients; the risk for your account is small, but it is there.
- If something goes wrong, click "Create report" and send the zip from your desktop.


РУССКИЙ
-------

Программа собирает R.E.P.O. с VR-модом RepoXR (автор DaXcess, https://github.com/DaXcess/RepoXR) для Meta Quest 2,
Quest 3 или Pico 4 из вашей копии игры в Steam. Сборка идёт на вашем Mac, файлы игры никуда не отправляются.
Исходный код, новые версии и вопросы: https://github.com/Dobutsu/repo-quest-vr

Что нужно
- Mac на Apple Silicon (M1 или новее), macOS 12 или новее, 16 ГБ памяти, около 60 ГБ свободного места.
- R.E.P.O. на вашем аккаунте Steam. Версии для Mac у игры нет, поэтому билдер скачивает Windows-версию через
  SteamCMD, официальный консольный клиент Steam от Valve (или находит её в CrossOver/Whisky, или вы указываете папку).
- Meta Quest 2, Quest 3 или Pico 4 в режиме разработчика и кабель USB-C для установки.
- Бесплатный аккаунт Unity (понадобится один раз).

Первый запуск
Билдер не нотаризован Apple, поэтому сначала macOS откажется его открыть:
1. Распакуйте архив, перенесите RepoQuestBuilder.app в «Программы» (или куда угодно) и откройте двойным щелчком.
   macOS скажет, что не может проверить приложение.
2. Откройте Системные настройки → Конфиденциальность и безопасность, внизу у «RepoQuestBuilder заблокировано»
   нажмите «Всё равно открыть» и подтвердите. Дальше он открывается как обычно.
(Или в Терминале: xattr -dr com.apple.quarantine /Applications/RepoQuestBuilder.app)

Как собрать
1. Если билдер не нашёл игру, нажмите «Скачать через SteamCMD…» и введите имя аккаунта Steam. Когда SteamCMD спросит,
   введите пароль и код Steam Guard (или подтвердите вход в мобильном приложении Steam). Они уходят только в Steam;
   SteamCMD запоминает вход, и для обновлений игры потом ничего вводить не нужно. Скачается около 1,5 ГБ.
   SteamCMD — программа для Intel: если macOS предложит установить Rosetta, согласитесь.
2. Выбирать шлем не нужно: один APK работает на Quest 2, 3, 3S и Pico 4 / 4 Ultra.
3. Нажмите «Собрать APK» и примите лицензии. Программа сама скачает Unity Hub, Unity и всё остальное.
4. Когда попросят, войдите в Unity Hub с бесплатным аккаунтом Unity и вернитесь в программу.
5. Первая сборка идёт около часа на M1 Pro и новее, плюс загрузки. Не выключайте; если остановить, следующая сборка
   продолжит с того же места.
6. Подключите шлем кабелем и нажмите «Установить на шлем». В шлеме игра будет в Библиотеке, в «Неизвестных источниках».

Обновления
- Когда выйдет новая версия, программа предложит обновиться и пересоберёт APK.
- После обновления игры в Steam снова нажмите «Скачать через SteamCMD…» (скачается только изменённое) и пересоберите:
  иначе не получится играть с друзьями на ПК.

Важно
- Не раздавайте APK: в нём файлы игры. Каждый собирает свой.
- Игра с друзьями: войдите в Steam в шлеме (главное меню → кнопка Steam). Это неофициальный клиент Steam: правила
  Steam не разрешают сторонние клиенты; риск для аккаунта небольшой, но он есть.
- Если что-то пошло не так, нажмите «Создать отчёт» и пришлите zip с рабочего стола.
