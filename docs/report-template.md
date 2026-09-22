# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: `lab/1-system`.
- Фінальний тег: `v0.1.0`.
- Commit hash: `TODO` (заповнити результатом `git rev-parse HEAD` після фінального commit; має збігатися з `git rev-list -n 1 v0.1.0`).

Гілка `lab/1-system` містить усі зміни цієї лабораторної роботи; після
завершення роботи її злито в основну гілку `main` командою
`git merge --no-ff`, а тег `v0.1.0` позначає саме злитий і повторно
перевірений стан `main`.

## 2. Змінений маршрут

Змінений (реалізований) маршрут — `GET /api/incidents/severity-summary`.
У baseline він повертав `501 Not Implemented`; після реалізації повертає
`200 OK` з підсумком кількості інцидентів за рівнем критичності.

```text
клік кнопки «Показати підсумок» (Client/index.html)
  → loadSeveritySummary (Client/app.js)
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetSeveritySummaryAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents, AsNoTracking().GroupBy(Severity)
  → PostgreSQL: SELECT severity, COUNT(*) FROM incidents GROUP BY severity
  → IncidentSeveritySummaryResponse[] (Presentation/Contracts/IncidentResponses.cs)
  → textContent/createElement у #summary-list
```

Ключові файли: `Presentation/Endpoints/IncidentEndpoints.cs`,
`Application/Incidents/IncidentQueries.cs`,
`Presentation/Contracts/IncidentResponses.cs`, `Client/index.html`,
`Client/app.js`, `tests/http/incidents.http`.

## 3. Виконані зміни

Реалізовано точку розширення на рівні «Добрий»:

Додано окремий іменований response DTO `IncidentSeveritySummaryResponse(string Severity, int Count)` без жодних полів сутності `Incident`, що не стосуються підсумку.

Реалізовано асинхронний query `GetSeveritySummaryAsync` за схемою `AsNoTracking()` → `GroupBy(Severity)` → проєкція одразу в DTO через `Select` → `ToListAsync(cancellationToken)`, без `.Result`/`.Wait()`. Обрано політику нульових груп «лише наявні групи» (без штучного доповнення нулями) та явний спадний порядок критичності `Critical → High → Medium → Low`, застосований у пам'яті вже після матеріалізації результату запиту. Політику обрано свідомо: у поєднанні з параметром `status` вона дає змогу отримати справжній порожній результат для статусів, яких немає в seed-даних (`Resolved`, `Closed`), без видалення навчальних даних заради демонстрації.

Замінено baseline-заглушку (`501`) в `IncidentEndpoints.cs` на окремий асинхронний endpoint-метод з `IncidentQueries` через dependency injection, `CancellationToken` та типізованою метаданою `.Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()`.

Самостійно додано необов'язковий query parameter `status`: сервер перевіряє значення за явним allowlist підтримуваних статусів через `Enum.TryParse` у поєднанні з `Enum.IsDefined` і повертає `400 Validation Problem Details` для некоректного значення.

Додано структурований лог результату операції (`{GroupCount}` груп, застосований `{Status}`) без чутливих даних.

У `Client/index.html` додано кнопку, елемент статусу та контейнер результату; у `Client/app.js` реалізовано асинхронну функцію `loadSeveritySummary` із чотирма станами: завантаження, успішний результат через `textContent`/`createElement`, окремий стан порожнього результату («Даних немає») та безпечна помилка в `catch` без стека виклику чи внутрішніх деталей. Ніде не використано `innerHTML`.

У `tests/http/incidents.http` оновлено коментар з очікування `501` на очікування `200` та додано окремі сценарії для порожнього (`?status=Resolved`) і некоректного (`?status=Unknown`) значення параметра безпосередньо на `severity-summary`.

## 4. Перевірка

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, API з'єднується з БД | 200, `{"status":"ready"}` | Рисунок 37 основного звіту |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, список за фільтром | 200, список відповідає фільтру | Рисунок 38 основного звіту |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200, `[]` | 200, `[]` | Рисунок 39 основного звіту |
| T-04 | відновлений seed | `GET /api/incidents/{неіснуючий id}` | 404 Problem Details | 404, коректний Problem Details з `traceId` | Рисунок 40 основного звіту |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | 400, коректний Validation Problem Details | Рисунок 41 основного звіту |
| T-06 | реалізовано Етап 3, відновлений seed | `GET /api/incidents/severity-summary` | 200; High, Medium, Low по одному, без Critical, порядок за контрактом | 200; `[{"severity":"High","count":1},{"severity":"Medium","count":1},{"severity":"Low","count":1}]` | Рисунок 42 основного звіту |
| T-06a | реалізовано Етап 3 | `GET /api/incidents/severity-summary?status=Resolved` | 200, `[]` (порожній сценарій, рівень «Добрий») | 200, `[]` | Рисунок 39 (аналогічний list-flow сценарій) основного звіту |
| T-06b | реалізовано Етап 3 | `GET /api/incidents/severity-summary?status=Unknown` | 400 Validation Problem Details (некоректний сценарій, рівень «Добрий») | 400, коректний Validation Problem Details | Рисунок 41 (аналогічний list-flow сценарій) основного звіту |
| T-07 | реалізовано Етап 3, API і клієнт запущено | клік кнопки підсумку в UI | безпечний рендер; для рівня «Добрий» — стани завантаження, порожнього результату та безпечної помилки | підтверджено: один мережевий запит `GET /api/incidents/severity-summary`, коректний DOM-рендер, усі 4 стани реалізовано в коді | Рисунки 35, 45 основного звіту |
| T-08 | після reset | `--reset-database`, повторити T-02 і T-06 | seed повертає стенд до відомого стану | результати повністю збіглися з T-02 і T-06 | Рисунки 46–48 основного звіту |
| — | після останньої зміни коду | `dotnet build` + `dotnet test` | успішна збірка, усі тести зелені | `Build succeeded`; `4 passed, 0 failed` | Рисунки 43–44 основного звіту |

## 5. Security-сценарій

Не застосовується для цієї лабораторної роботи: навмисно вразливий стан
у baseline варіанта 2-A для ЛР 1 відсутній (документ
`docs/variant-02-a.md` starter-проєкту прямо зазначає, що навмисно
вразливі стани передбачені для ЛР 2, 4 і 5, але не для ЛР 1).

## 6. Висновок

Точку розширення `GET /api/incidents/severity-summary` реалізовано на
рівні «Добрий» повністю: baseline-заглушку зі статус-кодом 501 замінено
на endpoint з окремим response DTO, асинхронним query на основі
`AsNoTracking`, `GroupBy` та `ToListAsync`, самостійно доданим і
провалідованим за allowlist параметром `status`, структурованим
журналюванням без чутливих даних та клієнтським інтерфейсом із чотирма
станами. Усі десять перевірок таблиці вище, включно з чотирма
HTTP-сценаріями для нової точки розширення (успіх, порожній і
некоректний результат безпосередньо на `severity-summary`, а також 404
для деталей інциденту) та повним набором автоматизованих тестів,
пройшли успішно. Повторна перевірка після `--reset-database`
підтвердила повну відтворюваність отриманих результатів.