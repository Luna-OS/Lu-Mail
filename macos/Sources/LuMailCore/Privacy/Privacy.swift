import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Hilfen zum Untersuchen von HTML (ohne vollständigen Parser – robust genug für Mails).
enum HTMLScan {
    /// Alle Tags eines Namens mit ihren Attributen (Namen klein geschrieben).
    static func tags(named name: String, in html: String) -> [[String: String]] {
        var result: [[String: String]] = []
        let lower = html.lowercased()
        var searchStart = lower.startIndex
        while let range = lower.range(of: "<\(name)", range: searchStart..<lower.endIndex) {
            let after = range.upperBound
            guard after < lower.endIndex, lower[after] == " " || lower[after] == "\n" || lower[after] == "\t" || lower[after] == "/" || lower[after] == ">" else {
                searchStart = after
                continue
            }
            guard let end = lower[after...].firstIndex(of: ">") else { break }
            // Original-Schreibweise der Attributwerte behalten.
            let offsetStart = lower.distance(from: lower.startIndex, to: after)
            let offsetEnd = lower.distance(from: lower.startIndex, to: end)
            let original = String(html[html.index(html.startIndex, offsetBy: offsetStart)..<html.index(html.startIndex, offsetBy: offsetEnd)])
            result.append(attributes(original))
            searchStart = end
        }
        return result
    }

    static func attributes(_ text: String) -> [String: String] {
        var result: [String: String] = [:]
        var index = text.startIndex
        func skipSpace() { while index < text.endIndex, text[index].isWhitespace || text[index] == "/" { index = text.index(after: index) } }
        while index < text.endIndex {
            skipSpace()
            let nameStart = index
            while index < text.endIndex, !text[index].isWhitespace, text[index] != "=", text[index] != "/" { index = text.index(after: index) }
            let name = text[nameStart..<index].lowercased()
            skipSpace()
            var value = ""
            if index < text.endIndex, text[index] == "=" {
                index = text.index(after: index)
                skipSpace()
                if index < text.endIndex, text[index] == "\"" || text[index] == "'" {
                    let quote = text[index]
                    index = text.index(after: index)
                    let valueStart = index
                    while index < text.endIndex, text[index] != quote { index = text.index(after: index) }
                    value = String(text[valueStart..<index])
                    if index < text.endIndex { index = text.index(after: index) }
                } else {
                    let valueStart = index
                    while index < text.endIndex, !text[index].isWhitespace { index = text.index(after: index) }
                    value = String(text[valueStart..<index])
                }
            }
            if !name.isEmpty { result[name] = HTMLText.decodeEntities(value) }
            if index == nameStart { index = text.index(after: index) }
        }
        return result
    }

    /// `<a href="…">Text</a>`: Paare aus Ziel und sichtbarem Text.
    static func links(in html: String) -> [(href: String, text: String)] {
        var result: [(String, String)] = []
        var rest = Substring(html)
        while let open = rest.range(of: "<a", options: .caseInsensitive) {
            guard let tagEnd = rest[open.upperBound...].firstIndex(of: ">") else { break }
            let attrs = attributes(String(rest[open.upperBound..<tagEnd]))
            let afterTag = rest.index(after: tagEnd)
            guard let close = rest[afterTag...].range(of: "</a", options: .caseInsensitive) else { break }
            let inner = HTMLText.stripTags(String(rest[afterTag..<close.lowerBound])).trimmingCharacters(in: .whitespacesAndNewlines)
            if let href = attrs["href"] { result.append((href, inner)) }
            rest = rest[close.upperBound...]
        }
        return result
    }

    static func host(of urlString: String) -> String? {
        var text = urlString.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix("//") { text = "https:" + text }
        if text.lowercased().hasPrefix("www.") { text = "https://" + text }
        return URLComponents(string: text)?.host?.lowercased()
    }

    /// „mail.shop.example.co.uk“ passt zu „example.co.uk“, „shop.example.co.uk“ …
    static func host(_ host: String, isWithin domain: String) -> Bool {
        let domain = domain.lowercased()
        return host == domain || host.hasSuffix("." + domain)
    }
}

/// Erkennt Tracking-Pixel (docs/FUNKTIONEN.md §14).
public struct TrackerDetector: Sendable {
    let domains: [String]

    private struct File: Decodable { var domains: [String] }

    public init(json: String = SharedResources.trackersJSON) {
        domains = (try? JSONDecoder().decode(File.self, from: Data(json.utf8)))?.domains
            .map { $0.split(separator: "/").first.map(String.init) ?? $0 } ?? []
    }

    public static let shared = TrackerDetector()

    /// Hosts der erkannten Tracker (ohne Doppelte, in Reihenfolge des Auftretens).
    public func trackers(in html: String) -> [String] {
        var found: [String] = []
        for image in HTMLScan.tags(named: "img", in: html) {
            guard let src = image["src"], let host = HTMLScan.host(of: src) else { continue }
            let width = Int(image["width"]?.replacingOccurrences(of: "px", with: "") ?? "")
            let height = Int(image["height"]?.replacingOccurrences(of: "px", with: "") ?? "")
            let style = (image["style"] ?? "").replacingOccurrences(of: " ", with: "").lowercased()
            let tiny = (width.map { $0 <= 1 } ?? false) || (height.map { $0 <= 1 } ?? false)
                || style.contains("width:1px") || style.contains("height:1px") || style.contains("display:none")
            let known = domains.first { HTMLScan.host(host, isWithin: $0) }
            if tiny || known != nil {
                let label = known ?? host
                if !found.contains(label) { found.append(label) }
            }
        }
        return found
    }
}

