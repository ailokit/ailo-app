using System.Collections.Concurrent;

namespace Ailo.Services;

public class LazyServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _serviceProvider;
    protected ConcurrentDictionary<Type, Lazy<object?>> CachedServices { get; } = [];

    public LazyServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        CachedServices.TryAdd(typeof(IServiceProvider), new Lazy<object?>(() => _serviceProvider));
    }

    public virtual object? GetService(Type serviceType)
    {
        return CachedServices.GetOrAdd(
            serviceType,
            _ => new Lazy<object?>(() => _serviceProvider.GetService(serviceType))
        ).Value;
    }

    public T GetService<T>(T defaultValue)
    {
        return (T)GetService(typeof(T), defaultValue!);
    }

    public object GetService(Type serviceType, object defaultValue)
    {
        return GetService(serviceType) ?? defaultValue;
    }

    public T GetService<T>(Func<IServiceProvider, object> factory)
    {
        return (T)GetService(typeof(T), factory);
    }

    public object GetService(Type serviceType, Func<IServiceProvider, object> factory)
    {
        return CachedServices.GetOrAdd(
            serviceType,
            _ => new Lazy<object?>(() => factory(_serviceProvider))
        ).Value!;
    }
}