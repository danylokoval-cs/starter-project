await (async () => {
  const check = (condition, message) => {
    if (!condition) throw new Error(message);
  };
  const marker = () => document.querySelector("#lab05-marker-target")
    .getAttribute("data-lab05-marker");
  const id = document.querySelector("#incident-id").value.trim();
  check(id.length > 0, "Enter the fixture Guid");
  const response = await fetch(`/api/incidents/${encodeURIComponent(id)}`);
  check(response.status === 200, `Unexpected HTTP ${response.status}`);
  const data = await response.json();
  const comments = data.comments ?? [];
  check(data.description.includes("data-lab05-marker")
    || comments.some(c => c.text.includes("data-lab05-marker")), "Wrong fixture");
  check(marker() === null, "Reload before test");
  await load();
  await load();
  const box = document.querySelector("#description");
  check(box.textContent === data.description, "Description text changed");
  check(box.childElementCount === 0, "Description became HTML elements");
  const rows = [...document.querySelectorAll("#comments > p")];
  check(rows.length === comments.length, "Comments duplicated or lost");
  rows.forEach((row, i) => {
    check(row.textContent === comments[i].text, `Comment ${i} text changed`);
    check(row.childElementCount === 0, `Comment ${i} became HTML elements`);
  });
  await new Promise(resolve => setTimeout(resolve, 500));
  check(marker() === null, "Marker executed");
  console.log("PASS: description and comments rendered as plain text");
})();