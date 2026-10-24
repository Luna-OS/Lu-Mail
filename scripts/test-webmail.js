#!/usr/bin/env node
// Prüft shared/webmail/lu-webmail.js mit nachgebauten Webmail-Seiten in Chromium (Playwright).
// Aufruf: node scripts/test-webmail.js   (braucht `playwright`; die echten Seiten können sich ändern –
// die Beispiele hier sichern nur die Logik des Lesers ab, nicht die aktuellen Selektoren der Anbieter.)
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const { chromium } = require("playwright");

const script = fs.readFileSync(path.join(__dirname, "..", "shared", "webmail", "lu-webmail.js"), "utf8");

const pages = {
  gmail: `
    <div role="main"><table><tbody>
      <tr class="zA zE"><td><span class="yW"><span email="anna@example.com" name="Anna">Anna</span></span></td>
        <td><span class="bog"><span data-thread-id="#thread-f:1" data-legacy-thread-id="18abc" style="font-weight:700">Hallo Welt</span></span>
        <span class="y2"> - Wie geht es dir?</span></td><td><span title="Sa., 24. Okt. 2026, 08:01">08:01</span></td></tr>
      <tr class="zA yO"><td><span email="news@shop.de" name="Shop">Shop</span></td>
        <td><span data-legacy-thread-id="18def">Angebot</span></td><td><span title="Fr., 23. Okt. 2026, 10:00">23. Okt.</span></td></tr>
    </tbody></table></div>`,
  gmailMessage: `
    <div role="main"><h2 class="hP">Hallo Welt</h2>
      <div data-message-id="#msg-f:1"><h3><span class="gD" email="anna@example.com" name="Anna">Anna</span></h3>
        <span email="ich@gmail.com" name="ich">ich</span><span class="g3" title="24. Okt. 2026, 08:01">08:01</span>
        <div class="a3s"><p onclick="x()">Wie geht es dir?</p><script>bad()</script></div></div></div>`,
  proton: `
    <div data-testid="message-list">
      <div data-element-id="p1" class="item-container unread"><span data-testid="message-column:sender-address" title="bob@proton.me">Bob</span>
        <span data-testid="message-column:subject">Treffen</span><time datetime="2026-10-24">09:30</time></div>
    </div>`,
  mailru: `
    <div class="dataset__items">
      <a class="llc llc_new" href="/inbox/0:1234:0/"><span class="ll-crpt" title="ivan@mail.ru">Иван</span>
        <span class="ll-sj__normal">Привет</span><span class="ll-sp__normal">Как дела?</span><span class="llc__item_date" title="24 октября">10:00</span></a>
    </div>`,
  login: `<form><input type="email"><input type="password"></form>`,
};

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  async function call(html, expr) {
    await page.setContent(`<html><body>${html}</body></html>`);
    await page.addScriptTag({ content: script });
    return JSON.parse(await page.evaluate(expr));
  }

  let r = await call(pages.gmail, 'LuWebmail.list("gmail")');
  assert.strictEqual(r.state, "ok");
  assert.strictEqual(r.items.length, 2);
  assert.deepStrictEqual(
    { id: r.items[0].id, from: r.items[0].from, fromEmail: r.items[0].fromEmail, subject: r.items[0].subject, unread: r.items[0].unread },
    { id: "18abc", from: "Anna", fromEmail: "anna@example.com", subject: "Hallo Welt", unread: true });
  assert.strictEqual(r.items[0].snippet, "Wie geht es dir?");
  assert.strictEqual(r.items[0].dateTitle, "Sa., 24. Okt. 2026, 08:01");
  assert.strictEqual(r.items[1].unread, false);

  r = await call(pages.gmail, 'LuWebmail.open("gmail", "18def")');
  assert.strictEqual(r.state, "ok");
  r = await call(pages.gmail, 'LuWebmail.open("gmail", "nope")');
  assert.strictEqual(r.state, "missing");

  r = await call(pages.gmailMessage, 'LuWebmail.message("gmail")');
  assert.strictEqual(r.state, "ok");
  assert.strictEqual(r.subject, "Hallo Welt");
  assert.strictEqual(r.parts[0].fromEmail, "anna@example.com");
  assert.strictEqual(r.parts[0].to, "ich");
  assert.ok(r.parts[0].html.includes("Wie geht es dir?"));
  assert.ok(!r.parts[0].html.includes("onclick") && !r.parts[0].html.includes("<script"));

  r = await call(pages.proton, 'LuWebmail.list("proton")');
  assert.strictEqual(r.state, "ok");
  assert.deepStrictEqual([r.items[0].id, r.items[0].from, r.items[0].fromEmail, r.items[0].subject, r.items[0].unread],
                         ["p1", "Bob", "bob@proton.me", "Treffen", true]);

  r = await call(pages.mailru, 'LuWebmail.list("mailru")');
  assert.strictEqual(r.state, "ok");
  assert.deepStrictEqual([r.items[0].id, r.items[0].fromEmail, r.items[0].subject, r.items[0].snippet, r.items[0].unread],
                         ["/inbox/0:1234:0/", "ivan@mail.ru", "Привет", "Как дела?", true]);

  r = await call(pages.login, 'LuWebmail.list("gmail")');
  assert.strictEqual(r.state, "login");
  r = await call("<div></div>", 'LuWebmail.list("gmail")');
  assert.strictEqual(r.state, "loading");
  r = await call("<div></div>", 'LuWebmail.list("outlook")');
  assert.strictEqual(r.state, "unsupported");

  await browser.close();
  console.log("lu-webmail.js: alle Prüfungen bestanden");
})().catch((e) => { console.error(e); process.exit(1); });