/// Phishing-Warnungen (docs/FUNKTIONEN.md §14).
public struct PhishingDetector: Sendable {
    struct Brand: Decodable, Sendable {
        var name: String
        var keywords: [String]
        var domains: [String]
    }

    private struct File: Decodable { var brands: [Brand] }

    let brands: [Brand]

    public init(json: String = SharedResources.phishingJSON) {
        brands = (try? JSONDecoder().decode(File.self, from: Data(json.utf8)))?.brands ?? []
    }

    public static let shared = PhishingDetector()

    public func warnings(from sender: MailAddress?, html: String?) -> [String] {
        var result: [String] = []
        if let sender {
            let domain = sender.domain
            let name = (sender.name ?? "").lowercased()
            // 1. Adresse im Anzeigenamen mit anderer Domain
            if let embedded = Self.firstEmail(in: name), let embeddedDomain = embedded.split(separator: "@").last,
               String(embeddedDomain) != domain {
                result.append("Der Absendername enthält „\(embedded)“, die Mail kommt aber von „\(sender.address)“.")
            }
            // 2. Bekannte Marke im Namen, aber fremde Domain
            if !domain.isEmpty, let brand = brands.first(where: { brand in brand.keywords.contains { name.contains($0) } }),
               !brand.domains.contains(where: { HTMLScan.host(domain, isWithin: $0) }) {
                result.append("Die Mail gibt sich als \(brand.name) aus, kommt aber von „\(domain)“.")
            }
        }
        // 3. Link-Text zeigt eine andere Adresse als das Ziel
        if let html {
            for link in HTMLScan.links(in: html) {
                let text = link.text.lowercased()
                guard text.hasPrefix("http") || text.hasPrefix("www."),
                      let shown = HTMLScan.host(of: text), let target = HTMLScan.host(of: link.href) else { continue }
                if !HTMLScan.host(target, isWithin: shown) && !HTMLScan.host(shown, isWithin: target) {
                    result.append("Ein Link zeigt „\(shown)“ an, führt aber zu „\(target)“.")
                    break
                }
            }
        }
        return result
    }

    static func firstEmail(in text: String) -> String? {
        let pattern = "[a-z0-9._%+-]+@[a-z0-9.-]+\\.[a-z]{2,}"
        guard let range = text.range(of: pattern, options: .regularExpression) else { return nil }
        return String(text[range])
    }
}

/// Wie man einen Newsletter abbestellt (docs/FUNKTIONEN.md §11, RFC 2369 / RFC 8058).
public enum UnsubscribeMethod: Equatable, Sendable {
    /// `POST` mit `List-Unsubscribe=One-Click`.
    case oneClick(URL)
    case mail(address: String, subject: String)
    case website(URL)

    public static func parse(listUnsubscribe: String?, listUnsubscribePost: String?) -> UnsubscribeMethod? {
        guard let header = listUnsubscribe else { return nil }
        // Einträge stehen in spitzen Klammern: <mailto:…>, <https://…>
        var entries: [String] = []
        var rest = Substring(header)
        while let open = rest.firstIndex(of: "<"), let close = rest[open...].firstIndex(of: ">") {
            entries.append(String(rest[rest.index(after: open)..<close]).trimmingCharacters(in: .whitespaces))
            rest = rest[rest.index(after: close)...]
        }
        if entries.isEmpty { entries = header.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) } }
        let https = entries.compactMap { URL(string: $0) }.first { $0.scheme?.lowercased() == "https" }
        let oneClick = listUnsubscribePost?.lowercased().replacingOccurrences(of: " ", with: "").contains("list-unsubscribe=one-click") ?? false
        if let https, oneClick { return .oneClick(https) }
        if let mailto = entries.first(where: { $0.lowercased().hasPrefix("mailto:") }) {
            let body = mailto.dropFirst("mailto:".count)
            let parts = body.split(separator: "?", maxSplits: 1)
            let address = String(parts.first ?? "").removingPercentEncoding ?? String(parts.first ?? "")
            var subject = "unsubscribe"
            if parts.count > 1 {
                for pair in parts[1].split(separator: "&") {
                    let kv = pair.split(separator: "=", maxSplits: 1)
                    if kv.count == 2, kv[0].lowercased() == "subject" {
                        subject = String(kv[1]).removingPercentEncoding ?? String(kv[1])
                    }
                }
            }
            if address.contains("@") { return .mail(address: address, subject: subject) }
        }
        if let https { return .website(https) }
        if let http = entries.compactMap({ URL(string: $0) }).first(where: { $0.scheme?.lowercased() == "http" }) {
            return .website(http)
        }
        return nil
    }

    /// Die Anfrage für Ein-Klick-Abbestellen (ohne Cookies).
    public static func oneClickRequest(_ url: URL) -> URLRequest {
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.httpShouldHandleCookies = false
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.httpBody = Data("List-Unsubscribe=One-Click".utf8)
        return request
    }
}
