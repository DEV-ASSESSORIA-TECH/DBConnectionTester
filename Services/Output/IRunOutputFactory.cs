using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Output;

public interface IRunOutputFactory
{
    Task<IRunOutput> CreateAsync(TestSettings settings);
}

public sealed class RunOutputFactory : IRunOutputFactory
{
    public async Task<IRunOutput> CreateAsync(TestSettings settings) =>
        await RunOutputSession.CreateAsync(settings);
}
