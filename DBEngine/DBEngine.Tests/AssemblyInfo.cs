using Xunit;

// Database is a process-wide singleton (only one can be open at a time), so tests that open
// a database must not run concurrently with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
