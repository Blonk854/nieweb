using Microsoft.Extensions.Options;

namespace Nieweb.Identity.Tests;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> stub returning a fixed value.
/// The Argon2id hasher only reads <see cref="IOptionsMonitor{T}.CurrentValue"/>;
/// change notifications are irrelevant for unit testing.
/// </summary>
internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    where T : class
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
