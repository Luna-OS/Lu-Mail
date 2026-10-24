import XCTest
@testable import LuMailCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

final class PrivacyTests: XCTestCase {
    func testTrackerDetection() {
        let html = """
        <p>Hallo</p><img src="https://cdn.shop.de/logo.png" width="200">
        <IMG SRC='https://shop.us1.list-manage.com/track/open.php?u=1' alt="">
        <img src="https://pixel.example.org/o.gif" width="1" height="1">
        <img src="https://x.sendgrid.net/wf/open?upn=2">
        <img src="data:image/png;base64,AAAA" width="1">
        """
        XCTAssertEqual(TrackerDetector.shared.trackers(in: html), ["list-manage.com", "pixel.example.org", "sendgrid.net"])
        XCTAssertEqual(TrackerDetector.shared.trackers(in: "<img src=\"https://cdn.shop.de/a.png\">"), [])
    }

    func testPhishingWarnings() {
        let detector = PhishingDetector.shared
        XCTAssertEqual(detector.warnings(from: MailAddress(name: "PayPal Service", address: "info@secure-pay.xyz"), html: nil).count, 1)
        XCTAssertTrue(detector.warnings(from: MailAddress(name: "PayPal", address: "service@paypal.de"), html: nil).isEmpty)
        XCTAssertTrue(detector.warnings(from: MailAddress(name: "Amazon", address: "no-reply@marketplace.amazon.de"), html: nil).isEmpty)
        XCTAssertEqual(detector.warnings(from: MailAddress(name: "support@bank.de", address: "x@evil.ru"), html: nil).count, 1)
        let html = #"<a href="https://evil.example/login">https://www.sparkasse.de/login</a> <a href="https://shop.de/x">hier</a>"#
        XCTAssertEqual(detector.warnings(from: MailAddress(name: nil, address: "a@b.de"), html: html),
                       ["Ein Link zeigt „www.sparkasse.de“ an, führt aber zu „evil.example“."])
        XCTAssertTrue(detector.warnings(from: nil, html: #"<a href="https://www.shop.de/a">www.shop.de</a>"#).isEmpty)
    }

    func testUnsubscribeParsing() {
        XCTAssertEqual(UnsubscribeMethod.parse(listUnsubscribe: "<mailto:u@x.de?subject=stop>, <https://x.de/u?id=1>",
                                               listUnsubscribePost: "List-Unsubscribe=One-Click"),
                       .oneClick(URL(string: "https://x.de/u?id=1")!))
        XCTAssertEqual(UnsubscribeMethod.parse(listUnsubscribe: "<mailto:u@x.de?subject=Abmelden%20bitte>, <https://x.de/u>", listUnsubscribePost: nil),
                       .mail(address: "u@x.de", subject: "Abmelden bitte"))
        XCTAssertEqual(UnsubscribeMethod.parse(listUnsubscribe: "<https://x.de/u>", listUnsubscribePost: nil), .website(URL(string: "https://x.de/u")!))
        XCTAssertNil(UnsubscribeMethod.parse(listUnsubscribe: nil, listUnsubscribePost: nil))
        let request = UnsubscribeMethod.oneClickRequest(URL(string: "https://x.de/u")!)
        XCTAssertEqual(request.httpMethod, "POST")
        XCTAssertEqual(request.httpBody, Data("List-Unsubscribe=One-Click".utf8))
        XCTAssertFalse(request.httpShouldHandleCookies)
    }
}

final class ConversationTests: XCTestCase {
    let account = UUID()

    func message(_ uid: UInt32, _ subject: String, from: String, to: String = "me@x.de", id: String, inReplyTo: String? = nil,
                 references: String? = nil, minutes: Double) -> MessageSummary {
        MessageSummary(accountId: account, folderPath: "INBOX", uid: uid, from: MailAddress(name: nil, address: from),
                       to: [MailAddress(name: nil, address: to)], subject: subject, date: Date(timeIntervalSince1970: minutes * 60),
                       messageId: id, inReplyTo: inReplyTo, references: references)
    }

    func testGroupsByReferencesAndSubject() {
        let messages = [
            message(1, "Urlaub", from: "anna@x.de", id: "<1@x>", minutes: 1),
            message(2, "Re: Urlaub", from: "me@x.de", to: "anna@x.de", id: "<2@x>", inReplyTo: "<1@x>", minutes: 2),
            message(3, "AW: Re: Urlaub", from: "anna@x.de", id: "<3@x>", references: "<1@x> <2@x>", minutes: 3),
            message(4, "Newsletter", from: "news@shop.de", id: "<4@shop>", minutes: 4),
            message(5, "Newsletter", from: "news@shop.de", id: "<5@shop>", minutes: 5),
            // Ohne Verweise, aber Antwort-Betreff mit gemeinsamem Teilnehmer
            message(6, "Re: Urlaub", from: "anna@x.de", id: "<6@x>", minutes: 6),
        ]
        let conversations = ConversationBuilder.group(messages)
        XCTAssertEqual(conversations.count, 3)
        XCTAssertEqual(conversations[0].messages.map(\.uid), [6, 3, 2, 1])
        XCTAssertEqual(conversations[0].participants, ["anna@x.de", "me@x.de"])
        XCTAssertEqual(Set(conversations.dropFirst().map(\.count)), [1])
        XCTAssertEqual(ConversationBuilder.normalizedSubject("Re: AW: Fwd: WG: Hallo"), "hallo")
        XCTAssertEqual(ConversationBuilder.normalizedSubject("Re[2]: Hallo"), "hallo")
    }
}

final class FeatureStoreTests: XCTestCase {
    var paths: AppPaths!

    override func setUp() {
        paths = AppPaths(baseDirectory: FileManager.default.temporaryDirectory.appendingPathComponent("lumail-\(UUID().uuidString)"))
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: paths.baseDirectory)
    }

    func testContactsRanking() throws {
        var book = ContactBook()
        let old = Date(timeIntervalSince1970: 0)
        book.record([MailAddress(name: "Anna Schmidt", address: "Anna@x.de")], at: old)
        book.record([MailAddress(name: nil, address: "anna@x.de"), MailAddress(name: "Andreas", address: "andi@y.de")])
        book.record([MailAddress(name: nil, address: "bernd@z.de")])
        XCTAssertEqual(book.suggestions(for: "an").map(\.address), ["anna@x.de", "andi@y.de"])
        XCTAssertEqual(book.suggestions(for: "schmidt").first?.name, "Anna Schmidt")
        let store = ContactStore(paths: paths)
        try store.save(book)
        XCTAssertEqual(store.load().contacts.count, 3)
    }

    func testSnoozePresets() {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
        // Mittwoch, 21.10.2026 15:00
        let wednesday = calendar.date(from: DateComponents(year: 2026, month: 10, day: 21, hour: 15))!
        let evening = SnoozePreset.thisEvening.date(from: wednesday, calendar: calendar)
        XCTAssertEqual(calendar.dateComponents([.day, .hour], from: evening), DateComponents(day: 21, hour: 18))
        let late = calendar.date(from: DateComponents(year: 2026, month: 10, day: 21, hour: 20))!
        XCTAssertEqual(calendar.component(.day, from: SnoozePreset.thisEvening.date(from: late, calendar: calendar)), 22)
        XCTAssertEqual(calendar.dateComponents([.day, .hour], from: SnoozePreset.tomorrowMorning.date(from: wednesday, calendar: calendar)),
                       DateComponents(day: 22, hour: 8))
        XCTAssertEqual(calendar.dateComponents([.day, .weekday], from: SnoozePreset.nextMonday.date(from: wednesday, calendar: calendar)),
                       DateComponents(day: 26, weekday: 2))
        let monday = calendar.date(from: DateComponents(year: 2026, month: 10, day: 26, hour: 9))!
        XCTAssertEqual(calendar.component(.day, from: SnoozePreset.nextMonday.date(from: monday, calendar: calendar)), 2)
    }

    func testOutboxRoundTrip() throws {
        let store = OutboxStore(paths: paths)
        let message = OutgoingMessage(from: MailAddress(name: nil, address: "a@b.de"), to: [MailAddress(name: nil, address: "c@d.de")],
                                      subject: "Später", body: "Hallo")
        let entry = try store.add(message, accountId: UUID(), sendAt: Date(timeIntervalSinceNow: 3600))
        XCTAssertEqual(store.list().map(\.id), [entry.id])
        XCTAssertTrue(store.due().isEmpty)
        XCTAssertEqual(store.due(at: Date(timeIntervalSinceNow: 7200)).count, 1)
        XCTAssertEqual(MIMEParser.parse(try store.rawMessage(entry)).subject, "Später")
        store.remove(entry)
        XCTAssertTrue(store.list().isEmpty)
    }

    func testMessageCache() throws {
        let cache = MessageCache(paths: paths, maximumBytes: 10)
        let account = UUID()
        let summary = MessageSummary(accountId: account, folderPath: "INBOX", uid: 4, from: MailAddress(name: "A", address: "a@b.de"),
                                     subject: "Hi", date: Date(timeIntervalSince1970: 100), flags: ["\\Seen"], category: .promotions)
        try cache.saveAccount(CachedAccount(folders: [MailFolder(path: "INBOX", delimiter: "/", attributes: [])], messages: [summary]), id: account)
        let restored = try XCTUnwrap(cache.loadAccount(id: account)).restore(accountId: account)
        XCTAssertEqual(restored.messages, [summary])
        XCTAssertEqual(restored.folders.first?.role, .inbox)

        cache.saveBody(Data("12345".utf8), accountId: account, folder: "INBOX", uid: 1)
        XCTAssertEqual(cache.loadBody(accountId: account, folder: "INBOX", uid: 1), Data("12345".utf8))
        cache.saveBody(Data("1234567890".utf8), accountId: account, folder: "INBOX", uid: 2)
        // Begrenzung auf 10 Bytes: die ältere Datei fliegt raus.
        XCTAssertNil(cache.loadBody(accountId: account, folder: "INBOX", uid: 1))
        cache.removeAccount(account)
        XCTAssertNil(cache.loadAccount(id: account))
    }

    func testCleanupAndNewMail() {
        let account = UUID()
        let now = Date(timeIntervalSince1970: 100 * 86_400)
        func summary(_ uid: UInt32, _ category: MailCategory, daysAgo: Double, flagged: Bool = false, seen: Bool = false) -> MessageSummary {
            var flags: Set<String> = []
            if flagged { flags.insert("\\Flagged") }
            if seen { flags.insert("\\Seen") }
            return MessageSummary(accountId: account, folderPath: "INBOX", uid: uid, from: nil, subject: "",
                                  date: now.addingTimeInterval(-daysAgo * 86_400), flags: flags, category: category)
        }
        let messages = [summary(1, .promotions, daysAgo: 40), summary(2, .promotions, daysAgo: 10), summary(3, .promotions, daysAgo: 50, flagged: true),
                        summary(4, .primary, daysAgo: 400), summary(5, .social, daysAgo: 8)]
        XCTAssertEqual(CleanupPlanner.messagesToClean(messages, days: [.promotions: 30, .social: 7], now: now).map(\.uid), [1, 5])
        XCTAssertTrue(CleanupPlanner.messagesToClean(messages, days: [:], now: now).isEmpty)

        XCTAssertTrue(NewMailDetector.newMessages(previous: nil, current: messages).isEmpty)
        let fresh = [summary(6, .primary, daysAgo: 0), summary(7, .promotions, daysAgo: 0), summary(8, .updates, daysAgo: 0, seen: true)]
        XCTAssertEqual(NewMailDetector.newMessages(previous: [1, 2], current: messages + fresh).map(\.uid), [4, 6])
    }

    func testAttachmentsAndSignature() throws {
        let message = OutgoingMessage(
            from: MailAddress(name: nil, address: "a@b.de"), to: [MailAddress(name: nil, address: "c@d.de")], subject: "Anhang",
            body: MessageBuilder.bodyWithSignature("", signature: "Luna\nlu-mail"),
            attachments: [OutgoingAttachment(filename: "Rechnung März.pdf", mimeType: "application/pdf", data: Data("%PDF-1".utf8))])
        let data = MessageBuilder.build(message)
        let text = String(decoding: data, as: UTF8.self)
        XCTAssertTrue(text.contains("filename*=UTF-8''Rechnung%20M%C3%A4rz.pdf"))
        let parsed = MIMEParser.parse(data)
        XCTAssertEqual(parsed.textBody?.replacingOccurrences(of: "\r\n", with: "\n"), "\n\n-- \nLuna\nlu-mail")
        XCTAssertEqual(parsed.visibleAttachments.map(\.filename), ["Rechnung März.pdf"])
        XCTAssertEqual(parsed.visibleAttachments.first?.data, Data("%PDF-1".utf8))
        XCTAssertEqual(MessageBuilder.bodyWithSignature("x", signature: "  "), "x")
    }
}
