#!/usr/bin/env node
// Prüft shared/webmail/lu-webmail.js mit nachgebauten Webmail-Seiten in Chromium (Playwright).
// Aufruf: node scripts/test-webmail.js   (braucht `playwright`; die echten Seiten können sich ändern –
// die Beispiele hier sichern nur die Logik des Lesers ab, nicht die aktuellen Selektoren der Anbieter.)
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const { chromium } = require("playwright");

const script = fs.readFileSync(path.join(__dirname, "..", "shared", "webmail", "lu-webmail.js"), "utf8");
const capture = fs.readFileSync(path.join(__dirname, "..", "shared", "webmail", "lu-capture.js"), "utf8");

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

  // Proton ohne Mitschnitt: Notlösung über die Textzeilen der Einträge (verschachtelte IDs zählen einmal).
  r = await call(`<div data-testid="message-list"><div data-element-id="c9" class="item-container read"><div data-element-id="c9">
      <span>Carla</span><br><span>Rechnung Oktober</span><br><span>10:15</span></div></div></div>`, 'LuWebmail.list("proton")');
  assert.strictEqual(r.items.length, 1);
  assert.deepStrictEqual([r.items[0].from, r.items[0].subject, r.items[0].unread], ["Carla", "Rechnung Oktober", false]);

  // Proton mit Mitschnitt der API-Antworten (lu-capture.js im Seitenkontext ab Dokumentbeginn).
  const time = Math.floor(Date.now() / 1000) - 60;
  await page.route("https://mail.proton.me/**", (route) => {
    const url = route.request().url();
    if (url.includes("/api/mail/v4/conversations?")) {
      return route.fulfill({ contentType: "application/json", body: JSON.stringify({ Code: 1000, Conversations: [
        { ID: "conv1", Subject: "Willkommen", Senders: [{ Name: "Proton", Address: "no-reply@proton.me" }], Time: time,
          ContextTime: time, NumUnread: 1, ContextNumUnread: 1, NumAttachments: 0, ContextNumAttachments: 1 },
        { ID: "conv2", Subject: "Alt", Senders: [{ Name: "", Address: "x@y.de" }], Time: 1600000000, ContextNumUnread: 0 },
      ] }) });
    }
    if (url.includes("/api/mail/v4/conversations/conv1")) {
      return route.fulfill({ contentType: "application/json", body: JSON.stringify({ Code: 1000,
        Conversation: { ID: "conv1", Subject: "Willkommen" },
        Messages: [{ ID: "m1", ConversationID: "conv1", Subject: "Willkommen", Sender: { Name: "Proton", Address: "no-reply@proton.me" },
                     ToList: [{ Name: "Alex", Address: "alex@proton.me" }], Time: time, Body: "-----BEGIN PGP MESSAGE-----" }] }) });
    }
    return route.fulfill({ contentType: "text/html", body: `<html><head><script>${capture}</script></head><body>
      <div data-testid="message-list">
        <div data-element-id="conv1" class="item-container unread"><span>Proton</span></div>
        <div data-element-id="conv2" class="item-container"><span>x@y.de</span></div>
      </div>
      <article data-testid="message-view-0" data-message-id="m1">
        <iframe data-testid="content-iframe" srcdoc="<p>Hallo <b>Alex</b></p><script>bad()</script>"></iframe>
      </article>
      <iframe data-testid="rooster-iframe" srcdoc="<p>Sent with Proton Mail secure email.</p>"></iframe>
      <script>
        // Proton ruft fetch mit einem URL-Objekt auf.
        // Erst die Liste, dann die Details – die Details dürfen Absender & Co. aus der Liste nicht löschen.
        fetch(new URL("/api/mail/v4/conversations?Page=0&LabelID=0", location.origin)).then(r => r.json()).then(() => {
          setTimeout(() => {
            var x = new XMLHttpRequest(); x.open("GET", new URL("/api/mail/v4/conversations/conv1", location.origin)); x.send();
          }, 120);
        });
      </script></body></html>` });
  });
  await page.goto("https://mail.proton.me/u/0/inbox/conv1");
  await page.waitForFunction(() => {
    const c = document.documentElement.getAttribute("data-lu-capture");
    return c && c.includes("conv2") && c.includes("m1");
  });
  await page.addScriptTag({ content: script });
  r = JSON.parse(await page.evaluate('LuWebmail.list("proton")'));
  assert.strictEqual(r.state, "ok");
  assert.deepStrictEqual(
    [r.items[0].id, r.items[0].from, r.items[0].fromEmail, r.items[0].subject, r.items[0].unread, r.items[0].attachment],
    ["conv1", "Proton", "no-reply@proton.me", "Willkommen", true, true]);
  assert.ok(/^\d{2}:\d{2}$/.test(r.items[0].date), r.items[0].date);
  assert.deepStrictEqual([r.items[1].from, r.items[1].unread, r.items[1].date.includes("2020")], ["x@y.de", false, true]);
  assert.ok(!JSON.stringify(r).includes("PGP"));
  r = JSON.parse(await page.evaluate('LuWebmail.message("proton")'));
  assert.strictEqual(r.state, "ok");
  assert.strictEqual(r.subject, "Willkommen");
  assert.strictEqual(r.parts.length, 1, "nur die Nachricht, nicht der Editor");
  assert.deepStrictEqual([r.parts[0].from, r.parts[0].fromEmail, r.parts[0].to], ["Proton", "no-reply@proton.me", "Alex"]);
  assert.ok(r.parts[0].html.includes("<b>Alex</b>") && !r.parts[0].html.includes("<script"));

  // Gelesen auf der Seite (Klasse "read") schlägt den älteren Zähler aus dem Mitschnitt.
  await page.evaluate(() => document.querySelector('[data-element-id="conv1"]').className = "item-container read");
  r = JSON.parse(await page.evaluate('LuWebmail.list("proton")'));
  assert.strictEqual(r.items[0].unread, false);

  // Nachricht nur mit Bild (ohne Text) lädt trotzdem.
  await page.evaluate(() => {
    document.querySelector('article iframe').srcdoc = '<div id="proton-root"><img src="x.png"></div>';
  });
  await page.waitForFunction(() => document.querySelector('article iframe').contentDocument.querySelector("img"));
  r = JSON.parse(await page.evaluate('LuWebmail.message("proton")'));
  assert.strictEqual(r.state, "ok");
  assert.ok(r.parts[0].html.includes("<img"));
  await page.unroute("https://mail.proton.me/**");

  r = await call(pages.mailru, 'LuWebmail.list("mailru")');
  assert.strictEqual(r.state, "ok");
  assert.deepStrictEqual([r.items[0].id, r.items[0].fromEmail, r.items[0].subject, r.items[0].snippet, r.items[0].unread],
                         ["/inbox/0:1234:0/", "ivan@mail.ru", "Привет", "Как дела?", true]);

  // Qualitätsprüfung: Einträge ohne Absender und Betreff → "error"/"unreadable" (statt „(unbekannt)“-Liste).
  r = await call(`<div data-testid="message-list"><div data-element-id="a"></div><div data-element-id="b"></div></div>`,
                 'LuWebmail.list("proton")');
  assert.deepStrictEqual([r.state, r.reason], ["error", "unreadable"]);

  // Platzhalter beim Laden der Proton-Liste → "loading" (nicht "unreadable").
  r = await call(`<div data-testid="message-list"><div data-element-id="placeholder-0" class="item-container unread item-is-loading">&nbsp;</div>
      <div data-element-id="placeholder-1" class="item-container item-is-loading">&nbsp;</div></div>`, 'LuWebmail.list("proton")');
  assert.strictEqual(r.state, "loading");

  r = await call(pages.login, 'LuWebmail.list("gmail")');
  assert.strictEqual(r.state, "login");
  r = await call("<div></div>", 'LuWebmail.list("gmail")');
  assert.strictEqual(r.state, "loading");
  r = await call("<div></div>", 'LuWebmail.list("outlook")');
  assert.strictEqual(r.state, "unsupported");

  await browser.close();
  console.log("lu-webmail.js: alle Prüfungen bestanden");
})().catch((e) => { console.error(e); process.exit(1); });
