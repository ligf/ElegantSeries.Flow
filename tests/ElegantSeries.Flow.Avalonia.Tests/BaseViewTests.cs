using ElegantSeries.Flow.Avalonia.Views;
namespace ElegantSeries.Flow.Avalonia.Tests;

public sealed class BaseViewTests
{
    [Fact]
    public void DataContextSet_CallsOnViewModelChangedOnceWithNullOldValue()
    {
        var view = new RecordingView();
        var vm = new TestViewModel();

        view.DataContext = vm;

        Assert.Single(view.Changes);
        Assert.Null(view.Changes[0].OldValue);
        Assert.Same(vm, view.Changes[0].NewValue);
        Assert.Same(vm, view.CurrentViewModel);
    }

    [Fact]
    public void DataContextSetToSameInstance_DoesNotCallAgain()
    {
        var view = new RecordingView();
        var vm = new TestViewModel();
        view.DataContext = vm;

        view.DataContext = vm;

        Assert.Single(view.Changes);
    }

    [Fact]
    public void DataContextChanged_CallsWithOldAndNewValues()
    {
        var view = new RecordingView();
        var first = new TestViewModel();
        var second = new TestViewModel();
        view.DataContext = first;

        view.DataContext = second;

        Assert.Equal(2, view.Changes.Count);
        Assert.Same(first, view.Changes[1].OldValue);
        Assert.Same(second, view.Changes[1].NewValue);
        Assert.Same(second, view.CurrentViewModel);
    }

    [Fact]
    public void DataContextCleared_CallsWithNullNewValue()
    {
        var view = new RecordingView();
        var vm = new TestViewModel();
        view.DataContext = vm;

        view.DataContext = null;

        Assert.Equal(2, view.Changes.Count);
        Assert.Same(vm, view.Changes[1].OldValue);
        Assert.Null(view.Changes[1].NewValue);
        Assert.Null(view.CurrentViewModel);
    }

    [Fact]
    public void DataContextSetToIncompatibleType_ViewModelIsNull()
    {
        var view = new RecordingView();
        var vm = new TestViewModel();
        view.DataContext = vm;

        view.DataContext = "not a view model";

        Assert.Equal(2, view.Changes.Count);
        Assert.Null(view.Changes[1].NewValue);
        Assert.Null(view.CurrentViewModel);
    }
}
