import XCTest
@testable import LuMailCore

final class ProviderCatalogTests: XCTestCase {
    func testLoadsAllProviders() throws {
        let catalog = ProviderCatalog.shared
        XCTAssertEqual(catalog.providers.map(\.id), ["gmail", "proton", "mailru", "hostinger"])
        XCTAssertEqual(catalog.provider(forEmail: "x@inbox.ru")?.id, "mailru")
        XCTAssertEqual(catalog.provider(forEmail: "x@pm.me")?.id, "proton")
        XCTAssertNil(catalog.provider(forEmail: "x@meinefirma.de"))
        XCTAssertTrue(try XCTUnwrap(catalog.provider(id: "gmail")).usesGmailCategories)
        XCTAssertEqual(catalog.provider(id: "hostinger")?.saveSentViaImap, true)
        XCTAssertNotNil(catalog.folderRoles[.trash])
    }

    func testThemeLoads() {
        let theme = ThemeTokens.shared
        XCTAssertEqual(theme.name, "Luna")
        XCTAssertNotNil(parseHexColor(theme.colors.dark.accent))
        XCTAssertEqual(Set(theme.providers.keys), ["gmail", "proton", "mailru", "hostinger"])
        XCTAssertNil(parseHexColor("nope"))
    }
}

final class ClassifierTests: XCTestCase {
    let classifier = CategoryClassifier.shared

    func category(_ from: String, _ subject: String = "Hallo", headers: [String: String] = [:],
                  server: MailCategory? = nil, overrides: [String: MailCategory] = [:]) -> MailCategory {
        classifier.classify(from: AddressParser.parseSingle(from), subject: subject, headers: headers,
                            serverCategory: server, overrides: overrides)
    }

    func testSocialBySubdomain() {
        XCTAssertEqual(category("notification@mail.instagram.com"), .social)
        XCTAssertEqual(category("noreply@vk.com"), .social)
    }

    func testForums() {
        XCTAssertEqual(category("someone@googlegroups.com"), .forums)
        XCTAssertEqual(category("list@example.org", headers: ["List-Post": "<mailto:list@example.org>"]), .forums)
    }

    func testUpdatesBySubjectBeatsPromotionHeaders() {
        XCTAssertEqual(category("shop@store.de", "Deine Bestellung wurde versendet", headers: ["List-Unsubscribe": "<x>"]), .updates)
        XCTAssertEqual(category("robot@ozon.ru", "Ваш заказ доставлен"), .updates)
    }

    func testPromotions() {
        XCTAssertEqual(category("anna@shop.de", headers: ["list-unsubscribe": "<mailto:x>"]), .promotions)
        XCTAssertEqual(category("anna@shop.de", headers: ["Precedence": " Bulk "]), .promotions)
        XCTAssertEqual(category("newsletter@shop.de"), .promotions)
        XCTAssertEqual(category("anna@shop.de", "50% Rabatt nur heute"), .promotions)
        XCTAssertEqual(category("anna@shop.ru", "Большие скидки"), .promotions)
    }

    func testUpdatesBySender() {
        XCTAssertEqual(category("no-reply@bank.de", "Neue Nachricht"), .updates)
    }

    func testPrimaryAndOverrides() {
        XCTAssertEqual(category("Mama <mama@gmx.de>", "Sonntag Essen?"), .primary)
        XCTAssertEqual(category("newsletter@lieblingsblog.de", overrides: ["newsletter@lieblingsblog.de": .primary]), .primary)
        XCTAssertEqual(category("mama@gmx.de", overrides: ["gmx.de": .social]), .social)
        // Nutzer-Regel schlägt Gmail-Kategorie, Gmail-Kategorie schlägt lokale Regeln.
        XCTAssertEqual(category("x@instagram.com", server: .primary), .primary)
        XCTAssertEqual(category("x@instagram.com", server: .primary, overrides: ["instagram.com": .updates]), .updates)
    }
}

final class FolderRoleTests: XCTestCase {
    let resolver = FolderRoleResolver()

    func roles(_ folders: [MailFolder]) -> [String: FolderRole] {
        var result: [String: FolderRole] = [:]
        for folder in resolver.assignRoles(folders) { if let role = folder.role { result[folder.path] = role } }
        return result
    }

