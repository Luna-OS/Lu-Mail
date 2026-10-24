import XCTest
@testable import LuMailCore

final class IMAPParserTests: XCTestCase {
    func testTokenizesFetchWithEnvelopeAndLiteral() throws {
        let header = "List-Unsubscribe: <mailto:u@x.de>\r\n\r\n"
        let raw = "12 FETCH (UID 345 FLAGS (\\Seen \\Flagged) INTERNALDATE \"17-Jul-2026 02:44:25 -0700\" RFC822.SIZE 4567 "
            + "ENVELOPE (\"Fri, 17 Jul 2026 09:44:25 +0000\" \"=?utf-8?B?R3LDvMOfZQ==?=\" ((\"Shop\" NIL \"news\" \"shop.de\")) "
            + "((\"Shop\" NIL \"news\" \"shop.de\")) NIL ((NIL NIL \"max\" \"gmail.com\")) NIL NIL NIL \"<abc@shop.de>\") "
            + "BODY[HEADER.FIELDS (LIST-UNSUBSCRIBE LIST-ID)] \(literal(header)))"
        let response = IMAPResponse.parse(Data(("* " + raw).utf8))
        XCTAssertEqual(response.kind, .untagged)
        XCTAssertEqual(response.keyword, "FETCH")
        XCTAssertEqual(response.leadingNumber, 12)

        let values = try response.values()
        let item = IMAPClient.parseFetch(sequence: 12, list: try XCTUnwrap(values.first?.list))
        XCTAssertEqual(item.uid, 345)
        XCTAssertEqual(item.flags, ["\\Seen", "\\Flagged"])
        XCTAssertEqual(item.size, 4567)
        XCTAssertNotNil(item.internalDate)
        XCTAssertEqual(item.envelope?.subject, "Grüße")
        XCTAssertEqual(item.envelope?.from.first, MailAddress(name: "Shop", address: "news@shop.de"))
        XCTAssertEqual(item.envelope?.to.first?.address, "max@gmail.com")
        XCTAssertEqual(item.envelope?.messageId, "<abc@shop.de>")
        XCTAssertEqual(item.headerSection.map { String(decoding: $0, as: UTF8.self) }, header)
    }

    func testParsesListWithModifiedUTF7() throws {
        let response = IMAPResponse.parse(Data("* LIST (\\HasNoChildren \\Sent) \"/\" \"&BB4EQgQ,BEAEMAQyBDsENQQ9BD0ESwQ1-\"".utf8))
        let values = try response.values()
        XCTAssertEqual(values[0].list?.compactMap(\.text), ["\\HasNoChildren", "\\Sent"])
        XCTAssertEqual(values[1].text, "/")
        XCTAssertEqual(ModifiedUTF7.decode(try XCTUnwrap(values[2].text)), "Отправленные")
    }

    func testModifiedUTF7RoundTrip() {
        for name in ["INBOX", "Entwürfe", "Отправленные", "A&B", "日本語/テスト", "Mixed Ä & Б"] {
            XCTAssertEqual(ModifiedUTF7.decode(ModifiedUTF7.encode(name)), name, name)
        }
        XCTAssertEqual(ModifiedUTF7.encode("A&B"), "A&-B")
        XCTAssertEqual(ModifiedUTF7.encode("Entwürfe"), "Entw&APw-rfe")
    }

    func testTrailingLiteralLength() {
        XCTAssertEqual(IMAPResponseReader.trailingLiteralLength(Data("* 1 FETCH (BODY[] {42}".utf8)), 42)
        XCTAssertEqual(IMAPResponseReader.trailingLiteralLength(Data("A1 APPEND x {7+}".utf8)), 7)
        XCTAssertNil(IMAPResponseReader.trailingLiteralLength(Data("* OK done".utf8)))
    }

