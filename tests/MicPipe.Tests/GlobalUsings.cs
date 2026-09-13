global using Xunit;

// TempAppRoot redirects the process-wide AppSettings.RootDirOverride and the cable tests share real audio devices,
// so test classes must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