    func testGmailSpecialUse() {
        let result = roles([
            MailFolder(path: "INBOX", delimiter: "/", attributes: []),
            MailFolder(path: "[Gmail]", delimiter: "/", attributes: ["\\Noselect"]),
            MailFolder(path: "[Gmail]/Alle Nachrichten", delimiter: "/", attributes: ["\\All"]),
            MailFolder(path: "[Gmail]/Gesendet", delimiter: "/", attributes: ["\\Sent"]),
            MailFolder(path: "[Gmail]/Spam", delimiter: "/", attributes: ["\\Junk"]),
            MailFolder(path: "[Gmail]/Papierkorb", delimiter: "/", attributes: ["\\Trash"]),
            MailFolder(path: "[Gmail]/Entw&APw-rfe", delimiter: "/", attributes: ["\\Drafts"]),
        ])
        XCTAssertEqual(result["INBOX"], .inbox)
        XCTAssertEqual(result["[Gmail]/Alle Nachrichten"], .archive)
        XCTAssertEqual(result["[Gmail]/Gesendet"], .sent)
        XCTAssertEqual(result["[Gmail]/Spam"], .spam)
        XCTAssertEqual(result["[Gmail]/Papierkorb"], .trash)
        XCTAssertEqual(result["[Gmail]/Entw&APw-rfe"], .drafts)
        XCTAssertNil(result["[Gmail]"])
    }

    func testHostingerDotNames() {
        let result = roles([
            MailFolder(path: "INBOX", delimiter: ".", attributes: []),
            MailFolder(path: "INBOX.Sent", delimiter: ".", attributes: []),
            MailFolder(path: "INBOX.Trash", delimiter: ".", attributes: []),
            MailFolder(path: "INBOX.Projekte.Archive", delimiter: ".", attributes: []),
        ])
        XCTAssertEqual(result["INBOX.Sent"], .sent)
        XCTAssertEqual(result["INBOX.Trash"], .trash)
        XCTAssertNil(result["INBOX.Projekte.Archive"])
    }

    func testMailRuRussianNames() {
        let result = roles([
            MailFolder(path: "INBOX", delimiter: "/", attributes: []),
            MailFolder(path: ModifiedUTF7.encode("Отправленные"), delimiter: "/", attributes: []),
            MailFolder(path: ModifiedUTF7.encode("Спам"), delimiter: "/", attributes: []),
            MailFolder(path: ModifiedUTF7.encode("Корзина"), delimiter: "/", attributes: []),
        ])
        XCTAssertEqual(result[ModifiedUTF7.encode("Отправленные")], .sent)
        XCTAssertEqual(result[ModifiedUTF7.encode("Спам")], .spam)
        XCTAssertEqual(result[ModifiedUTF7.encode("Корзина")], .trash)
    }

    func testDisplayNames() {
        XCTAssertEqual(MailFolder(path: "Projekte/K&APY-ln", delimiter: "/", attributes: []).displayName, "Köln")
        XCTAssertEqual(MailFolder(path: "x", delimiter: "/", attributes: [], role: .trash).displayName, "Papierkorb")
    }
}

