#!/usr/bin/env python3
"""Execute the complete pinned Anki client against an in-process URLProtocol; never use a server."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from macos_vocabulary_reference import BASELINE, between, sense_policy_sources, source

CLIENT = 'Cotabby/Services/Review/AnkiConnectClient.swift'
SESSION = 'Cotabby/Services/LanguageLearning/LearningEndpointSession.swift'
WIRE = 'Cotabby/Models/Review/AnkiWireModels.swift'
ENDPOINT = 'Cotabby/Support/Review/AnkiReviewPolicy.swift'
DRIVER = r'''
import Foundation
struct Scenario: Sendable {
    let status: Int, bytes: Data, failure: String
}
final class ProbeRegistry: @unchecked Sendable {
    static let shared = ProbeRegistry()
    private let lock = NSLock()
    private var value = Scenario(status: 200, bytes: Data(), failure: "")
    private var requests: [URLRequest] = []
    func install(_ value: Scenario) { lock.lock(); defer { lock.unlock() }; self.value = value; requests = [] }
    func accept(_ request: URLRequest) -> Scenario { lock.lock(); defer { lock.unlock() }; requests.append(request); return value }
    func snapshot() -> [URLRequest] { lock.lock(); defer { lock.unlock() }; return requests }
}
final class ProbeProtocol: URLProtocol, @unchecked Sendable {
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        let value = ProbeRegistry.shared.accept(request)
        if !value.failure.isEmpty {
            client!.urlProtocol(self, didFailWithError: value.failure == "cancel" ? CancellationError() : URLError(.cannotConnectToHost)); return
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: value.status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
        client!.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client!.urlProtocol(self, didLoad: value.bytes)
        client!.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() { }
}
func obj(_ pairs: [String: AnkiValue]) -> AnkiValue { .object(pairs) }
func value(_ row: [String: AnkiValue], _ name: String, _ fallback: String = "") -> String { row[name]?.string ?? fallback }
func count(_ row: [String: AnkiValue], _ name: String, _ fallback: Int = 0) -> Int {
    if case let .number(n) = row[name] { return Int(n) }; return fallback
}
func payload(_ row: [String: AnkiValue]) -> AnkiValue {
    if let repeatCount = row["repeatCount"] { return .string(String(repeating: value(row, "repeatText", "a"), count: count(["x": repeatCount], "x"))) }
    if let depth = row["depth"] {
        var v: AnkiValue = row["emptyLeaf"] == .bool(true) ? .array([]) : .null
        for _ in 0..<count(["x": depth], "x") { v = .array([v]) }; return v
    }
    if let number = row["specialNumber"]?.string { return .number(number == "nan" ? .nan : .infinity) }
    return row["params"] ?? .object([:])
}
func response(_ row: [String: AnkiValue]) -> Data {
    if let base64 = row["responseBytes"]?.string { return Data(base64Encoded: base64)! }
    if let size = row["responseSize"] {
        let base = "{\"error\":null,\"result\":null}"; return Data((base + String(repeating: " ", count: count(["x": size], "x") - base.utf8.count)).utf8)
    }
    return Data(value(row, "response", "{\"error\":null,\"result\":6}").utf8)
}
func observe(_ row: [String: AnkiValue]) async -> AnkiValue {
    let kind = value(row, "kind", "perform")
    if kind == "endpoint" { return obj(["endpoint": AnkiReviewPolicy.endpoint(value(row, "endpoint")).map { .string($0.absoluteString) } ?? .null]) }
    if kind == "encode" {
        do {
            let bytes = try await AnkiConnectClient().encodedProbe(["payload": payload(row)])
            var result: [String: AnkiValue] = ["valid": .bool(true), "count": .number(Double(bytes.count))]
            if bytes.count < 4096 { result["json"] = try JSONDecoder().decode(AnkiValue.self, from: bytes); result["raw"] = .string(String(data: bytes, encoding: .utf8)!) }
            return obj(result)
        } catch is EncodingError { return obj(["valid": .bool(false), "kind": .string("encoding")]) }
        catch { return obj(["valid": .bool(false), "error": .string(error.localizedDescription)]) }
    }
    let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [ProbeProtocol.self]; configuration.httpShouldSetCookies = false
    let session = URLSession(configuration: configuration, delegate: LearningEndpointSession(), delegateQueue: nil)
    defer { session.invalidateAndCancel() }
    ProbeRegistry.shared.install(.init(status: count(row, "status", 200), bytes: response(row), failure: value(row, "failure")))
    let client = AnkiConnectClient(session: session)
    let endpoint = URL(string: value(row, "endpoint", "http://127.0.0.1:8765"))!
    var result: [String: AnkiValue]
    do {
        let answer = try await client.perform(value(row, "action", "version"), params: payload(row).object ?? ["payload": payload(row)],
                                              connection: .init(endpoint: endpoint, apiKey: value(row, "key")))
        result = ["valid": .bool(true), "result": answer]
    } catch is CancellationError { result = ["valid": .bool(false), "kind": .string("cancelled")] }
    catch is EncodingError { result = ["valid": .bool(false), "kind": .string("encoding")] }
    catch { result = ["valid": .bool(false), "error": .string(error.localizedDescription)] }
    let requests = ProbeRegistry.shared.snapshot(); result["requests"] = .number(Double(requests.count))
    if let request = requests.first {
        result["method"] = .string(request.httpMethod ?? ""); result["contentType"] = .string(request.value(forHTTPHeaderField: "Content-Type") ?? "")
        result["timeout"] = .number(request.timeoutInterval)
        var bytes = request.httpBody
        if bytes == nil, let stream = request.httpBodyStream {
            stream.open(); defer { stream.close() }; var data = Data(); var buffer = [UInt8](repeating: 0, count: 8192)
            while true { let n = stream.read(&buffer, maxLength: buffer.count); if n <= 0 { break }; data.append(contentsOf: buffer[0..<n]) }; bytes = data
        }
        if let bytes { result["bodyCount"] = .number(Double(bytes.count)); if bytes.count < 4096 { result["body"] = try! JSONDecoder().decode(AnkiValue.self, from: bytes) } }
    }
    return obj(result)
}
@main struct Main {
    static func main() async throws {
        let corpus = try JSONDecoder().decode(AnkiValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
        var results: [AnkiValue] = []
        for item in corpus.object!["cases"]!.array! {
            let row = item.object!; if ProcessInfo.processInfo.environment["ANKI_PROBE_PROGRESS"] != nil { FileHandle.standardError.write(Data((row["id"]!.string! + "\n").utf8)) }; results.append(obj(["id": row["id"]!, "expected": await observe(row)]))
        }
        let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]; FileHandle.standardOutput.write(try encoder.encode(AnkiValue.array(results)))
    }
}
'''

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mac-repo', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, default=Path(__file__).resolve().parents[1] / 'fixtures/parity/anki-connect-v1.json')
    parser.add_argument('--capture-output', type=Path)
    args = parser.parse_args()
    corpus = json.loads(args.fixture.read_text(encoding='utf-8')); rows = corpus['cases']
    if corpus.get('version') != 1 or corpus.get('macReference') != BASELINE or not rows or len({r['id'] for r in rows}) != len(rows): parser.error('Pinned corpus with unique IDs required')
    for row in rows:
        if row.get('macSource') not in (CLIENT, ENDPOINT) or row.get('provenanceKind') != 'source-policy-extension': parser.error('Explicit original source provenance required')
    values = sense_policy_sources(args.mac_repo)
    endpoint = source(args.mac_repo, ENDPOINT)
    values += ['import Foundation\nextension AnkiReviewPolicy {\n' + between(endpoint, '    static func endpoint(', '    static func fields(') + '}\n', source(args.mac_repo, WIRE), source(args.mac_repo, SESSION)]
    speech = source(args.mac_repo, 'Cotabby/Models/Speech/LearningSpeechModels.swift')
    values += ['import Foundation\n' + between(speech, 'nonisolated enum SpeechError:', '/// The system catalog')]
    # A same-file probe grants access to the original private encoder; its body is unchanged.
    values += [source(args.mac_repo, CLIENT) + '\nextension AnkiConnectClient { func encodedProbe(_ body: [String: AnkiValue]) throws -> Data { try encodedBody(body) } }\n']
    with tempfile.TemporaryDirectory(prefix='lingrove-anki-connect-') as temp:
        root = Path(temp); files = []
        for i, text in enumerate(values + [DRIVER]):
            path = root / f'source{i}.swift'; path.write_text(text, encoding='utf-8'); files.append(str(path))
        binary = root / 'anki-connect'
        subprocess.run(['swiftc', '-swift-version', '6', '-O', *files, '-o', str(binary)], check=True)
        observed = json.loads(subprocess.check_output([str(binary), str(args.fixture)], timeout=120))
    if args.capture_output:
        with args.capture_output.open('x', encoding='utf-8') as out: json.dump(observed, out, ensure_ascii=False, indent=2); out.write('\n')
        print(f'Captured {len(observed)} actual Mac Anki client observations'); return
    if len(observed) != len(rows): raise SystemExit('Anki observation count changed')
    for row, actual in zip(rows, observed):
        if actual != {'id': row['id'], 'expected': row['expected']}: raise SystemExit(f'Mac Anki client mismatch: {row["id"]}')
    print(f'Verified {len(rows)} cases against complete original Anki client with in-process URLProtocol; no network')

if __name__ == '__main__': main()
