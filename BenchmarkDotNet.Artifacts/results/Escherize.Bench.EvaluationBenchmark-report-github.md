```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 9.0.315
  [Host]     : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2


```
| Method   | Template | PointCount | Mean     | Error   | StdDev   | Median   | Allocated |
|--------- |--------- |----------- |---------:|--------:|---------:|---------:|----------:|
| **Evaluate** | **IH1**      | **120**        | **191.9 ns** | **7.71 ns** | **22.72 ns** | **180.4 ns** |         **-** |
| **Evaluate** | **IH4**      | **120**        | **294.3 ns** | **5.79 ns** |  **9.51 ns** | **294.5 ns** |         **-** |
| **Evaluate** | **IH6**      | **120**        | **315.4 ns** | **5.94 ns** | **10.86 ns** | **314.2 ns** |         **-** |
