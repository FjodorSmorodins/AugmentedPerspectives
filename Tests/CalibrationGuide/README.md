Run `dotnet run --project Tests/CalibrationGuide/CalibrationGuide.Tests.csproj` from the repository root with the .NET 10 SDK installed. No packages or network access are needed.

The runner compiles CalibrationGuide and CalibrationSession from the Unity project. It checks both players' checkpoints, point edits, final approval invalidation, stale approvals, and disconnect recovery. Headset input, rendering, and multiplayer transport require an in-game test.
