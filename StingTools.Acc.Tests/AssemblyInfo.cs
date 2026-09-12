// The ACC clients hold static state — a shared HttpClient, and the host/back-off test
// seams. Running collections in parallel would let one test's host override leak into
// another's request. Sequential is correct here, not merely convenient.
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
