import Foundation
import LuMailCore
import Security

/// Was pro Konto geheim gespeichert wird.
struct AccountSecret: Codable, Equatable {
    var password: String?
    var tokens: OAuthTokens?
}

enum KeychainError: Error, LocalizedError {
    case status(OSStatus)

    var errorDescription: String? {
        switch self {
        case .status(let status):
            let message = SecCopyErrorMessageString(status, nil) as String? ?? "Code \(status)"
            return "Schlüsselbund-Fehler: \(message)"
        }
    }
}

/// Speichert Passwörter und Tokens im macOS-Schlüsselbund (Dienst `de.lunaos.lumail`).
enum Keychain {
    static let service = "de.lunaos.lumail"

    private static func baseQuery(_ accountId: UUID) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: accountId.uuidString,
        ]
    }

    static func save(_ secret: AccountSecret, for accountId: UUID) throws {
        let data = try JSONEncoder().encode(secret)
        let query = baseQuery(accountId)
        let attributes: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecAttrLabel as String: "Lu-Mail",
        ]
        let status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            var item = query
            item.merge(attributes) { _, new in new }
            let addStatus = SecItemAdd(item as CFDictionary, nil)
            guard addStatus == errSecSuccess else { throw KeychainError.status(addStatus) }
        } else if status != errSecSuccess {
            throw KeychainError.status(status)
        }
    }

    static func load(for accountId: UUID) throws -> AccountSecret? {
        var query = baseQuery(accountId)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw KeychainError.status(status) }
        return try JSONDecoder().decode(AccountSecret.self, from: data)
    }

    static func delete(for accountId: UUID) {
        SecItemDelete(baseQuery(accountId) as CFDictionary)
    }
}
