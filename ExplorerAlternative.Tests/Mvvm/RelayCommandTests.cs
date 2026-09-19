using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.Tests.Mvvm;

public sealed class RelayCommandTests
{
    [Fact]
    public void CanExecute_DefaultsToTrue_WhenNoPredicateGiven()
    {
        var sut = new RelayCommand(_ => { });

        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public void CanExecute_ReflectsPredicate()
    {
        var allowed = false;
        var sut = new RelayCommand(_ => { }, _ => allowed);

        Assert.False(sut.CanExecute(null));
        allowed = true;
        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public void Execute_InvokesActionWithParameter()
    {
        object? received = null;
        var sut = new RelayCommand(p => received = p);

        sut.Execute("hello");

        Assert.Equal("hello", received);
    }

    [Fact]
    public void ParameterlessOverload_IgnoresParameterAndUsesFuncCanExecute()
    {
        var executed = false;
        var sut = new RelayCommand(() => executed = true, () => true);

        Assert.True(sut.CanExecute("ignored"));
        sut.Execute("ignored");

        Assert.True(executed);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenExecuteIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action<object?>)null!));
    }
}
