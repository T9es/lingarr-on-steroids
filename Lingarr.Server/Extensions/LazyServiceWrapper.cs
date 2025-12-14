namespace Lingarr.Server.Extensions;

/// <summary>
/// A wrapper class to enable Lazy<T> resolution from the DI container.
/// This is used to break circular dependencies by deferring service resolution until first use.
/// </summary>
/// <typeparam name="T">The service type to lazily resolve</typeparam>
public class LazyServiceWrapper<T> : Lazy<T> where T : class
{
    public LazyServiceWrapper(IServiceProvider serviceProvider)
        : base(() => serviceProvider.GetRequiredService<T>())
    {
    }
}
