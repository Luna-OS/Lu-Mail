import XCTest
@testable import LuMailCore

final class MIMETests: XCTestCase {
    func testEncodedWords() {
        XCTAssertEqual(EncodedWords.decode("=?UTF-8?B?R3LDvMOfZSBhdXMgS8O2bG4=?="), "Grüße aus Köln")
        XCTAssertEqual(EncodedWords.decode("=?iso-8859-1?Q?Gr=FC=DFe_aus_K=F6ln?="), "Grüße aus Köln")
        // Zwei aufeinanderfolgende Encoded Words ohne Leerzeichen dazwischen.
        XCTAssertEqual(EncodedWords.decode("=?utf-8?Q?Hallo_?= =?utf-8?Q?Welt?="), "Hallo Welt")
        XCTAssertEqual(EncodedWords.decode("Re: =?utf-8?Q?Caf=C3=A9?= heute"), "Re: Café heute")
        // KOI8-R (ältere Mail.ru-Nachrichten): „Привет“
        XCTAssertEqual(EncodedWords.decode("=?koi8-r?B?8NLJ18XU?="), "Привет")
        // windows-1251: „Привет“
        XCTAssertEqual(EncodedWords.decode("=?windows-1251?B?z/Do4uXy?="), "Привет")
        XCTAssertEqual(EncodedWords.decode("kein =? encoded"), "kein =? encoded")
    }

    func testEncodeSubjectRoundTrip() {
        let subject = "Größere Bestellung – Привет 🌙 und ein ziemlich langer Betreff, der umbrechen muss"
        let encoded = EncodedWords.encode(subject)
        XCTAssertTrue(encoded.components(separatedBy: "\r\n").allSatisfy { $0.count <= 76 })
        XCTAssertEqual(EncodedWords.decode(encoded.replacingOccurrences(of: "\r\n", with: "")), subject)
        XCTAssertEqual(EncodedWords.encode("Plain ASCII"), "Plain ASCII")
    }

    func testQuotedPrintable() {
        let decoded = QuotedPrintable.decode(Data("Gr=C3=BC=C3=9Fe, das ist eine sehr lange Zeile mit weichem =\r\nUmbruch".utf8))
        XCTAssertEqual(String(decoding: decoded, as: UTF8.self), "Grüße, das ist eine sehr lange Zeile mit weichem Umbruch")
    }

    func testMultipartAlternativeWithInlineImageAndAttachment() throws {
        let png = Data([0x89, 0x50, 0x4E, 0x47]).base64EncodedString()
        let raw = """
        From: =?utf-8?Q?J=C3=BCrgen?= <juergen@example.de>
        To: max@gmail.com, "Müller, Anna" <anna@example.de>
        Subject: =?utf-8?B?UmVjaG51bmc=?=
        Date: Tue, 20 Oct 2026 14:03:00 +0200
        Message-ID: <id-1@example.de>
        Content-Type: multipart/mixed; boundary="outer"

        --outer
        Content-Type: multipart/related; boundary=rel

        --rel
        Content-Type: multipart/alternative; boundary="alt"

        --alt
        Content-Type: text/plain; charset=utf-8
        Content-Transfer-Encoding: quoted-printable

        Hallo M=C3=A4x
        --alt
        Content-Type: text/html; charset=utf-8

        <p>Hallo <b>Mäx</b><img src="cid:logo@x"></p>
        --alt--
        --rel
        Content-Type: image/png
        Content-ID: <logo@x>
        Content-Transfer-Encoding: base64

        \(png)
        --rel--
        --outer
        Content-Type: application/pdf
        Content-Disposition: attachment; filename*=UTF-8''Rechnung%20M%C3%A4rz.pdf
        Content-Transfer-Encoding: base64

        JVBERi0=
        --outer--
        """.replacingOccurrences(of: "\n", with: "\r\n")

        let message = MIMEParser.parse(Data(raw.utf8))
        XCTAssertEqual(message.subject, "Rechnung")
        XCTAssertEqual(message.from, [MailAddress(name: "Jürgen", address: "juergen@example.de")])
        XCTAssertEqual(message.to.map(\.address), ["max@gmail.com", "anna@example.de"])
        XCTAssertEqual(message.to.last?.name, "Müller, Anna")
        XCTAssertEqual(message.textBody, "Hallo Mäx")
        XCTAssertNotNil(message.date)
        XCTAssertEqual(message.messageId, "<id-1@example.de>")
        let html = try XCTUnwrap(message.htmlBody)
        XCTAssertTrue(html.contains("data:image/png;base64,\(png)"), html)
        XCTAssertFalse(html.contains("cid:"))
        XCTAssertEqual(message.visibleAttachments.map(\.filename), ["Rechnung März.pdf"])
        XCTAssertEqual(message.visibleAttachments.first?.data, Data("%PDF-".utf8))
        XCTAssertEqual(message.preview, "Hallo Mäx")
    }

