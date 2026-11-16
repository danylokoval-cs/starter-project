# Власна карта source-to-sink

Поле: Incident.Description (основне) і IncidentComment.Text (другий потік). Стан: vulnerable commit a0443461115c520999d69f5421e6556323d5fd92 (scaffold – 1adc4bd3a3b8910de89a4a22e6da8e0fd14e19d1). Вхід: Alice, Guid запису aaf4928f-828e-4557-a1a5-7cb4f5882062.

## Потік 1: Incident.Description

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Client/browser-lab.html`, форма `#edit`, `textarea[name=description]` | текст, який вводить користувач | контрольоване користувачем |
| Request | `Client/browser-lab.js`, submit handler, `fetch POST /api/incidents` | JSON-поле `description` | Network, Payload; контрольоване користувачем |
| Validation/mapping | `Scaffolding/Lab02Endpoints.cs`, `CreateIncidentRequest` → `MapPost("/api/incidents")` | `Trim`, довжина ≤ 4000, обов'язкове | не стає trusted: правила формату не перетворюють рядок на безпечний HTML |
| Storage | `Data/Entities/Incident.cs`, `Description`; колонка `description` (`HasMaxLength(4000)` у `SecureLabDbContext`) | та сама строка | збережене значення користувача |
| Response | `Application/Incidents/IncidentQueries.GetDetailsAsync` → `Presentation/Contracts/IncidentDetailsResponse.Description`; `GET /api/incidents/{id}` | JSON-поле `description` | Network, Response; недовірене для відображення (авторизація ЛР4 перевіряє право читати, а не DOM) |
| Client transform | `Client/browser-lab.js`, `load()` | `current.description` | передається без очищення |
| Sink/context | **до:** `document.querySelector("#description").innerHTML = current.description`; **після:** `setPlainText(...)` → `replaceChildren(document.createTextNode(...))` | HTML parser / текстовий вузол | до: небезпечний sink (HTML context); після: безпечний (text node) |

## Потік 2: IncidentComment.Text

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Data/DbSeeder.cs`, `SeedAsync`, коментар `…0005` (у продукті – автор коментаря) | `Lab05Marker` | контрольоване користувачем (імітація) |
| Request | endpoint створення коментаря відсутній, значення потрапляє через seed | – | – |
| Validation/mapping | відсутні для seed | – | не trusted |
| Storage | `IncidentComment.Text`, таблиця коментарів | marker | збережене значення користувача |
| Response | `IncidentQueries.GetDetailsAsync` → `IncidentCommentResponse.Text`; JSON `comments[].text`; публічний (`IsInternal=false`) видно Alice і Morgan | marker | недовірене для відображення |
| Client transform | `browser-lab.js`, цикл `for (const comment of current.comments)` | `comment.text` | без очищення |
| Sink/context | **до:** `paragraph.innerHTML = comment.text`; **після:** `setPlainText(paragraph, comment.text)` | HTML parser / текстовий вузол | до: небезпечний; після: безпечний |

## Інвентаризація client sinks (`src/SecureLab.Api/Client/`)

| Місце | Дані | Категорія | Контекст | Рішення |
|---|---|---|---|---|
| `browser-lab.html` | літерали | trusted constant | HTML | допустимо, user data відсутні |
| `browser-lab.js` `#title` | `current.title` | збережене значення користувача | text | `textContent`, безпечно |
| `browser-lab.js` `#description` | `current.description` | збережене значення користувача | HTML parser → text node | було `innerHTML` – виправлено |
| `browser-lab.js` коментарі | `comment.text` | збережене значення користувача | HTML parser → text node | було `innerHTML` – виправлено |
| `browser-lab.js` `#edit` textarea | `current.description` | збережене значення користувача | `value` property | безпечно (поле вводу, не розмітка) |
| `browser-lab.js` `#status` | `GET: ${status}` | server-generated | text | `textContent`, безпечно |
| `app.js` | `incident.title/description/comment.text` | збережене значення користувача | text | `createTextElement`/`createTextNode`, безпечно; seed-коментар `…0005` на `/` показується як текст |
| `auth-lab.js` | профіль, статуси | server-generated | text | `textContent`, безпечно |
| `href`/`src`/`onclick`/`eval`/`new Function` у власному коді | – | – | – | не знайдено (`Select-String`) |