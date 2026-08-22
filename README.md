This repository contains several libraries which can be used to debug .NET applications through the CLR debugging API

* DotNet.Debugging.CorApi: The low level interop layer. See [docs/corapi](docs/corapi/README.md).
* DotNet.Debugging.Engine: The debugger engine built on top of DotNet.Debugging.CorApi. See [docs/engine](docs/engine/README.md).
* DotNet.Debugging.Adapter: Debug Adapter Protocol (DAP) frontend for DotNet.Debugging.Engine.
* DotNet.Debugging.Common: Shared helpers (logging, interop, runtime discovery, Android and Apple platform support) used by the other projects.

Attribution
===========

The code in this project is based on _SharpDbg_ by **MattParkerDev**. See [ATTRIBUTION.md](ATTRIBUTION.md).
