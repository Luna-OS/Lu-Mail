import XCTest
@testable import LuMailCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

final class DiscoveryTests: XCTestCase {
    final class Recorder: @unchecked Sendable { var urls: [String] = [] }

    func discovery(mx: String?, autoconfig: [String: String] = [:], recorder: Recorder = Recorder()) -> ProviderDiscovery {
        ProviderDiscovery { request in
            let url = request.url!.absoluteString
            recorder.urls.append(url)
            if url.hasPrefix("https://cloudflare-dns.com") {
                guard let mx else { return (Data(), 500) }
                return (Data(mx.utf8), 200)
            }
            for (prefix, body) in autoconfig where url.hasPrefix(prefix) { return (Data(body.utf8), 200) }
            return (Data(), 404)
        }
    }

    func testKnownDomainNeedsNoNetwork() async {
        let recorder = Recorder()
        let result = await discovery(mx: nil, recorder: recorder).discover(email: "max@gmx.de")
        XCTAssertEqual(result.provider.id, "gmx")
        XCTAssertEqual(result.source, .knownDomain)
        XCTAssertTrue(recorder.urls.isEmpty)
    }

    func testWorkspaceAndHostingerViaMX() async {
        let workspace = #"{"Status":0,"Answer":[{"type":15,"data":"5 alt1.aspmx.l.google.com."},{"type":15,"data":"1 aspmx.l.google.com."}]}"#
        let gmail = await discovery(mx: workspace).discover(email: "chef@firma.de")
        XCTAssertEqual(gmail.provider.id, "gmail")
        XCTAssertEqual(gmail.source, .mx)

        let hostinger = #"{"Answer":[{"type":15,"data":"10 mx1.hostinger.com."}]}"#
        let result = await discovery(mx: hostinger).discover(email: "info@meinshop.de")
        XCTAssertEqual(result.provider.id, "hostinger")

        let microsoft = #"{"Answer":[{"type":15,"data":"0 firma-de.mail.protection.outlook.com."}]}"#
        let outlook = await discovery(mx: microsoft).discover(email: "a@firma.de")
        XCTAssertEqual(outlook.provider.id, "outlook")
    }

    func testAutoconfigPrefillsCustomServers() async {
        let xml = """
        <?xml version="1.0"?>
        <clientConfig version="1.1"><emailProvider id="example.org"><domain>example.org</domain>
          <incomingServer type="pop3"><hostname>pop.example.org</hostname><port>995</port><socketType>SSL</socketType></incomingServer>
          <incomingServer type="imap"><hostname>imap.example.org</hostname><port>143</port><socketType>plain</socketType></incomingServer>
          <incomingServer type="imap"><hostname>IMAP.example.org</hostname><port>993</port><socketType>SSL</socketType></incomingServer>
          <outgoingServer type="smtp"><hostname>smtp.example.org</hostname><port>587</port><socketType>STARTTLS</socketType></outgoingServer>
        </emailProvider></clientConfig>
        """
        let recorder = Recorder()
        let result = await discovery(mx: #"{"Answer":[{"data":"10 mx.example.org."}]}"#,
                                     autoconfig: ["https://autoconfig.thunderbird.net/v1.1/example.org": xml],
                                     recorder: recorder).discover(email: "ich@example.org")
        XCTAssertEqual(result.provider.id, "custom")
        XCTAssertEqual(result.source, .autoconfig)
        XCTAssertEqual(result.servers, CustomServers(imap: ServerEndpoint(host: "imap.example.org", port: 993, security: "tls"),
                                                     smtp: ServerEndpoint(host: "smtp.example.org", port: 587, security: "starttls")))
        XCTAssertTrue(recorder.urls.contains("https://autoconfig.example.org/mail/config-v1.1.xml?emailaddress=ich%40example.org"))
    }

    func testNothingFoundFallsBackToCustom() async {
        let result = await discovery(mx: nil).discover(email: "a@unbekannt.example")
        XCTAssertEqual(result.provider.id, "custom")
        XCTAssertNil(result.servers)
        XCTAssertEqual(result.source, .none)
    }

    func testSetupGuidesAndBuiltInOAuth() throws {
        let catalog = ProviderCatalog.shared
        for provider in catalog.providers {
            XCTAssertFalse(provider.setup?.passwordSteps.isEmpty ?? true, provider.id)
        }
        XCTAssertEqual(catalog.provider(id: "proton")?.setup?.checkLocalPort, 1143)
        XCTAssertNotNil(catalog.discovery)

        let url = FileManager.default.temporaryDirectory.appendingPathComponent("oauth-\(UUID().uuidString).json")
        try Data(#"{"gmail":{"clientId":"user-id"}}"#.utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let builtIn = #"{"gmail":{"clientId":"built-in"},"outlook":{"clientId":"ms-id"}}"#
        let resolved = OAuthClientCredentials.resolve(userFile: url, builtIn: builtIn)
        XCTAssertEqual(resolved["gmail"]?.clientId, "user-id")
        XCTAssertEqual(resolved["outlook"]?.clientId, "ms-id")
        XCTAssertTrue(OAuthClientCredentials.resolve(userFile: url.appendingPathExtension("fehlt"), builtIn: "{}").isEmpty)
    }
}
