using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

public abstract class ViewModelBase : ObservableObject, System.IDisposable
{
    /// <summary>
    /// Releases all resources held by the ViewModel. Derived classes should override
    /// this method to clear collections, unsubscribe events, and null references.
    /// </summary>
    public virtual void Dispose()
    {
    }
}