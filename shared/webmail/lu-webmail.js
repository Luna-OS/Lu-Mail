// Lu-Mail Web-Leser (docs/FUNKTIONEN.md §21)
//
// Läuft im unsichtbaren Browser eines Web-Kontos (macOS: eigene Content-World, Windows: WebView2) und liest
// Nachrichtenliste und geöffnete Nachricht aus dem Webmail des Anbieters, damit Lu-Mail sie in der eigenen
// Oberfläche zeigen kann. Jeder Aufruf liefert einen JSON-String:
//
//   LuWebmail.list("gmail")        → {"state":"ok"|"loading"|"login"|"error", "items":[{id, from, fromEmail, subject,
//                                      snippet, date, dateTitle, unread, attachment}]}
//                                      ("error" mit reason "unreadable": die Seite hat sich geändert)
//   LuWebmail.open("gmail", id)    → {"state":"ok"|"missing"}   (öffnet die Nachricht im Webmail)
//   LuWebmail.message("gmail")     → {"state":"ok"|"loading", "subject", "parts":[{from, fromEmail, to, date, html}]}
//
// Webseiten ändern sich: Jeder Anbieter hat mehrere Selektoren, zuerst stabile Attribute (role, data-*, email),
// danach Klassennamen. Erkennt der Leser nichts, schaltet Lu-Mail auf die Webseiten-Ansicht um.
(function () {
  "use strict";
  if (window.LuWebmail) return;

  // MARK: Hilfen

  function q(root, selector) {
    try { return root ? root.querySelector(selector) : null; } catch (e) { return null; }
  }

  function qa(root, selector) {
    try { return root ? Array.prototype.slice.call(root.querySelectorAll(selector)) : []; } catch (e) { return []; }
  }

  function first(root, selectors) {
    for (var i = 0; i < selectors.length; i++) {
      var found = q(root, selectors[i]);
      if (found) return found;
    }
    return null;
  }

  function text(el) {
    if (!el) return "";
    return String(el.innerText || el.textContent || "").replace(/\s+/g, " ").trim();
  }

  function attr(el, name) {
    return el && el.getAttribute ? (el.getAttribute(name) || "") : "";
  }

  function emailIn(value) {
    var match = String(value || "").match(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/);
    return match ? match[0] : "";
  }

  function isBold(el) {
    if (!el) return false;
    try {
      var weight = window.getComputedStyle(el).fontWeight;
      return weight === "bold" || parseInt(weight, 10) >= 600;
    } catch (e) {
      return false;
    }
  }

  function visible(el) {
    return !!(el && (el.offsetWidth || el.offsetHeight || (el.getClientRects && el.getClientRects().length)));
  }

  function hasPasswordField() {
    return qa(document, 'input[type="password"]').some(visible);
  }

  // Echte Maus-Ereignisse – manche Webmails reagieren nicht auf ein bloßes click().
  function press(el) {
    if (!el) return false;
    ["mousedown", "mouseup", "click"].forEach(function (type) {
      el.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, view: window, button: 0 }));
    });
    return true;
  }

  // HTML ohne Skripte, Ereignis-Attribute und Formulare (Lu-Mail zeigt es ohnehin ohne JavaScript an).
  function cleanHTML(el) {
    if (!el) return "";
    var copy = el.cloneNode(true);
    qa(copy, "script, noscript, form, button, input, textarea, select").forEach(function (node) {
      node.parentNode && node.parentNode.removeChild(node);
    });
    qa(copy, "*").forEach(function (node) {
      Array.prototype.slice.call(node.attributes || []).forEach(function (a) {
        if (/^on/i.test(a.name)) node.removeAttribute(a.name);
      });
    });
    return copy.innerHTML;
  }

  // Inhalt eines (gleich-originigen) iframes, z. B. bei Proton.
  function frameHTML(frame) {
    try {
      var doc = frame && (frame.contentDocument || (frame.contentWindow && frame.contentWindow.document));
      return doc && doc.body ? cleanHTML(doc.body) : "";
    } catch (e) {
      return "";
    }
  }

  function largestTextBlock(root) {
    var best = null;
    var bestLength = 0;
    qa(root, "div").forEach(function (div) {
      var length = text(div).length;
      if (length > bestLength && qa(div, "div").length < 400) {
        best = div;
        bestLength = length;
      }
    });
    return best;
  }

  function result(object) {
    return JSON.stringify(object);
  }

  // Zeilen des sichtbaren Textes – Notlösung, wenn die Selektoren für Absender/Betreff nichts finden.
  function lines(el) {
    return String((el && el.innerText) || "").split(/\n+/).map(function (l) { return l.trim(); }).filter(function (l) {
      return l.length > 0 && !/^(\d{1,2}[:.]\d{2}|\d{1,2}\.\s?\w+\.?|\d+)$/.test(l);
    });
  }

  function fillFromLines(item, row) {
    if (item.from && item.subject) return item;
    var parts = lines(row);
    if (!item.from && parts.length) item.from = parts[0];
    if (!item.subject && parts.length > 1) item.subject = parts[1];
    if (!item.snippet && parts.length > 2) item.snippet = parts[2];
    return item;
  }

  // Elemente mit gleichem Attribut können verschachtelt sein – nur das äußerste zählt.
  function outermost(elements, name) {
    var seen = {};
    return elements.filter(function (el) {
      var id = attr(el, name);
      if (!id || seen[id]) return false;
      for (var p = el.parentElement; p; p = p.parentElement) {
        if (attr(p, name) === id) return false;
      }
      seen[id] = true;
      return true;
    });
  }

  // Mitschnitt aus lu-capture.js (Metadaten der Webmail-API).
  function captured() {
    try {
      return JSON.parse(document.documentElement.getAttribute("data-lu-capture") || "{}");
    } catch (e) {
      return {};
    }
  }

  function shortDate(seconds) {
    if (!seconds) return "";
    var date = new Date(seconds * 1000);
    var now = new Date();
    if (date.toDateString() === now.toDateString()) {
      return date.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
    }
    var options = { day: "numeric", month: "short" };
    if (date.getFullYear() !== now.getFullYear()) options.year = "numeric";
    return date.toLocaleDateString("de-DE", options);
  }

  function longDate(seconds) {
    return seconds ? new Date(seconds * 1000).toLocaleString("de-DE", { dateStyle: "full", timeStyle: "short" }) : "";
  }

  // MARK: Anbieter

  var adapters = {};

  // Gmail: Zeilen tragen data-thread-id / data-legacy-thread-id, Absender ein email-Attribut.
  adapters.gmail = {
    rows: function () {
      var main = q(document, '[role="main"]');
      if (!main) return null;
      return qa(main, "tr").filter(function (row) {
        return q(row, "[data-legacy-thread-id], [data-thread-id]") && visible(row);
      });
    },
    rowId: function (row) {
      var marker = q(row, "[data-legacy-thread-id], [data-thread-id]");
      return attr(marker, "data-legacy-thread-id") || attr(marker, "data-thread-id").replace(/^#?thread-f:/, "");
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var rows = this.rows();
      if (!rows) return { state: "loading" };
      var self = this;
      var items = rows.map(function (row) {
        var marker = q(row, "[data-legacy-thread-id], [data-thread-id]");
        var senders = qa(row, "[email]");
        var sender = senders[0];
        var names = senders.map(function (s) { return attr(s, "name") || text(s); }).filter(Boolean);
        var dates = qa(row, "td span[title], td [title][aria-label]").filter(function (s) { return /\d/.test(attr(s, "title")); });
        var date = dates[dates.length - 1];
        var snippet = first(row, [".y2", '[class*="snippet"]']);
        return {
          id: self.rowId(row),
          from: names.slice(0, 3).join(", ") || text(sender),
          fromEmail: attr(sender, "email"),
          subject: text(marker),
          snippet: text(snippet).replace(/^[-–\s]+/, ""),
          date: text(date),
          dateTitle: attr(date, "title"),
          unread: row.classList.contains("zE") || isBold(marker) || isBold(sender),
          attachment: !!q(row, '[data-tooltip*="ttach"], img[src*="attachment"], .brd, .aKS'),
        };
      }).map(function (item, index) { return fillFromLines(item, rows[index]); }).filter(function (item) { return item.id; });
      return { state: "ok", items: items };
    },
    open: function (id) {
      var rows = this.rows() || [];
      for (var i = 0; i < rows.length; i++) {
        if (this.rowId(rows[i]) === id) {
          return press(q(rows[i], "[data-legacy-thread-id], [data-thread-id]") || rows[i]);
        }
      }
      return false;
    },
    message: function () {
      var main = q(document, '[role="main"]');
      if (!main) return { state: "loading" };
      var subject = first(main, ["h2[data-thread-perm-id]", "h2.hP", "h2"]);
      var containers = qa(main, "[data-message-id], [data-legacy-message-id]");
      var parts = containers.map(function (container) {
        var body = first(container, [".a3s", '[data-message-id] [dir="ltr"]']);
        if (!body) return null;
        var people = qa(container, "[email]");
        var sender = people[0];
        var date = qa(container, "span[title]").filter(function (s) { return /\d/.test(attr(s, "title")); })[0];
        return {
          from: attr(sender, "name") || text(sender),
          fromEmail: attr(sender, "email"),
          to: people.slice(1).map(function (p) { return attr(p, "name") || attr(p, "email"); }).filter(Boolean).join(", "),
          date: attr(date, "title") || text(date),
          html: cleanHTML(body),
        };
      }).filter(Boolean);
      if (!parts.length) return { state: "loading" };
      return { state: "ok", subject: text(subject), parts: parts };
    },
  };

  // Proton Mail: Listeneinträge mit data-element-id (= Unterhaltungs- bzw. Nachrichten-ID der Proton-API).
  // Absender, Betreff, Datum und „ungelesen“ kommen aus dem Mitschnitt der API-Antworten (lu-capture.js),
  // nur wenn der fehlt, aus dem Seitenaufbau. Der Inhalt steht in einem iframe.
  adapters.proton = {
    rows: function () {
      return outermost(qa(document, "[data-element-id]"), "data-element-id").filter(visible);
    },
    fromCapture: function (id, meta) {
      var senders = meta.Senders || (meta.Sender ? [meta.Sender] : []);
      var names = senders.map(function (p) { return p && (p.Name || p.Address); }).filter(Boolean);
      var time = meta.ContextTime || meta.Time;
      var unread = meta.ContextNumUnread !== undefined ? meta.ContextNumUnread
        : (meta.NumUnread !== undefined ? meta.NumUnread : meta.Unread);
      var attachments = meta.ContextNumAttachments !== undefined ? meta.ContextNumAttachments : meta.NumAttachments;
      return {
        id: id,
        from: names.slice(0, 3).join(", "),
        fromEmail: (senders[0] && senders[0].Address) || "",
        subject: meta.Subject || "",
        snippet: "",
        date: shortDate(time),
        dateTitle: longDate(time),
        unread: Number(unread) > 0,
        attachment: Number(attachments) > 0,
      };
    },
    fromPage: function (id, row) {
      var sender = first(row, ['[data-testid*="sender"]', ".item-senders", '[class*="sender"]']);
      var subject = first(row, ['[data-testid*="subject"]', ".item-subject", '[class*="subject"]']);
      var date = first(row, ["time", '[data-testid*="date"]', ".item-date"]);
      var emailHolder = first(row, ['[title*="@"]']);
      return fillFromLines({
        id: id,
        from: text(sender),
        fromEmail: emailIn(attr(emailHolder, "title")) || emailIn(attr(sender, "title")),
        subject: text(subject) || attr(subject, "title"),
        snippet: "",
        date: text(date),
        dateTitle: attr(date, "datetime") || attr(date, "title"),
        unread: row.classList.contains("unread") || attr(row, "data-unread") === "true",
        attachment: !!q(row, '[data-testid*="attachment"]'),
      }, row);
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var rows = this.rows();
      if (!rows.length && !q(document, '[data-testid="message-list"], [data-testid*="empty-view"], .items-column-list')) {
        return { state: "loading" };
      }
      var capture = captured();
      var self = this;
      var items = rows.map(function (row) {
        var id = attr(row, "data-element-id");
        var meta = (capture.conversations || {})[id] || (capture.messages || {})[id];
        return meta ? self.fromCapture(id, meta) : self.fromPage(id, row);
      });
      return { state: "ok", items: items };
    },
    open: function (id) {
      var rows = this.rows();
      for (var i = 0; i < rows.length; i++) {
        if (attr(rows[i], "data-element-id") === id) return press(rows[i]);
      }
      return false;
    },
    message: function () {
      // Inhalt: alle (gleich-originigen) iframes mit Text – bei Unterhaltungen eins pro aufgeklappter Nachricht.
      var bodies = qa(document, "iframe").map(frameHTML).filter(function (html) {
        return html && html.replace(/<[^>]*>/g, "").trim().length > 0;
      });
      if (!bodies.length) return { state: "loading" };
      // Absender/Datum aus dem Mitschnitt: Unterhaltungs-ID steht am Ende der Adresse (…/inbox/<ID>).
      var capture = captured();
      var id = decodeURIComponent((location.pathname.split("/").filter(Boolean).pop() || ""));
      var conversation = (capture.conversations || {})[id];
      var messages = [];
      var all = capture.messages || {};
      Object.keys(all).forEach(function (key) {
        var m = all[key];
        if (m && (m.ConversationID === id || m.ID === id)) messages.push(m);
      });
      messages.sort(function (a, b) { return (a.Time || 0) - (b.Time || 0); });
      var offset = messages.length - bodies.length;
      var parts = bodies.map(function (html, index) {
        var meta = messages[offset + index];
        var sender = meta && meta.Sender;
        return {
          from: (sender && (sender.Name || sender.Address)) || "",
          fromEmail: (sender && sender.Address) || "",
          to: meta && meta.ToList ? meta.ToList.map(function (p) { return p && (p.Name || p.Address); }).filter(Boolean).join(", ") : "",
          date: meta ? longDate(meta.Time) : "",
          html: html,
        };
      });
      var subject = (conversation && conversation.Subject) || (messages.length && messages[messages.length - 1].Subject)
        || text(first(document, ['[data-testid="conversation-header:subject"]', "h1"]));
      return { state: "ok", subject: subject || "", parts: parts };
    },
  };

  // Mail.ru: Listeneinträge sind Links auf /<ordner>/<id>/.
  adapters.mailru = {
    rows: function () {
      var rows = qa(document, "a.llc");
      if (!rows.length) {
        rows = qa(document, "a[href]").filter(function (a) {
          return /\/(inbox|sent|drafts|spam|trash|[0-9]+)\/[0-9]+:[^/]+/.test(attr(a, "href"));
        });
      }
      return rows.filter(visible);
    },
    list: function () {
      if (hasPasswordField()) return { state: "login" };
      var rows = this.rows();
      if (!rows.length && !q(document, ".dataset__items, .letter-list, [class*='dataset']")) return { state: "loading" };
      var items = rows.map(function (row) {
        var sender = first(row, [".ll-crpt", ".llc__item_correspondent", '[title*="@"]']);
        var subject = first(row, [".ll-sj__normal", ".llc__subject", '[class*="subject"]']);
        var snippet = first(row, [".ll-sp__normal", ".llc__snippet", '[class*="snippet"]']);
        var date = first(row, [".llc__item_date", ".ll-dt", '[class*="date"]']);
        return {
          id: attr(row, "href"),
          from: text(sender),
          fromEmail: emailIn(attr(sender, "title")),
          subject: text(subject),
          snippet: text(snippet),
          date: text(date),
          dateTitle: attr(date, "title"),
          unread: /llc_(new|unread)/.test(row.className) || !!q(row, ".ll-rs_is-active, [class*='unread']") || isBold(subject),
          attachment: !!q(row, '[class*="attach"]'),
        };
      }).map(function (item, index) { return fillFromLines(item, rows[index]); }).filter(function (item) { return item.id; });
      return { state: "ok", items: items };
    },
    open: function (id) {
      var rows = this.rows();
      for (var i = 0; i < rows.length; i++) {
        if (attr(rows[i], "href") === id) return press(rows[i]);
      }
      return false;
    },
    message: function () {
      var body = first(document, [".letter-body__body-content", ".letter__body", '[class*="letter-body"]', '[class*="letter__body"]']);
      if (!body) return { state: "loading" };
      var subject = first(document, [".thread-subject", ".thread__subject", '[class*="thread__subject"]', "h2"]);
      var sender = first(document, [".letter-contact", '[class*="letter__author"] [title*="@"]', '[title*="@"]']);
      var date = first(document, [".letter__date", '[class*="letter__date"]']);
      return {
        state: "ok",
        subject: text(subject),
        parts: [{
          from: text(sender),
          fromEmail: emailIn(attr(sender, "title")) || emailIn(text(sender)),
          to: "",
          date: text(date),
          html: cleanHTML(body) || cleanHTML(largestTextBlock(document.body)),
        }],
      };
    },
  };

  // Liste plausibel? Hat mindestens die Hälfte der Einträge weder Absender noch Betreff, hat sich die Seite
  // geändert – dann lieber die Webseite zeigen als eine Liste voller „(unbekannt)“.
  function checked(list) {
    if (list && list.state === "ok" && list.items && list.items.length) {
      var empty = list.items.filter(function (item) { return !item.from && !item.fromEmail && !item.subject; }).length;
      if (empty * 2 >= list.items.length) return { state: "error", reason: "unreadable", items: [] };
    }
    return list;
  }

  function run(name, action) {
    var adapter = adapters[name];
    if (!adapter) return result({ state: "unsupported" });
    try {
      return result(action(adapter));
    } catch (e) {
      return result({ state: "error", error: String(e && e.message || e) });
    }
  }

  window.LuWebmail = {
    version: 1,
    list: function (name) { return run(name, function (a) { return checked(a.list()); }); },
    open: function (name, id) { return run(name, function (a) { return { state: a.open(id) ? "ok" : "missing" }; }); },
    message: function (name) { return run(name, function (a) { return a.message(); }); },
  };
})();
