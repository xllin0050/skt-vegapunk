# 專案結構

skt-vegapunk/
├── global.json                        ← 釘選 SDK 10.0.103
├── Directory.Build.props              ← 共用屬性（TargetFramework、Nullable、TreatWarningsAsErrors…）
├── SktVegapunk.slnx                   ← 方案（含三個專案）
├── SktVegapunk.Console/
│   └── SktVegapunk.Console.csproj    ← OutputType=Exe + ref Core
├── SktVegapunk.Core/
│   └── SktVegapunk.Core.csproj       ← 類別庫（最精簡）
└── SktVegapunk.Tests/
    ├── SktVegapunk.Tests.csproj       ← xUnit + ref Core
    └── UnitTest1.cs
