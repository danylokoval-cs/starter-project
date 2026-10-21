const result = document.querySelector("#result");
const profile = document.querySelector("#profile");

function clearProfile() {
  profile.hidden = true;
  document.querySelector("#profile-name").textContent = "";
  document.querySelector("#profile-role").textContent = "";
  document.querySelector("#profile-id").textContent = "";
}

async function postJson(url, data) {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data)
  });
}

async function showProfile() {
  const response = await fetch("/api/me");
  if (!response.ok) {
    clearProfile();
    result.textContent = `Профіль: ${response.status}`;
    return;
  }
  const me = await response.json();
  document.querySelector("#profile-name").textContent = me.userName ?? "";
  document.querySelector("#profile-role").textContent = me.role ?? "";
  document.querySelector("#profile-id").textContent = me.userId;
  profile.hidden = false;
  result.textContent = `Профіль: ${response.status}`;
}

document.querySelector("#register").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const response = await postJson("/api/auth/register", Object.fromEntries(new FormData(form)));
  form.elements.password.value = "";
  result.textContent = `Реєстрація: ${response.status}`;
});

document.querySelector("#login").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const response = await postJson("/api/auth/login", Object.fromEntries(new FormData(form)));
  form.elements.password.value = "";
  result.textContent = `Вхід: ${response.status}`;
  if (response.ok) await showProfile();
});

document.querySelector("#me").addEventListener("click", showProfile);

document.querySelector("#incident").addEventListener("submit", async event => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const response = await postJson("/api/incidents", {
    title: data.get("title"),
    description: data.get("description"),
    severity: data.get("severity"),
    occurredAtUtc: new Date(data.get("occurredAtUtc")).toISOString()
  });
  result.textContent = `Створення: ${response.status}`;
});

document.querySelector("#logout").addEventListener("click", async () => {
  const response = await fetch("/api/auth/logout", { method: "POST" });
  clearProfile();
  result.textContent = `Вихід: ${response.status}`;
  const check = await fetch("/api/me");
  result.textContent += `\nПеревірка профілю після виходу: ${check.status}`;
});
