import Foundation

/// Eine Konversation: zusammengehörige Nachrichten eines Ordners (docs/FUNKTIONEN.md §8).
public struct Conversation: Identifiable, Hashable, Sendable {
    /// Neueste zuerst.
    public var messages: [MessageSummary]

    public var id: String { messages.last?.id ?? UUID().uuidString }
    public var latest: MessageSummary { messages[0] }
    public var count: Int { messages.count }
    public var hasUnread: Bool { messages.contains { !$0.isSeen } }

    /// Teilnehmer (Absender-Namen), älteste zuerst, ohne Doppelte.
    public var participants: [String] {
        var seen: [String] = []
        for message in messages.reversed() {
            let name = message.from?.displayName ?? "?"
            if !seen.contains(name) { seen.append(name) }
        }
        return seen
    }
}

public enum ConversationBuilder {
    /// „Re: AW: Fwd: Hallo“ → „hallo“.
    public static func normalizedSubject(_ subject: String) -> String {
        var text = subject.trimmingCharacters(in: .whitespaces)
        let prefixes = ["re:", "aw:", "fwd:", "fw:", "wg:", "ответ:", "re :", "sv:", "antw:"]
        var changed = true
        while changed {
            changed = false
            let lower = text.lowercased()
            for prefix in prefixes where lower.hasPrefix(prefix) {
                text = String(text.dropFirst(prefix.count)).trimmingCharacters(in: .whitespaces)
                changed = true
            }
            // „Re[2]:“
            if lower.hasPrefix("re["), let close = text.firstIndex(of: ":") {
                text = String(text[text.index(after: close)...]).trimmingCharacters(in: .whitespaces)
                changed = true
            }
        }
        return text.lowercased()
    }

    /// Gruppiert pro Konto und Ordner über Message-ID/In-Reply-To/References, sonst über Betreff + gemeinsame Teilnehmer.
    public static func group(_ messages: [MessageSummary]) -> [Conversation] {
        var parent = Array(messages.indices)
        func find(_ index: Int) -> Int {
            var index = index
            while parent[index] != index {
                parent[index] = parent[parent[index]]
                index = parent[index]
            }
            return index
        }
        func union(_ a: Int, _ b: Int) {
            let rootA = find(a), rootB = find(b)
            if rootA != rootB { parent[rootB] = rootA }
        }

        func scope(_ message: MessageSummary) -> String { "\(message.accountId.uuidString)|\(message.folderPath)" }

        // 1. Verweise über Message-IDs
        var byMessageID: [String: Int] = [:]
        for (index, message) in messages.enumerated() {
            if let id = message.messageId?.lowercased() { byMessageID[scope(message) + id] = index }
        }
        for (index, message) in messages.enumerated() {
            for reference in message.referencedIDs {
                if let other = byMessageID[scope(message) + reference.lowercased()] { union(index, other) }
            }
        }

        // 2. Gleicher normalisierter Betreff und gemeinsame Teilnehmer (nur für Antworten/Weiterleitungen
        //    oder wenn eine Seite schon eine Antwort ist – verhindert, dass alle „Newsletter“ verschmelzen).
        var bySubject: [String: [Int]] = [:]
        for (index, message) in messages.enumerated() {
            let normalized = normalizedSubject(message.subject)
            guard !normalized.isEmpty else { continue }
            bySubject[scope(message) + normalized, default: []].append(index)
        }
        for indices in bySubject.values where indices.count > 1 {
            for i in indices {
                for j in indices where j > i {
                    let a = messages[i], b = messages[j]
                    let isReply = normalizedSubject(a.subject) != a.subject.lowercased().trimmingCharacters(in: .whitespaces)
                        || normalizedSubject(b.subject) != b.subject.lowercased().trimmingCharacters(in: .whitespaces)
                    if isReply && !a.participantAddresses.isDisjoint(with: b.participantAddresses) { union(i, j) }
                }
            }
        }

        var groups: [Int: [MessageSummary]] = [:]
        for index in messages.indices { groups[find(index), default: []].append(messages[index]) }
        return groups.values
            .map { Conversation(messages: $0.sorted { $0.date > $1.date }) }
            .sorted { $0.latest.date > $1.latest.date }
    }
}

extension MessageSummary {
    /// In-Reply-To und References (ohne spitze Klammern-Unterschiede).
    var referencedIDs: [String] {
        var ids: [String] = []
        if let inReplyTo { ids.append(contentsOf: Self.extractIDs(inReplyTo)) }
        if let references { ids.append(contentsOf: Self.extractIDs(references)) }
        return ids
    }

    var participantAddresses: Set<String> {
        Set(([from].compactMap { $0 } + to + cc).map { $0.address.lowercased() })
    }

    static func extractIDs(_ text: String) -> [String] {
        var ids: [String] = []
        var rest = Substring(text)
        while let open = rest.firstIndex(of: "<"), let close = rest[open...].firstIndex(of: ">") {
            ids.append(String(rest[open...close]))
            rest = rest[rest.index(after: close)...]
        }
        return ids
    }
}
