# Embedded search engine dependency

AFMC FIND manages an unmodified, separate Jackett 0.24.3009 process over its local HTTP API. The application creates a private Windows Job Object, dedicated profile, loopback listener and automatically managed credentials. No Windows service, firewall rule, system proxy change, or upstream auto-update is installed. Existing system default HTTP/SOCKS5 proxy is inherited for upstream traffic, while local management bypasses proxies.

- Project: https://github.com/Jackett/Jackett
- License: GNU GPL version 2, included alongside the binary and in licenses/Jackett/LICENSE.
- Release: https://github.com/Jackett/Jackett/releases/tag/v0.24.3009
- Binary: https://github.com/Jackett/Jackett/releases/download/v0.24.3009/Jackett.Binaries.Windows.zip
- Binary SHA256 (official release digest): 2acc0f068629125f2577cc2144e0a47a26b4bd8296a42378a7148a91ca97c968
- Corresponding source: https://codeload.github.com/Jackett/Jackett/zip/refs/tags/v0.24.3009
- Source SHA256 (downloaded archive): d10b224660eabd571eb421a92aedf508715811fd0fb8de38b6b4c70061a7112e
- A full copy of this exact source archive, including upstream build scripts, is distributed as licenses/Jackett/Jackett-source-v0.24.3009.zip. AFMC does not remove upstream copyright notices.

Build prerequisite: download these archives into .tools/jackett/runtime-verified.zip and .tools/jackett/source-verified.zip; verify the hashes. For development tests extract the runtime under .tools/jackett/runtime. Bundle-Engine.ps1 validates both hashes before packaging. No download occurs on end-user startup.

User data: %LOCALAPPDATA%/AFMC/Find/SearchEngine. The directory ACL is limited to the current Windows user and SYSTEM. engine.secret is DPAPI encrypted. The unmodified engine needs its API key in ServerConfig.json while running, and maintains its own configuration and data-protection files in this protected directory; do not describe every upstream file as DPAPI encrypted. No credentials are sent to diagnostics. Main sources.json stores only the managed indexer ID and website; transient API endpoints/keys are resolved at runtime.

Upstream has an automatic migration of %APPDATA%/Jackett. If that old external directory exists AFMC refuses to start the embedded engine rather than allowing migration of unrelated data. Unsupported interactive/captcha schema fields are explicitly blocked in the native configuration form. A source directory entry is not a claim that a site is currently reachable.