    func testQuotedStringEscapes() throws {
        var tokenizer = IMAPTokenizer(Data(#"("a \"b\" \\c" NIL atom)"#.utf8))
        let value = try tokenizer.parseValue()
        XCTAssertEqual(value, .list([.string(Data(#"a "b" \c"#.utf8)), .nil_, .atom("atom")]))
    }
}

final class IMAPClientTests: XCTestCase {
    func testLoginListSelectFetch() async throws {
        let body = "Subject: Hallo\r\n\r\nText"
        let transport = ScriptedTransport(greeting: "* OK [CAPABILITY IMAP4rev1 MOVE UIDPLUS] ready\r\n") { command in
            switch true {
            case command.hasPrefix("LOGIN"):
                return "{tag} OK [CAPABILITY IMAP4rev1 MOVE UIDPLUS SPECIAL-USE] Logged in\r\n"
            case command.hasPrefix("LIST"):
                return "* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n* LIST (\\Noselect \\HasChildren) \"/\" \"[Gmail]\"\r\n"
                    + "* LIST (\\HasNoChildren \\Trash) \"/\" \"[Gmail]/Papierkorb\"\r\n{tag} OK done\r\n"
            case command.hasPrefix("SELECT"):
                return "* 3 EXISTS\r\n* OK [UIDVALIDITY 99] ok\r\n* OK [UIDNEXT 11] ok\r\n{tag} OK [READ-WRITE] selected\r\n"
            case command.hasPrefix("UID FETCH 10 (UID BODY.PEEK[])"):
                return "* 3 FETCH (UID 10 BODY[] \(literal(body)))\r\n{tag} OK fetched\r\n"
            case command.hasPrefix("UID MOVE"):
                return "{tag} OK moved\r\n"
            default:
                return "{tag} BAD unknown \(command)\r\n"
            }
        }
        let client = IMAPClient(transport: transport)
        try await client.connect()
        try await client.login(username: "max@gmail.com", password: "p\"w")
        XCTAssertTrue(client.capabilities.contains("SPECIAL-USE"))
        XCTAssertTrue(transport.sentLines.contains { $0.contains(#"LOGIN "max@gmail.com" "p\"w""#) })

        let entries = try await client.list()
        XCTAssertEqual(entries.map(\.path), ["INBOX", "[Gmail]", "[Gmail]/Papierkorb"])

        let status = try await client.select("INBOX")
        XCTAssertEqual(status, IMAPMailboxStatus(exists: 3, uidValidity: 99, uidNext: 11))

        let items = try await client.uidFetch(uidSet: "10", items: "UID BODY.PEEK[]")
        XCTAssertEqual(items.first?.fullBody.map { String(decoding: $0, as: UTF8.self) }, body)

        try await client.uidMove(uids: [10], to: "[Gmail]/Papierkorb")
        XCTAssertTrue(transport.sentLines.last?.contains(#"UID MOVE 10 "[Gmail]/Papierkorb""#) == true)
    }

    func testFailedLoginThrowsAuthenticationError() async throws {
        let transport = ScriptedTransport(greeting: "* OK ready\r\n") { command in
            if command.hasPrefix("CAPABILITY") { return "* CAPABILITY IMAP4rev1 AUTH=PLAIN\r\n{tag} OK\r\n" }
            return "{tag} NO [AUTHENTICATIONFAILED] Invalid credentials\r\n"
        }
        let client = IMAPClient(transport: transport)
        try await client.connect()
        do {
            try await client.login(username: "a@b.c", password: "falsch")
            XCTFail("Login hätte fehlschlagen müssen")
        } catch let error as IMAPError {
            XCTAssertEqual(error, .authenticationFailed("[AUTHENTICATIONFAILED] Invalid credentials"))
        }
    }

    func testXOAuth2FailureSendsEmptyContinuation() async throws {
        let transport = ScriptedTransport(greeting: "* OK [CAPABILITY IMAP4rev1 AUTH=XOAUTH2 SASL-IR] ready\r\n") { command in
            if command.hasPrefix("AUTHENTICATE") { return "+ eyJzdGF0dXMiOiI0MDAifQ==\r\n" }
            if command.isEmpty { return "L0001 NO [AUTHENTICATIONFAILED] Invalid token\r\n" }
            return nil
        }
        let client = IMAPClient(transport: transport)
        try await client.connect()
        do {
            try await client.authenticateXOAUTH2(username: "a@gmail.com", accessToken: "tok")
            XCTFail("XOAUTH2 hätte fehlschlagen müssen")
        } catch let error as IMAPError {
            guard case .authenticationFailed = error else { return XCTFail("falscher Fehler \(error)") }
        }
        XCTAssertEqual(transport.sentLines.last, "\r\n")
    }

    func testMoveFallbackWithoutMoveCapability() async throws {
        let transport = ScriptedTransport(greeting: "* OK [CAPABILITY IMAP4rev1] ready\r\n") { _ in "{tag} OK\r\n" }
        let client = IMAPClient(transport: transport)
        try await client.connect()
        try await client.uidMove(uids: [5, 6], to: "Trash")
        let commands = transport.sentLines.map { $0.split(separator: " ", maxSplits: 1).last.map(String.init) ?? "" }
        XCTAssertEqual(commands, ["UID COPY 5,6 \"Trash\"\r\n", "UID STORE 5,6 +FLAGS.SILENT (\\Deleted)\r\n", "EXPUNGE\r\n"])
    }

    func testAppendWaitsForLiteralContinuation() async throws {
        let transport = ScriptedTransport(greeting: "* OK [CAPABILITY IMAP4rev1] ready\r\n") { _ in "{tag} OK appended\r\n" }
        let client = IMAPClient(transport: transport)
        try await client.connect()
        try await client.append(Data("Subject: x\r\n\r\nHi\r\n".utf8), to: "INBOX.Sent", flags: ["\\Seen"])
        XCTAssertEqual(transport.sentLines.first, "L0001 APPEND \"INBOX.Sent\" (\\Seen) {18}\r\n")
        XCTAssertEqual(transport.sentLines.last, "Subject: x\r\n\r\nHi\r\n\r\n")
    }
}

final class SessionTests: XCTestCase {
    func testGmailInboxUsesServerCategories() async throws {
        let catalog = ProviderCatalog.shared
        let gmail = try XCTUnwrap(catalog.provider(id: "gmail"))
        let account = Account(providerId: "gmail", email: "max@gmail.com", displayName: "Max", authMethod: .password)

        @Sendable func fetchLine(_ seq: Int, uid: Int, from: String, subject: String) -> String {
            let parts = from.split(separator: "@")
            return "* \(seq) FETCH (UID \(uid) FLAGS () INTERNALDATE \"0\(seq)-Jan-2026 10:00:00 +0000\" RFC822.SIZE 10 "
                + "ENVELOPE (NIL \"\(subject)\" ((NIL NIL \"\(parts[0])\" \"\(parts[1])\")) NIL NIL NIL NIL NIL NIL NIL) "
                + "BODY[HEADER.FIELDS (LIST-UNSUBSCRIBE LIST-ID LIST-POST PRECEDENCE)] \(literal("\r\n")))\r\n"
        }

        let factory = ScriptedFactory { _ in
            ScriptedTransport(greeting: "* OK [CAPABILITY IMAP4rev1 X-GM-EXT-1] Gimap ready\r\n") { command in
                switch true {
                case command.hasPrefix("LOGIN"): return "{tag} OK [CAPABILITY IMAP4rev1 X-GM-EXT-1 MOVE] ok\r\n"
                case command.hasPrefix("LIST"): return "* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n{tag} OK\r\n"
                case command.hasPrefix("SELECT"): return "* 3 EXISTS\r\n{tag} OK\r\n"
                case command.hasPrefix("FETCH 1:3"):
                    return fetchLine(1, uid: 101, from: "freund@gmail.com", subject: "Hallo")
                        + fetchLine(2, uid: 102, from: "deals@shop.de", subject: "Angebot")
                        + fetchLine(3, uid: 103, from: "x@instagram.com", subject: "Neuer Follower")
                        + "{tag} OK\r\n"
                case command.contains("category:promotions"): return "* SEARCH 102\r\n{tag} OK\r\n"
                case command.contains("category:social"): return "* SEARCH 103\r\n{tag} OK\r\n"
                case command.contains("X-GM-RAW"): return "* SEARCH\r\n{tag} OK\r\n"
                default: return "{tag} BAD \(command)\r\n"
                }
            }
        }
        let session = MailAccountSession(account: account, provider: gmail, transports: factory) {
            .password(user: "max@gmail.com", password: "app-pw")
        }
        let messages = try await session.loadInbox(overrides: [:])
        let byUID = Dictionary(uniqueKeysWithValues: messages.map { ($0.uid, $0.category) })
        XCTAssertEqual(byUID[101], .primary)
        XCTAssertEqual(byUID[102], .promotions)
        XCTAssertEqual(byUID[103], .social)

        // Nutzer-Regel schlägt Server-Kategorie.
        let overridden = try await session.loadInbox(overrides: ["shop.de": .primary])
        XCTAssertEqual(overridden.first { $0.uid == 102 }?.category, .primary)
    }
}
