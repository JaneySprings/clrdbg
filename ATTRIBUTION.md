# Attribution

The code in this project is based on **SharpDbg** by MattParkerDev:
https://github.com/MattParkerDev/sharpdbg (commit e3f2298746c619b16a7d683d39f1fe01aa5ef8b8).

Original license:

```
MIT License

Copyright (c) 2026 Matthew Parker

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Roslyn

`DotNet.Debugging.Evaluation/Roslyn/src` is a verbatim copy of the expression compiler sources of
**Roslyn** by the .NET Foundation and contributors: https://github.com/dotnet/roslyn (the commit is
recorded in `DotNet.Debugging.Evaluation/Roslyn/Roslyn.props`), licensed under MIT
(`DotNet.Debugging.Evaluation/Roslyn/License.txt`). The way the Visual Studio debugger dependency is
removed (`DotNet.Debugging.Evaluation/Shims`) follows the
`MP.Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler` package by MattParkerDev.
