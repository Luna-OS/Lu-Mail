import XCTest
@testable import LuMailCore

final class WebAccountTests: XCTestCase {
    let catalog = ProviderCatalog.shared

    func web(_ id: String) -> WebConfig { catalog.provider(id: id)!.web! }

    func testEveryProviderExceptCustomHasWeb() {
        for provider in catalog.providers {
            if provider.id == "custom" {
                XCTAssertNil(provider.web)
                XCTAssertFalse(provider.authMethods.contains(.web))
            } else {
                XCTAssertNotNil(provider.web, provider.id)
                XCTAssertEqual(provider.authMethods.first, .web, provider.id)
            }
        }
        XCTAssertNotNil(catalog.webUserAgent["macos"])
    }

    func testLoginURLNeverCountsAsLoggedIn() {
        for provider in catalog.providers {
            guard let web = provider.web else { continue }
            XCTAssertFalse(web.isLoggedIn(url: web.loginUrl), provider.id)
            XCTAssertNotNil(URL(string: web.loginUrl), provider.id)
            XCTAssertTrue(web.loginUrl.hasPrefix("https://"), provider.id)
            for url in web.folders.values { XCTAssertTrue(url.hasPrefix("https://"), "\(provider.id) \(url)") }
            XCTAssertNotNil(web.folders["primary"], provider.id)
        }
    }

    func testLoggedInDetection() {
        let gmail = web("gmail")
        XCTAssertTrue(gmail.isLoggedIn(url: "https://mail.google.com/mail/u/0/#inbox"))
        XCTAssertFalse(gmail.isLoggedIn(url: "https://accounts.google.com/v3/signin/identifier?continue=x"))
        XCTAssertFalse(web("icloud").isLoggedIn(url: "https://www.icloud.com/mail/"))
    }

    func testSidebarFoldersOrderAndCategories() {
        let gmail = web("gmail")
        XCTAssertEqual(gmail.sidebarFolders().map(\.key),
                       ["primary", "promotions", "social", "updates", "forums", "sent", "drafts", "archive", "spam", "trash"])
        XCTAssertEqual(gmail.sidebarFolders().first?.title, "Posteingang")
        XCTAssertEqual(gmail.sidebarFolders()[1].title, "Werbung")
        XCTAssertEqual(gmail.sidebarFolders(separateCategories: false).map(\.key),
                       ["primary", "sent", "drafts", "archive", "spam", "trash"])
        XCTAssertEqual(web("hostinger").sidebarFolders().map(\.key), ["primary"])
    }

    func testUnreadCountOnlyFromTitle() {
        let gmail = web("gmail")
        XCTAssertEqual(gmail.unreadCount(fromTitle: "Inbox (12) - max@gmail.com - Gmail"), 12)
        XCTAssertEqual(gmail.unreadCount(fromTitle: "Posteingang - max@gmail.com - Gmail"), nil)
        XCTAssertEqual(web("proton").unreadCount(fromTitle: "(3) Inbox | max@proton.me | Proton Mail"), 3)
    }

    func testIsOnInbox() {
        let gmail = web("gmail")
        XCTAssertTrue(gmail.isOnInbox(url: "https://mail.google.com/mail/u/0/#inbox"))
        XCTAssertTrue(gmail.isOnInbox(url: "https://mail.google.com/mail/u/0/#inbox/FMfcgzQ"))
        XCTAssertFalse(gmail.isOnInbox(url: "https://mail.google.com/mail/u/0/#spam"))
        let mailru = web("mailru")
        XCTAssertTrue(mailru.isOnInbox(url: "https://e.mail.ru/inbox"))
        XCTAssertFalse(mailru.isOnInbox(url: "https://e.mail.ru/sent/"))
    }

    func testEmailFromTitle() {
        XCTAssertEqual(WebConfig.email(fromTitle: "Inbox (3) - max.muster@gmail.com - Gmail"), "max.muster@gmail.com")
        XCTAssertNil(WebConfig.email(fromTitle: "Mail - Max - Outlook"))
    }

