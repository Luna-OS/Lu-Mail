import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if canImport(FoundationXML)
import FoundationXML
#endif

/// Ergebnis der Anbieter-Erkennung (docs/FUNKTIONEN.md §17).
public struct DiscoveryResult: Sendable, Equatable {
    public enum Source: String, Sendable {
        case knownDomain, mx, autoconfig, none
    }

    public var provider: Provider
    /// Vorausgefüllte Server bei „Andere (IMAP)“.
    public var servers: CustomServers?
    public var source: Source
}

/// Erkennt den Anbieter zu einer E-Mail-Adresse – auch bei eigenen Domains.
public struct ProviderDiscovery: Sendable {
    public typealias Fetch = @Sendable (URLRequest) async throws -> (Data, Int)

    let catalog: ProviderCatalog
    let fetch: Fetch

    public init(catalog: ProviderCatalog = .shared, fetch: @escaping Fetch = UpdateService.urlSessionFetch) {
        self.catalog = catalog
        self.fetch = fetch
    }

    public func discover(email: String) async -> DiscoveryResult {
        let address = MailAddress(name: nil, address: email.trimmingCharacters(in: .whitespaces))
        let domain = address.domain
        guard let custom = catalog.provider(id: "custom") else { fatalError("providers.json braucht „custom“") }
        if let provider = catalog.provider(forEmail: address.address) {
            return DiscoveryResult(provider: provider, servers: nil, source: .knownDomain)
        }
        guard !domain.isEmpty, let config = catalog.discovery else {
            return DiscoveryResult(provider: custom, servers: nil, source: .none)
        }
        let deadline = Date().addingTimeInterval(config.timeoutSeconds)

        // MX-Eintrag → bekannter Anbieter (Google Workspace, Hostinger, Microsoft 365 …)
        for host in await mxHosts(domain: domain, template: config.mxLookup) {
            if let provider = catalog.provider(forMXHost: host), provider.id != "custom" {
                return DiscoveryResult(provider: provider, servers: nil, source: .mx)
            }
        }

        // Autoconfig (Thunderbird-Format)
        for template in config.autoconfig where Date() < deadline {
            guard let url = URL(string: Self.fill(template, domain: domain, email: address.address)), url.scheme == "https" else { continue }
            var request = URLRequest(url: url)
            request.timeoutInterval = max(1, deadline.timeIntervalSinceNow)
            guard let (data, status) = try? await fetch(request), status == 200,
                  let servers = AutoconfigParser.parse(data) else { continue }
            return DiscoveryResult(provider: custom, servers: servers, source: .autoconfig)
        }
        return DiscoveryResult(provider: custom, servers: nil, source: .none)
    }

    func mxHosts(domain: String, template: String) async -> [String] {
        guard let url = URL(string: Self.fill(template, domain: domain, email: "")) else { return [] }
        var request = URLRequest(url: url)
        request.setValue("application/dns-json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = 4
        guard let (data, status) = try? await fetch(request), status == 200 else { return [] }
        return Self.parseMX(data)
    }

    /// DNS-over-HTTPS-JSON: `{"Answer":[{"type":15,"data":"10 aspmx.l.google.com."}]}` → Hosts nach Priorität.
    static func parseMX(_ data: Data) -> [String] {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let answers = object["Answer"] as? [[String: Any]] else { return [] }
        let records: [(Int, String)] = answers.compactMap { answer in
            guard let value = answer["data"] as? String else { return nil }
            let parts = value.split(separator: " ")
            guard parts.count == 2, let priority = Int(parts[0]) else { return nil }
            var host = String(parts[1]).lowercased()
            if host.hasSuffix(".") { host.removeLast() }
            return (priority, host)
        }
        return records.sorted { $0.0 < $1.0 }.map(\.1)
    }

    static func fill(_ template: String, domain: String, email: String) -> String {
        let encode = { (text: String) in text.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed.subtracting(CharacterSet(charactersIn: "+&=@"))) ?? text }
        return template.replacingOccurrences(of: "{domain}", with: encode(domain))
            .replacingOccurrences(of: "{email}", with: encode(email))
    }
}

/// Liest Thunderbird-Autoconfig-XML (`clientConfig`).
public enum AutoconfigParser {
    public static func parse(_ data: Data) -> CustomServers? {
        let delegate = Delegate()
        let parser = XMLParser(data: data)
        parser.delegate = delegate
        guard parser.parse() else { return nil }
        guard let imap = delegate.servers.first(where: { $0.kind == "incomingServer" && $0.type == "imap" && $0.endpoint != nil })?.endpoint,
              let smtp = delegate.servers.first(where: { $0.kind == "outgoingServer" && $0.type == "smtp" && $0.endpoint != nil })?.endpoint else {
            return nil
        }
        return CustomServers(imap: imap, smtp: smtp)
    }

    struct Server {
        var kind: String
        var type: String
        var hostname = ""
        var port = 0
        var socketType = ""

        var endpoint: ServerEndpoint? {
            let security: String
            switch socketType.uppercased() {
            case "SSL": security = "tls"
            case "STARTTLS": security = "starttls"
            default: return nil  // Klartext wird nie verwendet
            }
            guard !hostname.isEmpty, (1...65535).contains(port) else { return nil }
            return ServerEndpoint(host: hostname.lowercased(), port: port, security: security)
        }
    }

    final class Delegate: NSObject, XMLParserDelegate {
        var servers: [Server] = []
        private var current: Server?
        private var text = ""

        func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?,
                    qualifiedName qName: String?, attributes attributeDict: [String: String] = [:]) {
            text = ""
            if elementName == "incomingServer" || elementName == "outgoingServer" {
                current = Server(kind: elementName, type: (attributeDict["type"] ?? "").lowercased())
            }
        }

        func parser(_ parser: XMLParser, foundCharacters string: String) {
            text += string
        }

        func parser(_ parser: XMLParser, didEndElement elementName: String, namespaceURI: String?, qualifiedName qName: String?) {
            let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
            switch elementName {
            case "hostname": current?.hostname = value
            case "port": current?.port = Int(value) ?? 0
            case "socketType": current?.socketType = value
            case "incomingServer", "outgoingServer":
                if let current { servers.append(current) }
                current = nil
            default: break
            }
            text = ""
        }
    }
}
