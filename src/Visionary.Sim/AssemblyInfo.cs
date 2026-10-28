using System.Runtime.CompilerServices;

// テストが SimHost.World などの internal を直接読み書きして前提状態を作れるようにする(W5-04)。
[assembly: InternalsVisibleTo("Visionary.Sim.Tests")]