    func testInternalHosts() {
        let gmail = web("gmail")
        XCTAssertTrue(gmail.isInternal(url: "https://mail.google.com/mail/u/0/?ui=2&view=att"))
        XCTAssertTrue(gmail.isInternal(url: "https://accounts.google.com/"))
        XCTAssertFalse(gmail.isInternal(url: "https://www.amazon.de/angebot"))
        XCTAssertTrue(web("mailru").isInternal(url: "https://account.mail.ru/login"))
    }

    func testRedirectTarget() {
        XCTAssertEqual(WebConfig.redirectTarget(of: "https://www.google.com/url?q=https://shop.de/a%3Fb%3D1&sa=D"), "https://shop.de/a?b=1")
        XCTAssertNil(WebConfig.redirectTarget(of: "https://www.google.com/search?q=test"))
        XCTAssertNil(WebConfig.redirectTarget(of: "https://www.google.com/url?q=javascript:alert(1)"))
    }

    func testWebAccountRoundTrip() throws {
        let account = Account(providerId: "gmail", email: "", displayName: "Gmail", authMethod: .web)
        let decoded = try JSONDecoder().decode(Account.self, from: JSONEncoder().encode(account))
        XCTAssertTrue(decoded.isWeb)
        XCTAssertEqual(decoded.label, "Gmail")
    }
}

final class WebReaderTests: XCTestCase {
    func testReaderConfigured() {
        let catalog = ProviderCatalog.shared
        XCTAssertEqual(catalog.provider(id: "gmail")?.web?.reader, "gmail")
        XCTAssertEqual(catalog.provider(id: "proton")?.web?.reader, "proton")
        XCTAssertEqual(catalog.provider(id: "mailru")?.web?.reader, "mailru")
        XCTAssertNil(catalog.provider(id: "outlook")?.web?.reader)
        XCTAssertTrue(SharedResources.webmailJS.contains("window.LuWebmail"))
    }

    func testDecodeList() throws {
        let json = #"{"state":"ok","items":[{"id":"18abc","from":"Anna","fromEmail":"anna@example.com","subject":"Hallo","snippet":"Hi","date":"08:01","dateTitle":"Sa.","unread":true,"attachment":false},{"id":"x"}]}"#
        let result = try XCTUnwrap(WebReader.decode(WebReader.ListResult.self, from: json))
        XCTAssertEqual(result.state, .ok)
        XCTAssertEqual(result.items.count, 2)
        XCTAssertEqual(result.items[0].sender, "Anna")
        XCTAssertTrue(result.items[0].unread)
        XCTAssertEqual(result.items[1].sender, "(unbekannt)")
        XCTAssertEqual(result.items[1].displaySubject, "(kein Betreff)")
    }

    func testDecodeStatesAndMessage() throws {
        XCTAssertEqual(WebReader.decode(WebReader.ListResult.self, from: #"{"state":"login"}"#)?.state, .login)
        XCTAssertEqual(WebReader.decode(WebReader.ListResult.self, from: #"{"state":"weird"}"#)?.state, .error)
        XCTAssertNil(WebReader.decode(WebReader.ListResult.self, from: "kaputt"))
        let message = try XCTUnwrap(WebReader.decode(WebReader.MessageResult.self,
            from: #"{"state":"ok","subject":"S","parts":[{"from":"","fromEmail":"a@b.de","html":"<p>x</p>"}]}"#))
        XCTAssertEqual(message.parts.first?.sender, "a@b.de")
        XCTAssertEqual(message.parts.first?.html, "<p>x</p>")
    }

    func testScriptsQuoteSafely() {
        XCTAssertEqual(WebReader.jsString(#"a"b\c</script>"#), #""a\"b\\c<\/script>""#)
        XCTAssertTrue(WebReader.openScript(reader: "gmail", id: "18abc").hasSuffix(#";LuWebmail.open("gmail", "18abc")"#))
    }
}
