# Third-Party Notices

This file summarizes principal third-party components used by the repository.
Release packaging must include the complete license texts required by each
distributed binary and its transitive dependencies.

- Avalonia UI - MIT
- CommunityToolkit.Mvvm - MIT
- Dock - MIT
- Microsoft.Data.Sqlite - MIT
- SQLite - public domain
- SQLitePCLRaw - Apache-2.0
- Silk.NET - MIT
- Vortice.Windows - MIT
- XenoAtom.ShaderCompiler - MIT

The signaling service interoperates with coturn through its REST/HMAC temporary
credential protocol. coturn is not bundled or distributed by this repository;
operators who deploy it must satisfy coturn's own license and notice obligations.

FFmpeg, libx264, libx265, and libwebrtc binaries are not distributed by the
current product. A future pinned libwebrtc runtime requires its BSD-3-Clause
license, `PATENTS`, `AUTHORS`, and complete transitive native notices before
packaging.

`WTK.MediaForge.Remote.WebRtc.Native/native-supply-chain.json` records the
reviewed libwebrtc/depot_tools pins and required notice set. The checked-in C++
contract target is not a libwebrtc binary and must not be represented as one.

