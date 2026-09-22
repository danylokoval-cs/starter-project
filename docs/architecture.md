# Карта архітектури

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити (`fetch`), безпечно показує відповідь через DOM API (`textContent`, `document.createTextNode`, `createElement`) |
| Presentation | `Presentation/Endpoints/`, `Presentation/Contracts/` | Реєструє endpoints через Minimal API, читає зовнішні параметри (`id`, `status`), валідує їх за allowlist, формує HTTP-відповідь у вигляді типізованого response DTO |
| Application | `Application/Incidents/IncidentQueries.cs` | Виконує сценарії отримання списку, деталей інциденту та підсумку за критичністю (`GetListAsync`, `GetDetailsAsync`, `GetSeveritySummaryAsync`) |
| Data | `Data/SecureLabDbContext.cs`, `Data/Entities/` | Відображає C#-сутності на таблиці PostgreSQL через EF Core/Npgsql, зокрема `Incident.Severity`/`Status` як `text`-колонки через `HasConversion<string>()` |
| PostgreSQL | `infra/compose.yaml`, контейнер `postgres` | Зберігає навчальні дані (seed) у локальному контейнері, доступному на порту 54329 |

## Реалізований наскрізний маршрут

Обраний для опису маршрут — самостійно реалізована точка розширення
`GET /api/incidents/severity-summary` (Етап 3 лабораторної роботи). У
baseline цей маршрут повертав `501 Not Implemented`; після реалізації
повертає `200 OK` з підсумком кількості інцидентів за рівнем критичності.

```text
клік кнопки «Показати підсумок» у Client/index.html
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync (Presentation/Endpoints/IncidentEndpoints.cs)
  → allowlist-валідація status (Enum.TryParse + Enum.IsDefined)
  → IncidentQueries.GetSeveritySummaryAsync (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents, AsNoTracking().GroupBy(Severity)
  → PostgreSQL: SELECT severity, COUNT(*) FROM incidents GROUP BY severity
  → IncidentSeveritySummaryResponse[] (Presentation/Contracts/IncidentResponses.cs)
  → JSON response (200 OK або 400 Validation Problem Details)
  → textContent/createElement у #summary-list (Client/app.js)
```

Додатково, для порівняння, під час Етапу 2 було досліджено вже наявний
маршрут `GET /api/incidents/{id}` (деталі одного інциденту) — його
наскрізна схема аналогічна, але завершується проєкцією в
`IncidentDetailsResponse` та рендером у секції `#incident-details`
замість `#summary-list`.

## Ключові файли за рівнями

| Рівень | Файл | Роль у маршруті severity-summary |
|---|---|---|
| Client (розмітка) | `Client/index.html` | Кнопка `#summary-button`, статус `#summary-status`, контейнер `#summary-list` |
| Client (логіка) | `Client/app.js` | Функція `loadSeveritySummary` — виклик `apiFetch`, стани завантаження/порожньо/помилка, безпечний DOM-рендер |
| Presentation (endpoint) | `Presentation/Endpoints/IncidentEndpoints.cs` | Реєстрація маршруту, валідація `status`, виклик query, формування `Results.Ok`/`Results.ValidationProblem` |
| Presentation (контракт) | `Presentation/Contracts/IncidentResponses.cs` | `IncidentSeveritySummaryResponse(string Severity, int Count)` |
| Application | `Application/Incidents/IncidentQueries.cs` | `GetSeveritySummaryAsync` — `AsNoTracking`, `GroupBy`, проєкція, сортування за явним рейтингом критичності, структурований лог |
| Data | `Data/SecureLabDbContext.cs`, `Data/Entities/Incident.cs` | `DbSet<Incident> Incidents`, `ToTable("incidents")`, конфігурація `Severity`/`Status` як `text` |
| Тести/сценарії | `tests/http/incidents.http` | Сценарії успіху, порожнього результату (`?status=Resolved`) та некоректного значення (`?status=Unknown`) |

## Межі довіри

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач керує лише кліком по кнопці, але будь-який HTTP-клієнт (curl, Postman, змінений fetch у консолі) може звернутися до того самого endpoint напряму, в обхід UI | Тому вся перевірка `status` продубльована на сервері, а не лише у формі клієнта |
| Browser client → API | Значення `status` у query string передається як звичайний рядок і може містити будь-що, включно з довільним текстом чи значенням поза enum | Явний allowlist через `Enum.TryParse` + `Enum.IsDefined` в `IncidentEndpoints.cs`, повертає `400 Validation Problem Details` для некоректного значення |
| API → PostgreSQL | Значення `status` та `id`, що потрапляють у SQL-запит, не можна безпечно підставляти в текст запиту напряму | Усі запити побудовано через параметризований LINQ-провайдер EF Core (Npgsql), без конкатенації рядків; операції читання додатково позначено `AsNoTracking()` |
| PostgreSQL → API → DOM | У базі даних зберігається раніше введений недовірений текст (наприклад, `description` з `<script>` у seed-даних) | Response DTO не містить внутрішніх полів (`OwnerUserId`, `email`, приватні коментарі); клієнт виводить лише через `textContent`/`document.createTextNode`/`createElement`, без `innerHTML` — підтверджено відображенням `<script>` як звичайного тексту (див. Рисунок 34 основного звіту) |

## Конфігураційні входи

- `global.json` — версія .NET SDK (смуга `10.0.3xx`, не нижче `10.0.302`, `rollForward: latestPatch`);
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string для Development-середовища;
- `infra/compose.yaml` та `infra/.env.example` — версія образу PostgreSQL, порт (`54329`) і локальні навчальні облікові дані (лише для `127.0.0.1`);
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм (наприклад, при зайнятому порту), без запису значення у Git.

## Відновлення відомого стану

Стенд повертається до відомого seed-стану командою
`dotnet run --no-build --project src/SecureLab.Api -- --reset-database`,
яка застосовує наявні migration, очищує навчальні таблиці та повторно
заповнює їх фіксованими seed-значеннями. Команда працює лише у явно
налаштованому Development-середовищі і є разовою службовою дією — після
виконання процес завершується, не запускаючи API. Відтворюваність
підтверджено повторним виконанням `GET /api/incidents?status=Triaged`
та `GET /api/incidents/severity-summary` після reset — обидва запити
повернули ті самі результати, що й до контрольованих експериментів
(див. Рисунки 46–48 основного звіту).