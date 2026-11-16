# Докази безпеки браузера (DEL-01)
 
Рівень: відмінний («Відмінно»). Паролів, значень cookie/token та export storage тут немає. Рисунки наведено у Word-звіті; у репозиторії вони не зберігаються.
 
## Стенд і Git-стан
 
ОС/browser: Windows, Firefox; API `http://localhost:5080`, probes `http://localhost:4173` (allowed) і `http://localhost:4174` (disallowed). Release/base_commit з `.scaffolds/lab-05.json`: `lab-05-start-v1` / 21cef1885aff615950a56392ebf2e7d2e4f604ae. Vulnerable commit: 1adc4bd3a3b8910de89a4a22e6da8e0fd14e19d1 (install scaffold, Рис. 2) і a0443461115c520999d69f5421e6556323d5fd92 (seed коментаря); проміжні commits: ff65173fda82dca9b74de654ba3bc5a3fbbc1aba (DOM fix, Рис. 30), a5aad35d77ddd040705cb499aae6f598a1cae792 (CORS, Рис. 38), ed396190f5d0c4d1a91ecd5f339e643595de8d9a (CSP report-only, Рис. 40); fixed commit – той, на який вказує `v0.5.0` (`git rev-parse "v0.5.0^{commit}"`; повний hash у Word-звіті); `v0.5.0` на fixed commit; `main` fast-forward до fixed; передано через remote (`git push origin lab/5-browser-xss v0.5.0 main`).
 
Механізм сеансу (Application → Storage → Cookies → localhost:5080): назва `.AspNetCore.Identity.Application`, Domain localhost, Path /, HttpOnly ✔, SameSite Lax, Secure ✘ (http на Development); Value не наведено.
 
## Гіпотеза й REG-XSS-01
 
Карта – `docs/source-to-sink.md`. Гіпотеза: значення з textarea опису (і з seed-коментаря) зберігається в PostgreSQL (колонка description / IncidentComment.Text) і повертається через response field `description` / `comments[].text`, а далі передається в sink `innerHTML` у `browser-lab.js` (HTML parser); тому за умови відображення без очищення браузер інтерпретує дані як розмітку з обробником події.
 
Передумови: локальний Alice (автор) і Morgan (Analyst, привілейований глядач); Guid запису aaf4928f-828e-4557-a1a5-7cb4f5882062; marker з `resources/lab05-marker.txt`; reset: `dotnet run --project src/SecureLab.Api -- --reset-database`; створення: форма на `/browser-lab.html`.
 
| Спостереження | До fix | Після fix | Доказ |
|---|---|---|---|
| Значення в БД/API (`description === marker`) | true | true | Рис. 11, 13; Рис. 22 |
| DOM основного Description | елемент `<img>` усередині `#description` | один текстовий вузол (`&lt;img …`) | Рис. 14; Рис. 23 |
| Дія marker (`data-lab05-marker`) | `executed` | `null` | Рис. 11, 15; Рис. 22, 24 |
| Апостроф/лапки, `5 < 7`, Unicode, `<b>жирний</b>`, `</>` | не знімалось | видно рядком, `<b>` літерально | Рис. 26 |
| Довжина 3900 символів (`copy("A".repeat(3900))`) | не знімалось | відображається, помилок немає | Рис. 27 |
| Вікно Morgan: GET і marker; контроль Bob | `GET 200`, `executed`; Bob `404` / `null` | `GET 200`, `null` | Рис. 17, 18; Рис. 24; Bob – Рис. 25 |
 
Finding: актив – browser session і дані від імені користувача; передумови – локальний акаунт може створити опис, привілейований глядач відкриває запис; першопричина – недовірений `current.description` потрапляє у sink `innerHTML`; safe DOM diff – `innerHTML = …` замінено на `replaceChildren(createTextNode(…))` (helper `setPlainText`); retest – той самий marker і той самий запис більше не виконується; залишковий ризик – інші проєкти/сторінки, сторонні скрипти (див. нижче).
 
REG-XSS-01: передумови – commit `a0443461…` (vulnerable), reset бази, Alice, Origin `http://localhost:5080`; вхід – marker з `lab05-marker.txt`; кроки – створити запис через `/browser-lab.html`, відкрити як Alice, потім як Morgan; очікування – marker не виконується, DOM містить текст; негативний контроль – на vulnerable commit marker виконувався (Рис. 11, 15, 18); доказ – Network/Elements і файли `browser-lab.js`, `tests/browser/description-dom.js`.
 
## CORS і REG-CORS-01
 
Прогноз (до зміни policy):
 
| Сценарій | Чи доходить request до API? | Чи може JS прочитати response? | Чому |
|---|---|---|---|
| same-origin 5080 → API | так | так | CORS не потрібен |
| simple GET 4173 → API | так | ні без valid CORS response | browser SOP/CORS |
| simple GET 4174 → API | так | ні, origin не в allowlist | browser SOP/CORS |
| curl/.http → API | так | не застосовується | це не browser JavaScript |
| preflighted 4173 → API | спершу `OPTIONS`; actual залежить від відповіді | ні без успішного preflight | browser preflight і CORS |
 
