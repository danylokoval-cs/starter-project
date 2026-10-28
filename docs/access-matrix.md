# Матриця контролю доступу — ресурс `Incident` (baseline 2-A)

Ресурс — `Incident` з дочірніми `IncidentComment` і `IncidentStatusHistory`. Власник — `Incident.OwnerUserId`.
Суб'єкт береться лише з перевіреного principal (cookie Identity): claim `NameIdentifier` і роль.
Рішення сервера = суб'єкт + дія + ресурс + контекст (поточний `Status` ресурсу).

Позначки: `allow` — дозволено; `deny, NNN` — заборонено зі статусом відповіді NNN; `own` — лише власні записи.

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник ресурсу має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | own: лише власні інциденти | allow: усі інциденти (для аналізу) | own: лише власні (адміністративних прав над чужими немає) | Так для Reporter і Administrator | Реалізовано: `IncidentQueries.GetListAsync` + `IncidentAccess.VisibleTo`; тест A-05 |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | own | allow: усі | own | Так для Reporter і Administrator | Реалізовано: те саме `VisibleTo` у запиті пошуку; тест A-11 |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | own: підраховуються лише власні | allow: усі | own | Так для Reporter і Administrator | Реалізовано: `VisibleTo` до групування; тест A-11 |
| Перегляд деталей `GET /api/incidents/{id}` | достатній (головна action) | deny, 401 | owner — allow; non-owner — deny, 404 | allow (для аналізу) | owner — allow; чужий — deny, 404 | Так | Реалізовано: resource-based handler, операція `read`; тести A-02, A-03, A-03R, A-10 |
| Створення інциденту `POST /api/incidents` | успадкований baseline | deny, 401 | allow for self: власник = principal | allow for self | allow for self | Сервер призначає власника | Не змінювалось (ЛР 3); перевірено тестами `ProtectedCreateTests` |
| Update content `PUT /api/incidents/{id}` | добрий | deny, 401 | owner і `Status = New` — allow, 204; non-owner — deny, 404; owner, але `Status` не `New` — deny, 403 | deny, 403 (читає, але вміст не змінює) | як власник: лише власні зі статусом `New`; чужий — deny, 404 | Так | Реалізовано: операція `update`; тести A-04, A-08a, A03R |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner і `Status = New` — allow, 204; non-owner — deny, 404; owner, але не `New` — deny, 403 | deny, 403 | як власник: лише власні зі статусом `New`; чужий — deny, 404 | Так | Реалізовано: операція `delete`; тести A-04, `Delete_OwnNewIncident…`, `UpdateAndDelete_AfterTriage…` |
| Зміна status `POST /api/incidents/{id}/triage` | добрий | deny, 401 | deny: власний — 403, чужий — 404 | allow лише для переходу `New → Triaged`; інший поточний status — 409 | deny: чужий — 404, власний — 403 | Ні (право дає роль, а не володіння) | Реалізовано: операція `triage`; сервер сам визначає наступний status, тіло запиту не читається; тести A-06, A-07, A-09 |
| Зміна severity | добрий | deny, 401 | deny | deny | deny | — | endpoint-у немає: змінити severity неможливо; documented deny |
| Перегляд internal comment `GET /api/incident-comments/{id}` | добрий | deny, 401 | deny, 404 (навіть для власника інциденту) | allow після перевірки доступу до батьківського інциденту | deny, 404 | Ні для internal rule; доступ до parent incident перевіряється окремо | Реалізовано: операція `read-internal-comments`; тест A-12 |
| Перегляд публічного коментаря `GET /api/incident-comments/{id}` | добрий | deny, 401 | owner parent — allow; non-owner — deny, 404 | allow | як власник parent; чужий — deny, 404 | Так (через parent incident) | Реалізовано: `Read` батьківського інциденту; тест A-12 |
| Export/audit | відмінний, лише якщо flow існує | — | — | — | — | — | Не реалізовано: окремого flow у проєкті немає; події `authorization.denied` пишуться лише в журнал сервера |

## Політика відповідей

| Стан запиту | Відповідь |
|---|---|
| Немає автентифікації | `401 Unauthorized`, без HTML redirect (cookie-профіль ЛР 3) |
| Ресурс не існує **або** принципал не має права його читати | `404 Not Found` з однаковим `Problem Details`: `title` «Інцидент не знайдено» (для коментарів — «Коментар не знайдено»), без `detail`; відрізняються лише `traceId` |
| Принципал може читати ресурс, але не має права на дію | `403 Forbidden` (існування ресурсу йому вже відоме) |
| Дія дозволена ролі, але поточний стан ресурсу не підходить | `409 Conflict` (повторний triage) |

Чому 404 для чужого ресурсу: факт існування чужого інциденту сам є інформацією (перебір id), тому
«не існує» і «не доступний вам» не відрізняються ні статусом, ні `Content-Type`, ні тілом (A-10).
`403` застосовується лише там, де принципал уже бачить ресурс (власник або Analyst).
Приховане дочірнє значення (internal comment) також повертає `404`.

## Правило видимості

`IncidentAccess.VisibleTo(viewer)` — єдине визначення: `isAnalyst || incident.OwnerUserId == userId`.
Його використовують і запити list/search/severity-summary (умова в `WHERE`), і handler для операції
`read`, тому ці шляхи не можуть розійтися.

## Least privilege

- Reporter: лише власні записи; редагувати й видаляти може лише доки інцидент `New`.
- Analyst: читає все й виконує лише перехід `New → Triaged`; не змінює вміст і не видаляє.
- Administrator: роль сама по собі не дає доступу до чужого вмісту; окремих адміністративних операцій над `Incident` у baseline немає.
- Роль береться із claim-а, виданого сервером під час login; жодне поле тіла запиту (`role`, `status`, `ownerUserId`) ролі не змінює.
