using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            var viewModel = ResolveViewModel(param);
            if (viewModel is null)
            {
                return null;
            }

            var name = viewModel.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return ResolveViewModel(data) is not null;
        }

        private static ViewModelBase? ResolveViewModel(object? data)
        {
            return data switch
            {
                ViewModelBase viewModel => viewModel,
                IDockable { Context: ViewModelBase contextViewModel } => contextViewModel,
                _ => null
            };
        }
    }
}
