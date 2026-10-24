import XCTest
@testable import LuMailCore
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

final class UpdateTests: XCTestCase {
    let package = Data("dmg-inhalt".utf8)

    func release(tag: String, assets: [String]) -> String {
        let list = assets.map { #"{"name":"\#($0)","browser_download_url":"https://dl.example/\#($0)","size":10}"# }
        return #"{"tag_name":"\#(tag)","name":"Lu-Mail \#(tag)","body":"Neu: alles","html_url":"https://github.com/x/r","assets":[\#(list.joined(separator: ","))]}"#
    }

    func service(current: String, releaseJSON: String?, sums: String?, status: Int = 200) -> UpdateService {
        let package = package
        return UpdateService(currentVersion: SemanticVersion(current)!, platform: "macos") { request in
            let url = request.url!.absoluteString
            if url.hasPrefix("https://api.github.com") {
                guard let releaseJSON else { return (Data(), 404) }
                return (Data(releaseJSON.utf8), status)
            }
            if url.hasSuffix("SHA256SUMS.txt") { return (Data((sums ?? "").utf8), sums == nil ? 404 : 200) }
            return (package, 200)
        }
    }

    func testVersionParsingAndOrdering() {
        XCTAssertEqual(SemanticVersion("v1.2.3"), SemanticVersion(major: 1, minor: 2, patch: 3))
        XCTAssertNil(SemanticVersion("1.2"))
        XCTAssertNil(SemanticVersion("1.2.3-beta"))
        XCTAssertLessThan(SemanticVersion("0.9.10")!, SemanticVersion("0.10.0")!)
        XCTAssertTrue(SemanticVersion("0.0.0")!.isDevelopmentBuild)
        XCTAssertEqual(UpdateConfig.shared.assetSuffixes["macos"], "-macOS.dmg")
    }

    func testChecksumParsing() {
        let hash = String(repeating: "a", count: 64)
        let sums = UpdateService.parseChecksums("\(hash)  Lu-Mail-1.0.0-macOS.dmg\n\(hash.uppercased()) *Setup.exe\nkaputt\n")
        XCTAssertEqual(sums["Lu-Mail-1.0.0-macOS.dmg"], hash)
        XCTAssertEqual(sums["Setup.exe"], hash)
        XCTAssertEqual(sums.count, 2)
    }

    func testFindsNewerReleaseAndDownloadsVerifiedPackage() async throws {
        let hash = UpdateService.sha256Hex(of: package)
        let svc = service(current: "0.1.0",
                          releaseJSON: release(tag: "v0.2.0", assets: ["Lu-Mail-0.2.0-macOS.dmg", "Lu-Mail-0.2.0-Windows-x64-Setup.exe", "SHA256SUMS.txt"]),
                          sums: "\(hash)  Lu-Mail-0.2.0-macOS.dmg\n")
        let checked = try await svc.check()
        let update = try XCTUnwrap(checked)
        XCTAssertEqual(update.version, SemanticVersion("0.2.0"))
        XCTAssertEqual(update.assetName, "Lu-Mail-0.2.0-macOS.dmg")
        XCTAssertEqual(update.notes, "Neu: alles")

        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("lumail-update-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let file = try await svc.download(update, to: dir)
        XCTAssertEqual(try Data(contentsOf: file), package)
    }

    func testNoUpdateWhenCurrentOrNoRelease() async throws {
        let json = release(tag: "v0.2.0", assets: ["Lu-Mail-0.2.0-macOS.dmg", "SHA256SUMS.txt"])
        let same = try await service(current: "0.2.0", releaseJSON: json, sums: "").check()
        XCTAssertNil(same)
        let newer = try await service(current: "1.0.0", releaseJSON: json, sums: "").check()
        XCTAssertNil(newer)
        let none = try await service(current: "0.1.0", releaseJSON: nil, sums: nil).check()
        XCTAssertNil(none)
        let noMacAsset = try await service(current: "0.1.0", releaseJSON: release(tag: "v0.2.0", assets: ["x-Windows-x64-Setup.exe", "SHA256SUMS.txt"]), sums: "").check()
        XCTAssertNil(noMacAsset)
    }

    func testMissingChecksumIsRejected() async {
        let svc = service(current: "0.1.0", releaseJSON: release(tag: "v0.2.0", assets: ["Lu-Mail-0.2.0-macOS.dmg"]), sums: nil)
        do {
            _ = try await svc.check()
            XCTFail("ohne SHA256SUMS.txt darf es kein Update geben")
        } catch {
            XCTAssertEqual(error as? UpdateError, .checksumMissing("Lu-Mail-0.2.0-macOS.dmg"))
        }
    }

    func testChecksumMismatchIsRejected() async throws {
        let svc = service(current: "0.1.0",
                          releaseJSON: release(tag: "v0.2.0", assets: ["Lu-Mail-0.2.0-macOS.dmg", "SHA256SUMS.txt"]),
                          sums: "\(String(repeating: "0", count: 64))  Lu-Mail-0.2.0-macOS.dmg")
        let checked = try await svc.check()
        let update = try XCTUnwrap(checked)
        do {
            _ = try await svc.download(update, to: FileManager.default.temporaryDirectory)
            XCTFail("falsche Prüfsumme muss abgelehnt werden")
        } catch {
            XCTAssertEqual(error as? UpdateError, .checksumMismatch)
        }
    }
}
