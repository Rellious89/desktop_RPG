// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CodexUsageOverlay",
    platforms: [.macOS(.v14)],
    targets: [.executableTarget(name: "CodexUsageOverlay", resources: [.copy("config.json")])]
)
