using ElegantSeries.Flow.WPF.Views;
namespace ElegantSeries.Flow.WPF.Tests.Windows;

/// <summary>
/// Windows-only tests for <see cref="BaseView{TViewModel}"/>.
/// </summary>
public sealed class BaseViewTests
{
    private sealed class TestView : BaseView<StubViewModel>
    {
        public List<(StubViewModel? Old, StubViewModel? New)> Changes { get; } = new();

        // Exposed for assertions.
        public new StubViewModel? ViewModel => base.ViewModel;

        protected override void OnViewModelChanged(StubViewModel? oldValue, StubViewModel? newValue)
        {
            Changes.Add((oldValue, newValue));
        }
    }

    [Fact]
    public void DataContextChange_InvokesHookExactlyOncePerChange()
    {
        StaHelper.Run(() =>
        {
            var view = new TestView();
            var vm1 = new StubViewModel();
            var vm2 = new StubViewModel();

            view.DataContext = vm1;
            view.DataContext = vm2;
            view.DataContext = vm2; // Same value: no additional call.
            view.DataContext = null;

            Assert.Equal(3, view.Changes.Count);

            Assert.Null(view.Changes[0].Old);
            Assert.Same(vm1, view.Changes[0].New);

            Assert.Same(vm1, view.Changes[1].Old);
            Assert.Same(vm2, view.Changes[1].New);

            Assert.Same(vm2, view.Changes[2].Old);
            Assert.Null(view.Changes[2].New);

            Assert.Null(view.ViewModel);
        });
    }

    [Fact]
    public void NonViewModelDataContext_DoesNotInvokeHook()
    {
        StaHelper.Run(() =>
        {
            var view = new TestView();

            view.DataContext = "not a view model";

            Assert.Empty(view.Changes);
            Assert.Null(view.ViewModel);
        });
    }

    [Fact]
    public void ViewModel_ReflectsCurrentDataContext()
    {
        StaHelper.Run(() =>
        {
            var view = new TestView();
            var vm = new StubViewModel();

            Assert.Null(view.ViewModel);

            view.DataContext = vm;

            Assert.Same(vm, view.ViewModel);
        });
    }
}