    func testCharsetWindows1251Body() {
        var raw = Data("Content-Type: text/plain; charset=windows-1251\r\n\r\n".utf8)
        raw.append(Data([0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2]))
        XCTAssertEqual(MIMEParser.parse(raw).textBody, "Привет")
    }

    func testAddressParsing() {
        XCTAssertEqual(AddressParser.parseList("a@b.de, Max <max@x.de>, \"Doe, J\" <j@d.com>, kaputt").map(\.address),
                       ["a@b.de", "max@x.de", "j@d.com"])
        XCTAssertEqual(AddressParser.parseSingle("max@x.de (Max M)")?.name, "Max M")
    }

    func testMessageBuilderRoundTrip() throws {
        let outgoing = OutgoingMessage(
            from: MailAddress(name: "Lü Na", address: "luna@example.de"),
            to: [MailAddress(name: nil, address: "a@b.de")],
            cc: [MailAddress(name: "Иван", address: "ivan@mail.ru")],
            bcc: [MailAddress(name: nil, address: "geheim@x.de")],
            subject: "Grüße 🌙",
            body: "Zeile 1\n.Punktzeile\nПривет",
            inReplyTo: "<orig@x>")
        let data = MessageBuilder.build(outgoing, messageId: "<test@lumail>")
        let text = String(decoding: data, as: UTF8.self)
        XCTAssertFalse(text.contains("geheim@x.de"), "Bcc darf nicht in den Headern stehen")
        XCTAssertTrue(text.contains("In-Reply-To: <orig@x>"))
        XCTAssertTrue(text.contains("Message-ID: <test@lumail>"))

        let parsed = MIMEParser.parse(data)
        XCTAssertEqual(parsed.subject, "Grüße 🌙")
        XCTAssertEqual(parsed.from.first?.name, "Lü Na")
        XCTAssertEqual(parsed.cc.first?.name, "Иван")
        XCTAssertEqual(parsed.textBody?.replacingOccurrences(of: "\r\n", with: "\n"), "Zeile 1\n.Punktzeile\nПривет")
    }

    func testDotStuffing() {
        let stuffed = SMTPClient.dotStuff(Data("a\r\n.b\r\n..c".utf8))
        XCTAssertEqual(String(decoding: stuffed, as: UTF8.self), "a\r\n..b\r\n...c\r\n.\r\n")
    }

    func testSubjectPrefixes() {
        XCTAssertEqual(MessageBuilder.prefixedSubject("Hallo", prefix: "Re:"), "Re: Hallo")
        XCTAssertEqual(MessageBuilder.prefixedSubject("AW: Hallo", prefix: "Re:"), "AW: Hallo")
        XCTAssertEqual(MessageBuilder.prefixedSubject("Hallo", prefix: "Fwd:"), "Fwd: Hallo")
    }

    func testHTMLPreparationBlocksRemoteContent() {
        let html = MailHTML.prepare(html: "<img src=\"https://tracker/x.gif\">", text: nil, allowRemoteContent: false, darkMode: false)
        XCTAssertTrue(html.contains("img-src data: cid:"))
        let plain = MailHTML.prepare(html: nil, text: "<b> & https://lu.example/x?a=1", allowRemoteContent: false, darkMode: true)
        XCTAssertTrue(plain.contains("&lt;b&gt;"))
        XCTAssertTrue(plain.contains("<a href=\"https://lu.example/x?a=1\">"))
    }

    func testDateParsing() {
        XCTAssertNotNil(RFC5322Date.parse("Tue, 20 Oct 2026 14:03:00 +0200 (CEST)"))
        XCTAssertNotNil(RFC5322Date.parse("20 Oct 2026 14:03:00 +0200"))
        XCTAssertNotNil(RFC5322Date.parseInternalDate("17-Jul-1996 02:44:25 -0700"))
    }
}
