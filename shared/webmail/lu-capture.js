// Lu-Mail Web-Leser – Mitschnitt (docs/FUNKTIONEN.md §21)
//
// Läuft im Seitenkontext des Webmails ab Dokumentbeginn (macOS: WKUserScript in der Page-World, Windows:
// AddScriptToExecuteOnDocumentCreatedAsync). Merkt sich die Metadaten aus den Listen-Antworten der Webmail-API
// (Proton: /api/mail/v4/conversations und /messages – Absender, Betreff, Datum, ungelesen sind dort Klartext;
// der verschlüsselte Inhalt wird verworfen) und legt sie als JSON im Attribut data-lu-capture von <html> ab.
// lu-webmail.js liest sie von dort – das ist stabiler als Absender und Betreff aus dem Seitenaufbau zu lesen.
(function () {
  "use strict";
  if (window.__luCaptureInstalled) return;
  window.__luCaptureInstalled = true;

  var PATTERN = /\/api\/mail\/v4\/(conversations|messages)(\/[A-Za-z0-9_=-]+)?(\?|$)/;
  var LIMIT = 3000;
  var store = { conversations: {}, messages: {} };
  var count = 0;
  var timer = null;

  function person(p) {
    return p ? { Name: p.Name || "", Address: p.Address || "" } : null;
  }

  function slim(item) {
    return {
      ID: item.ID,
      ConversationID: item.ConversationID,
      Subject: item.Subject,
      Senders: Array.isArray(item.Senders) ? item.Senders.map(person) : undefined,
      Sender: person(item.Sender),
      ToList: Array.isArray(item.ToList) ? item.ToList.map(person) : undefined,
      Time: item.Time,
      ContextTime: item.ContextTime,
      Unread: item.Unread,
      NumUnread: item.NumUnread,
      ContextNumUnread: item.ContextNumUnread,
      NumAttachments: item.NumAttachments,
      ContextNumAttachments: item.ContextNumAttachments,
    };
  }

  function save() {
    if (timer) return;
    timer = setTimeout(function () {
      timer = null;
      try {
        document.documentElement.setAttribute("data-lu-capture", JSON.stringify(store));
      } catch (e) { /* zu groß oder kein Dokument – egal */ }
    }, 50);
  }

  function put(map, item) {
    if (!item || !item.ID) return;
    if (count > LIMIT) {
      store = { conversations: {}, messages: {} };
      count = 0;
    }
    if (!store[map][item.ID]) count++;
    store[map][item.ID] = slim(item);
  }

  function take(url, data) {
    if (!data || typeof data !== "object") return;
    (data.Conversations || []).forEach(function (c) { put("conversations", c); });
    (data.Messages || []).forEach(function (m) { put("messages", m); });
    if (data.Conversation) put("conversations", data.Conversation);
    if (data.Message) put("messages", data.Message);
    save();
  }

  var originalFetch = window.fetch;
  if (typeof originalFetch === "function") {
    window.fetch = function (input, init) {
      var url = typeof input === "string" ? input : (input && input.url) || "";
      var promise = originalFetch.apply(this, arguments);
      if (PATTERN.test(url)) {
        promise.then(function (response) {
          try {
            response.clone().json().then(function (data) { take(url, data); }, function () {});
          } catch (e) { /* Antwort nicht lesbar */ }
        }, function () {});
      }
      return promise;
    };
  }

  var originalOpen = XMLHttpRequest.prototype.open;
  var originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function (method, url) {
    this.__luUrl = String(url || "");
    return originalOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function () {
    var xhr = this;
    if (PATTERN.test(xhr.__luUrl || "")) {
      xhr.addEventListener("load", function () {
        try {
          var data = xhr.responseType === "json" ? xhr.response : JSON.parse(xhr.responseText);
          take(xhr.__luUrl, data);
        } catch (e) { /* keine JSON-Antwort */ }
      });
    }
    return originalSend.apply(this, arguments);
  };
})();
