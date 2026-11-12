# NetMQ (ZeroMQ for .NET) for the Pupil eye-tracker connection

Used by `Scripts/Twin/PupilGazeSource.cs` and `FakePupilServer.cs`. Unmodified binaries from NuGet.

| File | Package | Version | Target | Licence | SHA-256 |
|---|---|---|---|---|---|
| NetMQ.dll | [NetMQ](https://www.nuget.org/packages/NetMQ/4.0.0.1) (source: https://github.com/zeromq/netmq) | 4.0.0.1 | net40 | LGPL-3.0 (`LICENSE-NetMQ-LGPL-3.0.txt`, which builds on `LICENSE-GPL-3.0.txt`) | ce9b347a4cf35c4ed96e1343f3ce84ce6514be680347e59de0f5a29f6ee6fba1 |
| AsyncIO.dll | [AsyncIO](https://www.nuget.org/packages/AsyncIO/0.1.69) (source: https://github.com/somdoron/AsyncIO) | 0.1.69 | net40 | MPL-2.0 (`LICENSE-AsyncIO-MPL-2.0.txt`) | 7124e586dda40a009fcf31eb3f224f6d378af1116fca76ceb31770d93c0cc0c7 |

- These are the same two DLLs that Pupil Labs ship in their Unity plugin hmd-eyes (`plugin/3rd-party/`): byte-identical, checked 2026-11-12.
- Newer NetMQ versions (4.0.1 and later) also need NaCl.Net and several System.* packages. We use 4.0.0.1 to keep the Unity setup simple. So **NaCl.Net is not included**.
- **Unity setting needed:** Project Settings → Player → Api Compatibility Level = **.NET Framework**. NetMQ.dll references System.ServiceModel; see `Docs/EyeTracking.md` section 2.
- The LGPL allows using the unmodified library from our code. Keep this folder (DLLs, licences, README) together.
