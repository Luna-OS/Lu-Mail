import Foundation

/// IMAP-Ordnernamen sind in „modified UTF-7“ kodiert (RFC 3501, 5.1.3) – z. B. „Отправленные“ bei Mail.ru.
public enum ModifiedUTF7 {
    public static func encode(_ text: String) -> String {
        var result = ""
        var pending: [UInt16] = []

        func flush() {
            guard !pending.isEmpty else { return }
            var bytes = Data()
            for unit in pending {
                bytes.append(UInt8(unit >> 8))
                bytes.append(UInt8(unit & 0xFF))
            }
            let base64 = bytes.base64EncodedString()
                .replacingOccurrences(of: "=", with: "")
                .replacingOccurrences(of: "/", with: ",")
            result += "&" + base64 + "-"
            pending.removeAll()
        }

        for scalar in text.unicodeScalars {
            if scalar.value >= 0x20 && scalar.value <= 0x7E {
                flush()
                result += scalar == "&" ? "&-" : String(scalar)
            } else {
                pending.append(contentsOf: Array(String(scalar).utf16))
            }
        }
        flush()
        return result
    }

    public static func decode(_ text: String) -> String {
        var result = ""
        var index = text.startIndex
        while index < text.endIndex {
            let char = text[index]
            guard char == "&" else {
                result.append(char)
                index = text.index(after: index)
                continue
            }
            guard let end = text[index...].firstIndex(of: "-") else {
                result += text[index...]
                break
            }
            let encoded = text[text.index(after: index)..<end]
            if encoded.isEmpty {
                result += "&"
            } else {
                var base64 = encoded.replacingOccurrences(of: ",", with: "/")
                while base64.count % 4 != 0 { base64 += "=" }
                if let data = Data(base64Encoded: base64) {
                    var units: [UInt16] = []
                    var bytes = Array(data)
                    if bytes.count % 2 == 1 { bytes.removeLast() }
                    for i in stride(from: 0, to: bytes.count, by: 2) {
                        units.append(UInt16(bytes[i]) << 8 | UInt16(bytes[i + 1]))
                    }
                    result += String(decoding: units, as: UTF16.self)
                } else {
                    result += "&" + encoded + "-"
                }
            }
            index = text.index(after: end)
        }
        return result
    }
}
