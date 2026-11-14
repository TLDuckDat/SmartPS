using System.ComponentModel;
using SmartPS.ViewModels;

namespace SmartPS.Tests.Unit;

public class ValidatableViewModelBaseTests
{
    private sealed class Sample : ValidatableViewModelBase
    {
        public void Set(string prop, params string[] errors) => SetErrors(prop, errors);
        public void Clear(string prop) => ClearErrors(prop);
        public void Validate(string prop, string? error) => ValidateProperty(prop, error);
    }

    [Fact]
    public void New_instance_has_no_errors()
    {
        var vm = new Sample();
        Assert.False(vm.HasErrors);
        Assert.Empty(vm.GetErrors("A").Cast<object>());
    }

    [Fact]
    public void SetErrors_adds_errors_and_raises_events()
    {
        var vm = new Sample();
        var changed = new List<string?>();
        var props = new List<string?>();
        vm.ErrorsChanged += (_, e) => changed.Add(e.PropertyName);
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => props.Add(e.PropertyName);

        vm.Set("A", "loi 1");

        Assert.True(vm.HasErrors);
        Assert.Equal(new[] { "loi 1" }, vm.GetErrors("A").Cast<string>());
        Assert.Equal(new string?[] { "A" }, changed);
        Assert.Contains(nameof(ValidatableViewModelBase.HasErrors), props);
    }

    [Fact]
    public void ClearErrors_removes_errors_and_HasErrors_follows_other_properties()
    {
        var vm = new Sample();
        vm.Set("A", "x");
        vm.Set("B", "y");

        vm.Clear("A");
        Assert.True(vm.HasErrors);
        Assert.Empty(vm.GetErrors("A").Cast<object>());

        vm.Clear("B");
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void ClearErrors_on_clean_property_raises_nothing()
    {
        var vm = new Sample();
        var raised = 0;
        vm.ErrorsChanged += (_, _) => raised++;
        vm.Clear("A");
        Assert.Equal(0, raised);
    }

    [Fact]
    public void ValidateProperty_sets_then_clears()
    {
        var vm = new Sample();
        vm.Validate("A", "bat buoc");
        Assert.True(vm.HasErrors);
        vm.Validate("A", null);
        Assert.False(vm.HasErrors);
        vm.Validate("A", "  ");
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void SetErrors_with_empty_list_clears()
    {
        var vm = new Sample();
        vm.Set("A", "x");
        vm.Set("A");
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void GetErrors_null_returns_all_errors()
    {
        var vm = new Sample();
        vm.Set("A", "x");
        vm.Set("B", "y");
        Assert.Equal(2, vm.GetErrors(null).Cast<string>().Count());
    }
}