final class OAuthTests: XCTestCase {
    func testSHA256() {
        XCTAssertEqual(SHA256.hash(Data("abc".utf8)).map { String(format: "%02x", $0) }.joined(),
                       "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        XCTAssertEqual(SHA256.hash(Data()).map { String(format: "%02x", $0) }.joined(),
                       "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
        let long = Data(String(repeating: "a", count: 1000).utf8)
        XCTAssertEqual(SHA256.hash(long).map { String(format: "%02x", $0) }.joined(),
                       "41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3")
    }

    func testPKCEChallenge() {
        let pkce = PKCE(verifier: "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gEVJWfIrw")
        XCTAssertEqual(pkce.challenge, "TKk0DSwlCkdDyHP0Hl3HapX-TPYSCEM_7iZWby8_xMg")
        XCTAssertGreaterThanOrEqual(PKCE().verifier.count, 43)
    }

    func testAuthorizationURL() throws {
        let gmail = try XCTUnwrap(ProviderCatalog.shared.provider(id: "gmail")?.oauth)
        let flow = OAuthFlow(endpoints: gmail, credentials: OAuthClientCredentials(clientId: "cid.apps"), port: 51234, state: "st")
        let url = flow.authorizationURL(loginHint: "max@gmail.com")
        let items = Dictionary(uniqueKeysWithValues: (URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []).map { ($0.name, $0.value ?? "") })
        XCTAssertEqual(url.host, "accounts.google.com")
        XCTAssertEqual(items["redirect_uri"], "http://127.0.0.1:51234/callback")
        XCTAssertEqual(items["client_id"], "cid.apps")
        XCTAssertEqual(items["state"], "st")
        XCTAssertEqual(items["code_challenge_method"], "S256")
        XCTAssertEqual(items["code_challenge"], flow.pkce.challenge)
        XCTAssertEqual(items["scope"], "https://mail.google.com/ openid email")
        XCTAssertEqual(items["access_type"], "offline")
        XCTAssertEqual(items["login_hint"], "max@gmail.com")

        let mailru = try XCTUnwrap(ProviderCatalog.shared.provider(id: "mailru")?.oauth)
        XCTAssertEqual(OAuthFlow(endpoints: mailru, credentials: .init(clientId: "x"), port: 9).redirectURI, "http://localhost:9/callback")
    }

    func testTokenRequestAndResponse() throws {
        let gmail = try XCTUnwrap(ProviderCatalog.shared.provider(id: "gmail")?.oauth)
        let flow = OAuthFlow(endpoints: gmail, credentials: .init(clientId: "cid", clientSecret: "sec"), port: 1)
        let request = flow.tokenRequest(code: "a/b c")
        let form = FormEncoding.decode(String(decoding: try XCTUnwrap(request.httpBody), as: UTF8.self))
        XCTAssertEqual(form["code"], "a/b c")
        XCTAssertEqual(form["code_verifier"], flow.pkce.verifier)
        XCTAssertEqual(form["client_secret"], "sec")
        XCTAssertEqual(form["grant_type"], "authorization_code")

        let now = Date(timeIntervalSince1970: 1000)
        let tokens = try OAuthFlow.parseTokenResponse(Data(#"{"access_token":"at","expires_in":3599,"refresh_token":"rt"}"#.utf8), now: now)
        XCTAssertEqual(tokens, OAuthTokens(accessToken: "at", refreshToken: "rt", expiresAt: now.addingTimeInterval(3599)))
        XCTAssertFalse(tokens.needsRefresh(now: now))
        XCTAssertTrue(tokens.needsRefresh(now: now.addingTimeInterval(3550)))

        let refreshed = try OAuthFlow.parseTokenResponse(Data(#"{"access_token":"at2","expires_in":"60"}"#.utf8), previousRefreshToken: "rt")
        XCTAssertEqual(refreshed.refreshToken, "rt")
        XCTAssertThrowsError(try OAuthFlow.parseTokenResponse(Data(#"{"error":"invalid_grant"}"#.utf8))) { error in
            XCTAssertEqual(error as? OAuthError, .tokenRequestFailed("invalid_grant"))
        }
    }

    func testEmailFromIDToken() {
        let payload = Data(#"{"email":"max@gmail.com","email_verified":true}"#.utf8).base64URLEncodedString
        XCTAssertEqual(OAuthFlow.email(fromIDToken: "eyJhbGciOiJSUzI1NiJ9.\(payload).sig"), "max@gmail.com")
        XCTAssertNil(OAuthFlow.email(fromIDToken: "kaputt"))
    }

    func testXOAuth2String() {
        let decoded = Data(base64Encoded: XOAuth2.initialResponse(user: "a@b.c", accessToken: "tok"))
        XCTAssertEqual(decoded.map { String(decoding: $0, as: UTF8.self) }, "user=a@b.c\u{01}auth=Bearer tok\u{01}\u{01}")
    }

    func testClientCredentialsIgnoreEmptyEntries() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("oauth-\(UUID().uuidString).json")
        try Data(#"{"_hinweis":"x","gmail":{"clientId":"abc","clientSecret":""},"mailru":{"clientId":""}}"#.utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        XCTAssertEqual(OAuthClientCredentials.load(from: url), ["gmail": OAuthClientCredentials(clientId: "abc")])
    }
}

final class BrowserLoginTests: XCTestCase {
    func request(_ raw: String) throws -> HTTPRequest {
        guard case .complete(let request) = HTTPRequest.parse(Data(raw.utf8)) else {
            throw XCTSkip("Anfrage unvollständig: \(raw)")
        }
        return request
    }

    func testHTTPParsing() throws {
        XCTAssertEqual(HTTPRequest.parse(Data("GET / HTTP/1.1\r\nHost: x".utf8)), .incomplete)
        XCTAssertEqual(HTTPRequest.parse(Data("POST /login HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc".utf8)), .incomplete)
        XCTAssertEqual(HTTPRequest.parse(Data("Unsinn\r\n\r\n".utf8)), .invalid)
        let parsed = try request("GET /callback?code=4%2F0A&state=s%2B1 HTTP/1.1\r\nHost: 127.0.0.1:5\r\n\r\n")
        XCTAssertEqual(parsed.query["code"], "4/0A")
        XCTAssertEqual(parsed.query["state"], "s+1")
        XCTAssertEqual(parsed.headers["host"], "127.0.0.1:5")
    }

    func testPasswordFlow() async throws {
        let hostinger = try XCTUnwrap(ProviderCatalog.shared.provider(id: "hostinger"))
        let router = BrowserLoginRouter(mode: .password(hostinger, emailHint: "info@firma.de"), port: 4000, state: "STATE") { _, password in
            password == "richtig" ? nil : "Falsches Passwort"
        }
        XCTAssertEqual(router.loginPageURL.absoluteString, "http://127.0.0.1:4000/login/STATE")

        // Falscher Host → abgelehnt (DNS-Rebinding).
        let evil = await router.handle(try request("GET /login/STATE HTTP/1.1\r\nHost: evil.com:4000\r\n\r\n"))
        XCTAssertEqual(evil.response.status, 403)

        let page = await router.handle(try request("GET /login/STATE HTTP/1.1\r\nHost: 127.0.0.1:4000\r\n\r\n"))
        XCTAssertEqual(page.response.status, 200)
        XCTAssertTrue(page.response.body.contains("Hostinger Mail"))
        XCTAssertTrue(page.response.body.contains("value=\"info@firma.de\""))
        XCTAssertFalse(page.response.body.contains("{{"))

        func post(_ body: String) throws -> HTTPRequest {
            try request("POST /login HTTP/1.1\r\nHost: localhost:4000\r\nContent-Length: \(body.utf8.count)\r\n\r\n\(body)")
        }
        let wrongState = await router.handle(try post("state=X&email=a%40b.de&password=richtig"))
        XCTAssertEqual(wrongState.response.status, 403)
        XCTAssertNil(wrongState.outcome)

        let wrongPassword = await router.handle(try post("state=STATE&email=a%40b.de&password=nein"))
        XCTAssertTrue(wrongPassword.response.body.contains("Falsches Passwort"))
        XCTAssertNil(wrongPassword.outcome)

        let success = await router.handle(try post("state=STATE&email=a%40b.de&password=richtig"))
        XCTAssertEqual(success.outcome, .success(.credentials(email: "a@b.de", password: "richtig")))
        let finished = await router.isFinished
        XCTAssertTrue(finished)
    }

    func testOAuthCallback() async throws {
        let router = BrowserLoginRouter(mode: .oauth, port: 4001, state: "S")
        let mismatch = await router.handle(try request("GET /callback?code=c&state=X HTTP/1.1\r\nHost: 127.0.0.1:4001\r\n\r\n"))
        XCTAssertEqual(mismatch.response.status, 400)
        XCTAssertNil(mismatch.outcome)

        let ok = await router.handle(try request("GET /callback?code=c0de&state=S HTTP/1.1\r\nHost: 127.0.0.1:4001\r\n\r\n"))
        XCTAssertEqual(ok.outcome, .success(.authorizationCode("c0de")))

        let denied = BrowserLoginRouter(mode: .oauth, port: 4002, state: "S")
        let result = await denied.handle(try request("GET /callback?error=access_denied&state=S HTTP/1.1\r\nHost: localhost:4002\r\n\r\n"))
        XCTAssertEqual(result.outcome, .failure(.authorizationDenied("access_denied")))
    }

    func testTemplateEscaping() {
        let html = LoginPages.render("<p>{{A}}</p>", ["A": "<script>\"&'"])
        XCTAssertEqual(html, "<p>&lt;script&gt;&quot;&amp;&#39;</p>")
    }
}

final class StoreTests: XCTestCase {
    func testAccountsAndRulesRoundTrip() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("lumail-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let paths = AppPaths(baseDirectory: dir)
        let accounts = [Account(providerId: "proton", email: "a@pm.me", displayName: "A", authMethod: .password)]
        try AccountStore(paths: paths).save(accounts)
        XCTAssertEqual(AccountStore(paths: paths).load(), accounts)

        try SenderRuleStore(paths: paths).save(["shop.de": .promotions])
        XCTAssertEqual(SenderRuleStore(paths: paths).load(), ["shop.de": .promotions])
    }
}