Команди probes: `py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe` і те саме з `4174`. Policy `Lab05BrowserProbe` – лише Development, один exact origin 4173 (4174 – контроль); це навчальний стенд, а не архітектура продукту; штатна same-origin топологія – 5080.
 
| Клієнт / простий запит | До policy | Fixed state | Status, ACAO, Console |
|---|---|---|---|
| Same-origin 5080 | 200 (клієнт на 5080, Рис. 7, 13) | 200 | CORS-заголовки не потрібні |
| Allowed 4173 | CORS error (Рис. 31) | 200 (Рис. 34) | ACAO `http://localhost:4173` |
| Disallowed 4174 | CORS error (Рис. 32) | CORS error (Рис. 35) | немає ACAO; `CORS header 'Access-Control-Allow-Origin' missing` |
| Direct HTTP / curl | не знімалось | 200 (Рис. 37) | для 4173 є ACAO, для 4174 немає |
 
REG-CORS-01: browser evidence – Рис. 31–36; HTTP evidence – `curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4173|4174" http://localhost:5080/health`: для 4173 є `Access-Control-Allow-Origin: http://localhost:4173` (і `Allow-Credentials: true`), для 4174 нічого (Рис. 37); статус 200 в обох – CORS не зупиняє сам request. Порядок: `UseCors` стоїть після `UseRouting` і до `UseAuthentication`; запит без cookie з 4173 до `/api/incidents` дав `401`, і JS його прочитав (Рис. 33). Різниця browser/direct: SOP/CORS – правило браузера для JavaScript веб-сторінки; `curl` – не браузер. У Firefox повідомлення: `Cross-Origin Request Blocked … (Reason: CORS request did not succeed)` до policy і `(Reason: CORS header 'Access-Control-Allow-Origin' missing)` для 4174 після policy.
 
## Додатково для Добре та Відмінно
 
Другий flow – `IncidentComment.Text`: seed-коментар `30000000-0000-0000-0000-000000000005` (публічний, автор Morgan) на інциденті Alice `20000000-0000-0000-0000-000000000001`; у відповіді API `comments[].text` містить marker (Рис. 16); DOM до – `<img>` усередині `<p>`, після – текст (Рис. 28). Єдиний helper `setPlainText` використовується для обох полів.
 
Preflight: `OPTIONS` з 4173 → 204, `Access-Control-Request-Headers: x-lab-cors-probe`, відповідь містить Allow-Origin `http://localhost:4173`, Allow-Methods `GET`, Allow-Headers `X-Lab-Cors-Probe` (Рис. 34); з 4174 – `OPTIONS` 204 без ACAO, браузер блокує, actual GET не надсилається (0 B) (Рис. 35). Cookie-дослід: 4173 → `GET /api/incidents` 200 із `Access-Control-Allow-Credentials: true` і `Access-Control-Allow-Origin: http://localhost:4173` (Рис. 36; значення cookie замазано); без входу – `401` (Рис. 33). Контроль без `.AllowCredentials()` не знімався. Кінцевий `.AllowCredentials()` лише в Development policy для 4173. Review XSS/CORS/CSRF/authorization – див. DEL-03.
 
Місце CSP-зміни – наявна headers middleware у `Program.cs` перед `UseDefaultFiles`.
 
| Header / control | Фактичне значення | Ризик і межа |
|---|---|---|
| Content-Security-Policy | `default-src 'self'; frame-ancestors 'none'` | обмежує джерела ресурсів власним origin і заборонює вбудовування у frame; маскує наслідки, але не усуває sink |
| X-Content-Type-Options | `nosniff` | знижує MIME sniffing; не замінює правильний `Content-Type` |
| Referrer-Policy | `no-referrer` | не передає адресу сторінки; наш клієнт не потребує referrer; не дозволяє класти секрети в URL |
| frame-ancestors | `'none'` | clickjacking; не замінює authorization чи XSS-fix |
 
| Тип ресурсу | Джерело в клієнті | Дозволено CSP |
|---|---|---|
| сторінки, скрипти, стилі, зображення, fetch до API | лише `'self'` (`http://localhost:5080`) | так |
| зображення з `http://localhost:4173` (проба) | інший origin | ні |
 
## Додатково для Відмінно
 
DOM-тест: `tests/browser/description-dom.js`, Console на `/browser-lab.html`; vulnerable commit – FAIL (`Description text changed`, Рис. 20; `Comment 1 text changed`, Рис. 21), fixed commit – PASS (Рис. 29). Comments: перевірено кожен `comments[].text`, двічі `load()` без дублювання, позитивні рядки (Рис. 26–29). CSP: report-only → enforcement; headers, керована проба `new Image().src = "http://localhost:4173/x.png"` – у report-only лише повідомлення `(Report-Only policy) … would block`, запит у Network без блокування (Рис. 39), в enforcement – `blocked`, у Network статус `CSP` (Рис. 41). Firefox додатково попереджає, що Report-Only без `report-uri` не надсилає звітів; порушення все одно видно в Console. `safe-url.js`: сім рядків (Рис. 42). Пояснення text/attribute/URL/JavaScript і third-party – DEL-03.
 