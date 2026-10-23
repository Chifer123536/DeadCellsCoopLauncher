# Dead Cells Co-op Launcher

Кооператив в Dead Cells с друзьями через Steam — установка одной кнопкой.

## Как начать

1. Скачайте `DeadCellsCoopLauncher.exe` со страницы [Releases](https://github.com/Chifer123536/DeadCellsCoopLauncher/releases/latest).
2. Запустите. Если Windows покажет SmartScreen — «Подробнее» → «Выполнить в любом случае» (exe не подписан).
3. Нажмите «Установить». Лаунчер сам найдёт игру, поставит .NET 10, Visual C++, DCCM и мод.
4. Жмите «Играть» в Steam — откроется окно лаунчера: **«Играть в кооп»** или **«Обычная игра»**.

## Как играть с другом

1. Друг тоже ставит мод через этот лаунчер.
2. Хост: «Кооператив» → «Создать лобби» → «Пригласить друга» (или Shift+Tab в Steam).
3. Друг принимает приглашение в Steam — игра сама запустится в кооперативе и зайдёт в лобби.
4. Забег начинает хост. Сложность (Лёгкая / Стандарт / Хардкор) и набор DLC выбираются в лобби.

## Что делает лаунчер

- Вместо `deadcells.exe` ставит свою копию — по «Играть» в Steam открывается окно выбора режима;
  оригинал хранится как `deadcells.vanilla.exe`, «Обычная игра» запускает его без модов и без сети.
  «Запомнить выбор» убирает вопрос, Shift при запуске возвращает окно. Принятый инвайт сразу открывает кооператив.
- Перед каждым запуском кооператива из Steam тихо проверяет `manifest.json` и, если вышла новая версия мода,
  обновляет его с прогрессом в окне. Нет сети — запускает установленную версию.
- После обновления игры или «Проверки целостности» в Steam — кнопка «Починить».
- Сохранения кооператива отдельные (`MSave`), одиночные не трогаются.
- «Собрать логи» кладёт zip на рабочий стол — пришлите его, если что-то сломалось.
- «Удалить мод» возвращает оригинальную игру и удаляет `coremod`.

## Сборка

```
dotnet test tests/CoopLauncher.Core.Tests/CoopLauncher.Core.Tests.csproj -c Release
dotnet publish src/CoopLauncher.App/CoopLauncher.App.csproj -c Release -o artifacts/launcher
```

`manifest.json` в ветке `main` — источник версий для всех лаунчеров: мод, DCCM, сам лаунчер (url + sha256).

## Благодарности

- [DeadCellsMultiplayerMod](https://github.com/vaiserYT/DeadCellsMultiplayerMod) — vaiserYT (форк: [Chifer123536/DeadCellsMultiplayerMod](https://github.com/Chifer123536/DeadCellsMultiplayerMod))
- [Dead Cells Core Modding](https://github.com/dead-cells-core-modding/core)

Арт и логотип в окне берутся из кэша Steam на вашем ПК. Dead Cells — товарный знак Motion Twin / Evil Empire; проект неофициальный.

---

**EN:** one-click installer for Dead Cells co-op over Steam. Download the exe from Releases, press Install, then press Play in Steam and pick "Play co-op". Co-op launches from Steam update the mod automatically. The host creates a lobby and invites a friend through Steam; accepting the invite starts the game straight in co-op.
